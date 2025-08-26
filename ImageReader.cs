using Ionic.Zip;
using Ionic.Zlib;
using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using PickPack.Disk;

namespace PickPack.Disk
{
    public class ImageReader
    {
        #region Field

        readonly ProgressReporter progressReporter;
        CancellationToken cancellationToken;

        #endregion

        #region Constructor

        public ImageReader()
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

        public async Task ReadImageAsync(int physicalDriveNumber, string outputPath, long maxOutputSegmentSize64,
            CompressionLevel compressionLevel, CancellationToken cancellationToken)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                this.progressReporter.Initialize(cancellationToken);
                string extension = Path.GetExtension(outputPath).ToLowerInvariant();

                if (!ImageReaderFactory.IsSupported(extension))
                {
                    throw new InvalidOperationException($"지원하지 않는 파일 형식입니다. 지원 형식: {string.Join(", ", ImageReaderFactory.GetSupportedExtensions())}");
                }

                string physicalDrivePath = $@"\\.\PHYSICALDRIVE{physicalDriveNumber}";
                long driveSize = DiskUtil.GetDiskLength(physicalDriveNumber);

                using var driveStream = new FileStream(physicalDrivePath, FileMode.Open, FileAccess.Read, FileShare.None,
                    Optimal.BufferSize, FileOptions.Asynchronous);

                var handler = ImageReaderFactory.GetHandler(extension, maxOutputSegmentSize64, compressionLevel, OnProgressChanged, this.progressReporter.ReportProgress, cancellationToken);

                if (handler == null)
                    throw new InvalidOperationException($"지원하지 않는 파일 형식입니다: {extension}");

                await handler.WriteImageAsync(driveStream, outputPath, driveSize, cancellationToken);

                this.progressReporter.ReportCompletion("이미지 저장 완료");

                OnWriteEnded();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"이미지 저장 중 오류 발생: {ex.Message}", ex);
            }
        }
    }
}