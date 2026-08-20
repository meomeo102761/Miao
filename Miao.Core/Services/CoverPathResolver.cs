using System.IO;

namespace Miao.Core.Services
{
    public static class CoverPathResolver
    {
        public static string? Resolve(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppSettingsService.Instance.Settings.DataFolder, path);

            return File.Exists(fullPath) ? fullPath : null;
        }
    }
}