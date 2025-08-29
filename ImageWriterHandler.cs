using ICSharpCode.SharpZipLib.GZip;
using SharpCompress.Archives;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PickPack.Disk
{
    public interface IImageWriteHandler
    {
        Task<(Stream stream, long length)> OpenStreamAsync(string imagePath, CancellationToken cancellationToken);
    }

    public class ZipWriteHandler : IImageWriteHandler
    {
        public async Task<(Stream stream, long length)> OpenStreamAsync(string imagePath, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var zipFile = Ionic.Zip.ZipFile.Read(imagePath);

            try
            {
                var entry = zipFile.Entries.FirstOrDefault(e => e.FileName.EndsWith(".img", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    throw new InvalidOperationException("ZIP 파일 안에 IMG 파일이 없습니다.");

                var entryStream = entry.OpenReader();
                var wrapperStream = new CompositeDisposableStream(entryStream, zipFile);
                return (wrapperStream, entry.UncompressedSize);
            }
            catch
            {
                zipFile.Dispose();
                throw;
            }
        }
    }

    public class SevenZipWriteHandler : IImageWriteHandler
    {
        public async Task<(Stream stream, long length)> OpenStreamAsync(string imagePath, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var archive = ArchiveFactory.Open(imagePath);

            try
            {
                var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.Key.EndsWith(".img", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    throw new InvalidOperationException("7z 파일 안에 IMG 파일이 없습니다.");

                var entryStream = entry.OpenEntryStream();
                var wrapperStream = new CompositeDisposableStream(entryStream, archive);
                return (wrapperStream, entry.Size);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }
    }

    public class GzipWriteHandler : IImageWriteHandler
    {
        readonly Action<int, string, string?> progressCallback;

        public GzipWriteHandler(Action<int, string, string?> progressCallback)
        {
            this.progressCallback = progressCallback;
        }

        public async Task<(Stream stream, long length)> OpenStreamAsync(string imagePath, CancellationToken cancellationToken)
        {
            progressCallback(0, "파일 크기 계산 중...", "");
            long sourceLength = await GetGzUncompressedSizeAsync(imagePath, cancellationToken);

            var compressedStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                Optimal.BufferSize * 2, FileOptions.SequentialScan);
            var gzStream = new GZipInputStream(compressedStream);

            return (gzStream, sourceLength);
        }

        private async Task<long> GetGzUncompressedSizeAsync(string imagePath, CancellationToken cancellationToken)
        {
            var sizeProgressReporter = new ProgressReporter();
            sizeProgressReporter.Initialize(cancellationToken);
            sizeProgressReporter.ProgressChanged += (sender, args) => progressCallback(args.Percent, args.Message1, args.Message2);

            long totalReadBytes = 0;
            byte[] buffer = Optimal.ArrayPool.Rent(Optimal.BufferSize * 2);

            long compressedFileSize = new FileInfo(imagePath).Length;
            progressCallback(0, "이미지 크기 확인 중...", "");

            try
            {
                using var compressedStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, Optimal.BufferSize * 2);
                using var gzStream = new GZipInputStream(compressedStream);

                int readBytes;
                while ((readBytes = await gzStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalReadBytes += readBytes;

                    sizeProgressReporter.ReportProgressWithInterval(
                        compressedStream.Position,
                        compressedFileSize,
                        "이미지 크기 확인 중...",
                        1.0);
                }
            }
            finally
            {
                Optimal.ArrayPool.Return(buffer);
            }

            return totalReadBytes;
        }
    }

    public class ImgWriteHandler : IImageWriteHandler
    {
        public async Task<(Stream stream, long length)> OpenStreamAsync(string imagePath, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                Optimal.BufferSize * 2, FileOptions.SequentialScan);
            return (stream, stream.Length);
        }
    }

    #region Inner Class

    internal class CompositeDisposableStream : Stream
    {
        readonly Stream baseStream;
        readonly IDisposable additionalDisposable;

        public override bool CanRead  => this.baseStream.CanRead;

        public override bool CanSeek => this.baseStream.CanSeek;

        public override bool CanWrite => this.baseStream.CanWrite;

        public override long Length => this.baseStream.Length;

        public override long Position { get => this.baseStream.Position; set => this.baseStream.Position = value; }

        public CompositeDisposableStream(Stream baseStream, IDisposable additionalDisposable)
        {
            this.baseStream = baseStream;
            this.additionalDisposable = additionalDisposable;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.baseStream?.Dispose();
                this.additionalDisposable?.Dispose();
            }
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {            
            return this.baseStream.Read(buffer, offset, count);
        }

        public override void Flush()
        {
            this.baseStream?.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return (long)this.baseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.baseStream?.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.baseStream?.Write(buffer, offset, count);
        }
    }

    #endregion
}