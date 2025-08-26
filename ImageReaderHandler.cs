using Ionic.Zip;
using Ionic.Zlib;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PickPack.Disk
{
    public interface IImageReaderHandler
    {
        Task WriteImageAsync(Stream sourceStream, string outputPath, long totalSize, CancellationToken cancellationToken);
    }

    public class ZipReadHandler : IImageReaderHandler
    {
        #region Field

        readonly long maxOutputSegmentSize;
        readonly CompressionLevel compressionLevel;
        readonly Action<int, string, string?> progressCallback;
        readonly CancellationToken cancellationToken;

        #endregion

        #region Constructor

        public ZipReadHandler(long maxOutputSegmentSize, CompressionLevel compressionLevel, Action<int, string, string?> progressCallback, CancellationToken cancellationToken)
        {
            this.maxOutputSegmentSize = maxOutputSegmentSize;
            this.compressionLevel = compressionLevel;
            this.progressCallback = progressCallback;
            this.cancellationToken = cancellationToken;
        }

        #endregion

        public async Task WriteImageAsync(Stream sourceStream, string outputPath, long totalSize, CancellationToken cancellationToken)
        {
            using var zipFile = new ZipFile();

            zipFile.UseZip64WhenSaving = Zip64Option.AsNecessary;
            zipFile.CompressionLevel = compressionLevel;
            zipFile.MaxOutputSegmentSize64 = maxOutputSegmentSize;
            zipFile.BufferSize = Optimal.BufferSize;
            zipFile.CodecBufferSize = Optimal.BufferSize;
            zipFile.ParallelDeflateThreshold = long.MaxValue;
            zipFile.AlternateEncoding = null;
            zipFile.AlternateEncodingUsage = ZipOption.Never;
            zipFile.Strategy = CompressionStrategy.Default;

            string imgFileName = $"{Path.GetFileNameWithoutExtension(outputPath)}.img";
            var optimizedStream = new DirectReadStream(sourceStream as FileStream, totalSize);

            zipFile.SaveProgress += (sender, e) => OnZipSaveProgress(e);
            zipFile.AddEntry(imgFileName, optimizedStream);

            await Task.Run(() => zipFile.Save(outputPath), cancellationToken);
        }

        private void OnZipSaveProgress(SaveProgressEventArgs e)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (e.EventType == ZipProgressEventType.Saving_EntryBytesRead && e.TotalBytesToTransfer > 0)
            {
                int percent = (int)((double)e.BytesTransferred / e.TotalBytesToTransfer * 100);
                progressCallback(percent, "이미지 저장 중...", null);
            }
        }

        #region Inner Class

        private class DirectReadStream : Stream
        {
            readonly Stream baseStream;
            readonly long length;
            long position = 0;
            bool disposed = false;

            public DirectReadStream(Stream baseStream, long length)
            {
                this.baseStream = baseStream;
                this.length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => this.length;

            public override int Read(byte[] buffer, int offset, int count)
            {
                long remaining = this.length - this.position;
                if (remaining <= 0) return 0;

                int bytesToRead = (int)Math.Min(count, remaining);

                try
                {
                    int bytesRead = baseStream.Read(buffer, offset, bytesToRead);
                    this.position += bytesRead;
                    return bytesRead;
                }
                catch (IOException)
                {
                    Array.Clear(buffer, offset, bytesToRead);
                    this.position += bytesToRead;
                    return bytesToRead;
                }
            }

            public override long Position
            {
                get => this.position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        this.position = offset;
                        break;
                    case SeekOrigin.Current:
                        this.position += offset;
                        break;
                    case SeekOrigin.End:
                        this.position = this.length + offset;
                        break;
                }

                baseStream.Seek(this.position, SeekOrigin.Begin);
                return this.position;
            }

            protected override void Dispose(bool disposing)
            {
                if (!this.disposed && disposing)
                {
                    this.disposed = true;
                }
                base.Dispose(disposing);
            }

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => baseStream.Flush();
        }

        #endregion
    }

    public class RawImageReadHandler : IImageReaderHandler
    {
        #region Field & Const

        readonly Action<long, long, string> progressReporter;
        readonly CancellationToken cancellationToken;

        const int ERROR_CRC = unchecked((int)0x80070017);
        const int ERROR_SECTOR_NOT_FOUND = unchecked((int)0x8007001B);

        #endregion

        #region Constructor

        public RawImageReadHandler(Action<long, long, string> progressReporter, CancellationToken cancellationToken)
        {
            this.progressReporter = progressReporter;
            this.cancellationToken = cancellationToken;
        }

        #endregion

        public async Task WriteImageAsync(Stream sourceStream, string outputPath, long totalSize, CancellationToken cancellationToken)
        {
            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Optimal.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var producerTask = ProduceDataAsync(sourceStream, channel.Writer, totalSize, cancellationToken);
            var consumerTask = ConsumeDataAsync(channel.Reader, outputPath, totalSize, cancellationToken);

            await Task.WhenAll(producerTask, consumerTask);
        }

        private async Task ProduceDataAsync(Stream sourceStream, ChannelWriter<byte[]> writer, long totalSize, CancellationToken cancellationToken)
        {
            byte[] rentedBuffer = Optimal.ArrayPool.Rent(Optimal.BufferSize);
            long totalRead = 0;

            try
            {
                while (totalRead < totalSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int read;
                    try
                    {
                        read = await sourceStream.ReadAsync(rentedBuffer, 0, Optimal.BufferSize, cancellationToken);
                        if (read == 0) break;
                    }
                    catch (IOException ex) when (ex.HResult == ERROR_CRC || ex.HResult == ERROR_SECTOR_NOT_FOUND)
                    {
                        Array.Clear(rentedBuffer, 0, Optimal.BufferSize);
                        read = Optimal.BufferSize;
                    }

                    byte[] dataToSend = new byte[read];
                    Array.Copy(rentedBuffer, dataToSend, read);

                    await writer.WriteAsync(dataToSend, cancellationToken);
                    totalRead += read;
                }

                if (totalSize > totalRead)
                    await writer.WriteAsync(Array.Empty<byte>(), cancellationToken);
            }
            finally
            {
                Optimal.ArrayPool.Return(rentedBuffer);
                writer.Complete();
            }
        }

        private async Task ConsumeDataAsync(ChannelReader<byte[]> reader, string outputPath, long totalSize, CancellationToken cancellationToken)
        {
            long totalWritten = 0;
            long zeroBytesWritten = 0;

            using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                Optimal.BufferSize, FileOptions.Asynchronous);

            await foreach (var buffer in reader.ReadAllAsync(cancellationToken))
            {
                if (buffer.Length == 0)
                {
                    // 남은 바이트를 0으로 채우기
                    long bytesRemaining = totalSize - totalWritten;
                    while (bytesRemaining > 0)
                    {
                        int writeCount = (int)Math.Min(Optimal.BufferSize, bytesRemaining);
                        await outStream.WriteAsync(Optimal.ZeroBuffer, 0, writeCount, cancellationToken);
                        bytesRemaining -= writeCount;
                        zeroBytesWritten += writeCount;

                        progressReporter(totalWritten + zeroBytesWritten, totalSize, "이미지 저장 중...");
                    }
                }
                else
                {
                    await outStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                    totalWritten += buffer.Length;
                    progressReporter(totalWritten, totalSize, "이미지 저장 중...");
                }
            }
        }
    }

    public static class ImageReaderFactory
    {
        public static IImageReaderHandler? GetHandler(string extension, long maxSegmentSize, CompressionLevel compressionLevel,
            Action<int, string, string?> progressCallback, Action<long, long, string> rawProgressReporter, CancellationToken cancellationToken)
        {
            return extension.ToLowerInvariant() switch
            {
                ".zip" => new ZipReadHandler(maxSegmentSize, compressionLevel, progressCallback, cancellationToken),
                ".img" => new RawImageReadHandler(rawProgressReporter, cancellationToken),
                _ => null
            };
        }

        public static bool IsSupported(string extension)
        {
            return extension.ToLowerInvariant() is ".zip" or ".img";
        }

        public static string[] GetSupportedExtensions()
        {
            return new[] { ".zip", ".img" };
        }
    }
}