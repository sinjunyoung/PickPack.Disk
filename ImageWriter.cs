using ICSharpCode.SharpZipLib.GZip;
using Microsoft.Win32.SafeHandles;
using SharpCompress.Archives;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace PickPack.Disk
{
    public class ImageWriter
    {
        #region Field

        readonly ProgressReporter progressReporter;
        CancellationToken cancellationToken;

        #endregion

        #region Property

        public string WorkTitle { get; set; } = "이미지 굽기";

        #endregion

        #region Constructor

        public ImageWriter()
        {
            this.progressReporter = new ProgressReporter();
            this.progressReporter.ProgressChanged += (sender, args) => OnProgressChanged(args.Percent, args.Message1, args.Message2);
        }

        #endregion

        #region Event

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<EventArgs>? WriteEnded;

        protected virtual void OnProgressChanged(int percent, string message1, string? message2 = "")
        {
            ProgressChanged?.Invoke(this, new ProgressEventArgs
            {
                Percent = percent,
                Message1 = message1,
                Message2 = message2
            });
        }

        protected virtual void OnWriteEnded()
        {
            WriteEnded?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        public async Task WriteImageAsync(string imagePath, int physicalDriveNumber, long diskSize, CancellationToken cancellationToken)
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            this.cancellationToken = cancellationToken;

            this.progressReporter.Initialize(cancellationToken);

            await WriteImageAsyncInternal(imagePath, physicalDriveNumber, diskSize, cancellationToken);

            OnWriteEnded();
        }

        private async Task WriteImageAsyncInternal(string imagePath, int physicalDriveNumber, long diskSize, CancellationToken cancellationToken)
        {
            string extension = Path.GetExtension(imagePath).ToLowerInvariant();

            if (!ImageWriterFactory.IsSupported(extension))
                throw new NotSupportedException($"지원되지 않는 파일 형식입니다. 지원 형식: {string.Join(", ", ImageWriterFactory.GetSupportedExtensions())}");

            cancellationToken.ThrowIfCancellationRequested();

            var handler = ImageWriterFactory.GetHandler(extension, OnProgressChanged);

            var (sourceStream, sourceLength) = await handler.OpenStreamAsync(imagePath, cancellationToken);

            if (sourceLength > diskSize)
            {
                sourceStream?.Dispose();
                throw new InvalidOperationException("이미지 파일 크기가 대상 드라이브보다 큽니다.");
            }

            OnProgressChanged(0, "파티션 삭제 중...");
            await PartitionUtil.DeleteAllPartitionsAsync(physicalDriveNumber);
            OnProgressChanged(0, "파티션 삭제 완료.");

            await WriteToPhysicalDiskAsync(sourceStream, sourceLength, physicalDriveNumber, cancellationToken);
        }

        private async Task WriteToPhysicalDiskAsync(Stream sourceStream, long sourceLength, int physicalDriveNumber, CancellationToken cancellationToken)
        {
            using (sourceStream)
            {
                string physicalDrivePath = $@"\\.\PhysicalDrive{physicalDriveNumber}";

                using var stream = new FileStream(physicalDrivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096, useAsync: true);

                stream.Seek(0, SeekOrigin.Begin);

                int sectorSize = DiskUtil.GetSectorSize(stream.SafeFileHandle);                
                long bytesToWrite = Math.Min(sourceLength, stream.Length);
                int sectorsPerBuffer = Math.Max(131072, sectorSize * 128);
                int bufferSize = sectorSize * sectorsPerBuffer;

                var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Optimal.ChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true
                });

                var producerTask = ProduceDataAsync(sourceStream, channel.Writer, bufferSize, sectorSize, cancellationToken);
                var consumerTask = ConsumeDataAsync(channel.Reader, stream, bytesToWrite, cancellationToken);

                await Task.WhenAll(producerTask, consumerTask);

                this.progressReporter.ReportCompletion($"{this.WorkTitle} 완료");
            }
        }

        private async Task ProduceDataAsync(Stream fs, ChannelWriter<byte[]> writer, int bufferSize, int sectorSize, CancellationToken cancellationToken)
        {
            byte[] buffer = Optimal.ArrayPool.Rent(bufferSize);

            try
            {
                int read;
                while ((read = await fs.ReadAsync(buffer, 0, bufferSize, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] dataToSend;
                    if (read < bufferSize)
                    {
                        int padding = (sectorSize - (read % sectorSize)) % sectorSize;
                        int totalSize = read + padding;
                        dataToSend = new byte[totalSize];
                        Buffer.BlockCopy(buffer, 0, dataToSend, 0, read);
                    }
                    else
                    {
                        dataToSend = new byte[read];
                        Buffer.BlockCopy(buffer, 0, dataToSend, 0, read);
                    }

                    await writer.WriteAsync(dataToSend, cancellationToken);
                }
            }
            finally
            {
                Optimal.ArrayPool.Return(buffer);
                writer.Complete();
            }
        }

        private async Task ConsumeDataAsync(ChannelReader<byte[]> reader, FileStream physicalDriveStream, long bytesToWrite, CancellationToken cancellationToken)
        {
            long totalWritten = 0;

            await foreach (var buffer in reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await physicalDriveStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                    totalWritten += buffer.Length;
                    this.progressReporter.ReportProgressWithInterval(totalWritten, bytesToWrite, $"{WorkTitle} 진행중...", 1.0);
                }
                catch (IOException)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new Win32Exception(errorCode == 0 ? -1 : errorCode, $"{WorkTitle} 실패 (오류코드: {errorCode})");
                }
            }
            physicalDriveStream.Flush();
        }
    }
}