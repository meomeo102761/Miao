using System;
using System.IO;
using System.Text.Json;

namespace Miao.Core.Services
{
    public class AppSettings
    {
        public string DataFolder { get; set; } = string.Empty;

        public string ReaderFontFamily { get; set; } = "Segoe UI";
        public double ReaderFontSize { get; set; } = 15;
        public double ReaderLineHeight { get; set; } = 1.5;
        public string ReaderBackground { get; set; } = "#FFFDF8";

        public string TranslationEngine { get; set; } = "DichNgay";

        public string DichNgayEndpoint { get; set; }
            = "https://dichngay.com/translate/text";
    }

    public class AppSettingsService
    {
        private static AppSettingsService? _instance;

        public static AppSettingsService Instance =>
            _instance ?? throw new InvalidOperationException(
                "AppSettingsService chưa được khởi tạo. " +
                "Gọi AppSettingsService.Initialize(baseFolder) " +
                "ở entry point của Miao.Desktop hoặc Miao.Android trước khi dùng.");

        public static void Initialize(string baseFolder)
        {
            _instance = new AppSettingsService(baseFolder);
        }

        private readonly string _configPath;

        public AppSettings Settings { get; private set; }

        private AppSettingsService(string baseFolder)
        {
            var configFolder = Path.Combine(baseFolder, "Miao");

            Directory.CreateDirectory(configFolder);

            _configPath = Path.Combine(
                configFolder,
                "settings.json");

            if (File.Exists(_configPath))
            {
                Settings =
                    JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(_configPath))
                    ?? new AppSettings();
            }
            else
            {
                Settings = new AppSettings
                {
                    DataFolder = configFolder
                };
            }

            if (string.IsNullOrWhiteSpace(Settings.DataFolder))
                Settings.DataFolder = configFolder;

            if (string.IsNullOrWhiteSpace(Settings.TranslationEngine) ||
                string.Equals(Settings.TranslationEngine, "CT2", StringComparison.OrdinalIgnoreCase))
            {
                Settings.TranslationEngine = "DichNgay";
            }

            if (string.IsNullOrWhiteSpace(Settings.DichNgayEndpoint))
            {
                Settings.DichNgayEndpoint =
                    "https://dichngay.com/translate/text";
            }

            if (string.IsNullOrWhiteSpace(Settings.ReaderBackground))
                Settings.ReaderBackground = "#FFFDF8";
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(_configPath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(
                Settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(
                _configPath,
                json);
        }
    }
}