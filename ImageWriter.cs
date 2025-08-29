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

        private async Task WriteImageAsyncInternal(string imagePathOrUrl, int physicalDriveNumber, long diskSize, CancellationToken cancellationToken)
        {
            if (!ImageWriterFactory.IsSupported(imagePathOrUrl))
            {
                string supportedTypes = string.Join(", ", ImageWriterFactory.GetSupportedExtensions());
                throw new NotSupportedException($"지원되지 않는 파일 형식입니다. 지원 형식: {supportedTypes}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var handler = ImageWriterFactory.GetHandler(imagePathOrUrl, OnProgressChanged);
            if (handler == null)
                throw new InvalidOperationException("적절한 핸들러를 찾을 수 없습니다.");

            var (sourceStream, sourceLength) = await handler.OpenStreamAsync(imagePathOrUrl, cancellationToken);

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

                using var stream = new FileStream(physicalDrivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1024 * 1024, FileOptions.WriteThrough);

                stream.Seek(0, SeekOrigin.Begin);

                int sectorSize = DiskUtil.GetSectorSize(stream.SafeFileHandle);
                long bytesToWrite = Math.Min(sourceLength, stream.Length);

                // GPT/MBR 헤더를 마지막에 쓰기 위해 지연된 헤더 쓰기 방식 사용
                await WriteImageWithDelayedHeaders(sourceStream, stream, bytesToWrite, sectorSize, cancellationToken);

                this.progressReporter.ReportCompletion($"{this.WorkTitle} 완료");
            }
        }

        private async Task WriteImageWithDelayedHeaders(Stream sourceStream, FileStream targetStream, long bytesToWrite, int sectorSize, CancellationToken cancellationToken)
        {
            const int headerSize = 1048576;
            byte[] headerBuffer = new byte[headerSize];

            OnProgressChanged(0, "헤더 정보 읽는 중...");

            int headerBytesRead = await sourceStream.ReadAsync(headerBuffer, 0, headerSize, cancellationToken);

            OnProgressChanged(5, "이미지 데이터 쓰는 중...");

            if (bytesToWrite > headerSize)
            {
                targetStream.Seek(headerSize, SeekOrigin.Begin);
                long remainingBytes = bytesToWrite - headerSize;

                await WriteDataWithProgress(sourceStream, targetStream, remainingBytes, headerSize, bytesToWrite, sectorSize, cancellationToken);
            }

            OnProgressChanged(95, "헤더 정보 쓰는 중...");

            targetStream.Seek(0, SeekOrigin.Begin);
            await targetStream.WriteAsync(headerBuffer, 0, headerBytesRead, cancellationToken);
            targetStream.Flush();

            OnProgressChanged(100, "쓰기 완료");
        }

        private async Task WriteDataWithProgress(Stream sourceStream, FileStream targetStream, long remainingBytes, long headerSize, long totalBytes, int sectorSize, CancellationToken cancellationToken)
        {
            int sectorsPerBuffer = Math.Max(131072, sectorSize * 128);
            int bufferSize = sectorSize * sectorsPerBuffer;

            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Optimal.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

            var producerTask = ProduceDataAsync(sourceStream, channel.Writer, bufferSize, sectorSize, remainingBytes, cancellationToken);
            var consumerTask = ConsumeDataAsync(channel.Reader, targetStream, remainingBytes, headerSize, totalBytes, cancellationToken);

            await producerTask;
            await consumerTask;
        }

        private async Task ProduceDataAsync(Stream fs, ChannelWriter<byte[]> writer, int bufferSize, int sectorSize, long remainingBytes, CancellationToken cancellationToken)
        {
            byte[] buffer = Optimal.ArrayPool.Rent(bufferSize);
            long totalRead = 0;

            try
            {
                int read;
                while (totalRead < remainingBytes && (read = await fs.ReadAsync(buffer, 0, (int)Math.Min(bufferSize, remainingBytes - totalRead), cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalRead += read;

                    if (read == bufferSize)
                    {
                        await writer.WriteAsync(buffer, cancellationToken);
                        buffer = Optimal.ArrayPool.Rent(bufferSize);
                    }
                    else
                    {
                        int padding = (sectorSize - (read % sectorSize)) % sectorSize;
                        int totalSize = read + padding;
                        byte[] dataToSend = Optimal.ArrayPool.Rent(totalSize);
                        Buffer.BlockCopy(buffer, 0, dataToSend, 0, read);
                        await writer.WriteAsync(dataToSend, cancellationToken);
                    }
                }
            }
            finally
            {
                Optimal.ArrayPool.Return(buffer);
                writer.Complete();
            }
        }

        private async Task ConsumeDataAsync(ChannelReader<byte[]> reader, FileStream physicalDriveStream, long remainingBytes, long headerSize, long totalBytes, CancellationToken cancellationToken)
        {
            long totalWritten = 0;

            await foreach (var buffer in reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    long bytesToWriteFromBuffer = Math.Min(buffer.Length, remainingBytes - totalWritten);
                    await physicalDriveStream.WriteAsync(buffer, 0, (int)bytesToWriteFromBuffer, cancellationToken);
                    totalWritten += bytesToWriteFromBuffer;

                    long overallWritten = headerSize + totalWritten;
                    int progressPercent = Math.Min(95, (int)(5 + (overallWritten * 90) / totalBytes));
                    this.progressReporter.ReportProgressWithInterval(overallWritten, totalBytes, $"{WorkTitle} 진행중...", 1.0);
                }
                catch (IOException)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new Win32Exception(errorCode == 0 ? -1 : errorCode, $"{WorkTitle} 실패 (오류코드: {errorCode})");
                }
                finally
                {
                    Optimal.ArrayPool.Return(buffer);
                }

                if (totalWritten >= remainingBytes)
                    break;
            }
            physicalDriveStream.Flush();
        }
    }
}