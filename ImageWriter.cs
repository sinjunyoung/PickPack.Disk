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
        public event EventHandler<ImageWriterEventArgs>? WriteEnding;
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

        protected virtual void OnWriteEnding(SafeFileHandle physicalDriveHandle)
        {
            WriteEnding?.Invoke(this, new ImageWriterEventArgs
            {
                Handle = physicalDriveHandle
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

            await PartitionUtil.RescanDisksAsync();

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

                using var physicalDriveHandle = Win32API.CreateFile(physicalDrivePath, FileAccess.ReadWrite, FileShare.None, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

                if (physicalDriveHandle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "물리 디스크 핸들 열기 실패");

                if (!Win32API.SetFilePointerEx(physicalDriveHandle, 0, out _, Win32API.FILE_BEGIN))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "디스크 오프셋 초기화 실패");

                int sectorSize = DiskUtil.GetSectorSize(physicalDriveHandle);
                long diskLength = DiskUtil.GetDiskLength(physicalDriveHandle);
                long bytesToWrite = Math.Min(sourceLength, diskLength);
                int sectorsPerBuffer = Math.Max(131072, sectorSize * 128);
                int bufferSize = sectorSize * sectorsPerBuffer;

                var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Optimal.ChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true
                });

                var producerTask = ProduceDataAsync(sourceStream, channel.Writer, bufferSize, sectorSize, cancellationToken);
                var consumerTask = ConsumeDataAsync(channel.Reader, physicalDriveHandle, bufferSize, bytesToWrite, cancellationToken);

                await Task.WhenAll(producerTask, consumerTask);

                this.progressReporter.ReportCompletion($"{this.WorkTitle} 완료");
                OnWriteEnding(physicalDriveHandle);

                Win32API.FlushFileBuffers(physicalDriveHandle);
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
                        // padding 부분은 이미 0으로 초기화됨 (new byte[]의 기본값)
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

        private async Task ConsumeDataAsync(ChannelReader<byte[]> reader, SafeFileHandle physicalDriveHandle, int bufferSize, long bytesToWrite, CancellationToken cancellationToken)
        {
            IntPtr alignedBuffer = Marshal.AllocHGlobal(bufferSize);

            long totalWritten = 0;

            try
            {
                await foreach (var buffer in reader.ReadAllAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Marshal.Copy(buffer, 0, alignedBuffer, buffer.Length);

                    if (!Win32API.WriteFile(physicalDriveHandle, alignedBuffer, (uint)buffer.Length, out uint written, IntPtr.Zero))
                    {
                        int err = Marshal.GetLastWin32Error();

                        throw new Win32Exception(err == 0 ? -1 : err, $"{WorkTitle} 실패 (오류코드: {err})");
                    }

                    totalWritten += written;

                    this.progressReporter.ReportProgressWithInterval(totalWritten, bytesToWrite, $"{WorkTitle} 진행중...", 1.0);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(alignedBuffer);
            }
        }
    }
}