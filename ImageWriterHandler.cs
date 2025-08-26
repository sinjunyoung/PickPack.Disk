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
            var entry = zipFile.Entries.FirstOrDefault(e => e.FileName.EndsWith(".img", StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                zipFile.Dispose();
                throw new InvalidOperationException("ZIP 파일 안에 IMG 파일이 없습니다.");
            }

            return (entry.OpenReader(), entry.UncompressedSize);
        }
    }

    public class SevenZipWriteHandler : IImageWriteHandler
    {
        public async Task<(Stream stream, long length)> OpenStreamAsync(string imagePath, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var archive = ArchiveFactory.Open(imagePath);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.Key.EndsWith(".img", StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                archive.Dispose();
                throw new InvalidOperationException("7z 파일 안에 IMG 파일이 없습니다.");
            }

            return (entry.OpenEntryStream(), entry.Size);
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

    public static class ImageWriterFactory
    {
        private static readonly Dictionary<string, Func<Action<int, string, string?>, IImageWriteHandler>> HandlerCreators =
            new Dictionary<string, Func<Action<int, string, string?>, IImageWriteHandler>>(StringComparer.OrdinalIgnoreCase)
            {
                { ".zip", _ => new ZipWriteHandler() },
                { ".7z", _ => new SevenZipWriteHandler() },
                { ".gz", progressCallback => new GzipWriteHandler(progressCallback) },
                { ".img", _ => new ImgWriteHandler() }
            };

        public static IImageWriteHandler? GetHandler(string extension, Action<int, string, string?> progressCallback)
        {
            return HandlerCreators.TryGetValue(extension, out var creator) ? creator(progressCallback) : null;
        }

        public static bool IsSupported(string extension)
        {
            return HandlerCreators.ContainsKey(extension);
        }

        public static string[] GetSupportedExtensions()
        {
            return HandlerCreators.Keys.ToArray();
        }
    }
}