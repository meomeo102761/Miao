using System.IO;
using Miao.Core.Services;

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

        public static string CharacterImagesRoot
        {
            get
            {
                var folder = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "images", "characters");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }
    }
}