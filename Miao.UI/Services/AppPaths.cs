using System.IO;

namespace Miao.Core.Services
{
    public static class AppPaths
    {
        public static string DbFilePath
        {
            get
            {
                var folder = AppSettingsService.Instance.Settings.DataFolder;
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "miao.db");
            }
        }
    }
}