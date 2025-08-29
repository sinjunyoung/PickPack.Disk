using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickPack.Disk
{
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

        public static IImageWriteHandler? GetHandler(string pathOrUrl, Action<int, string, string?> progressCallback)
        {
            if (IsUrl(pathOrUrl))
                return new UrlWriteHandler(progressCallback);

            string extension = Path.GetExtension(pathOrUrl);

            return HandlerCreators.TryGetValue(extension, out var creator) ? creator(progressCallback) : null;
        }

        public static bool IsSupported(string pathOrUrl)
        {
            if (IsUrl(pathOrUrl))
                return true;

            string extension = Path.GetExtension(pathOrUrl);
            return HandlerCreators.ContainsKey(extension);
        }

        public static string[] GetSupportedExtensions()
        {
            var extensions = HandlerCreators.Keys.ToList();
            extensions.Add("URL (http/https)");
            return extensions.ToArray();
        }

        private static bool IsUrl(string input)
        {
            return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
