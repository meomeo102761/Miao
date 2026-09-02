using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Miao.Core.Services;

namespace Miao.UI.Converters
{
    public class CoverImageConverter : IValueConverter
    {
        public static string? LastDebugInfo;

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
            {
                LastDebugInfo = $"value null/empty: {value}";
                return null;
            }

            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                    uri.IsFile)
                {
                    path = uri.LocalPath;
                }

                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(
                        AppSettingsService.Instance.Settings.DataFolder,
                        path);
                }

                if (!File.Exists(path))
                {
                    LastDebugInfo = $"File.Exists=false at: {path}";
                    return null;
                }

                using var stream = File.OpenRead(path);
                var bitmap = new Bitmap(stream);
                LastDebugInfo = $"OK: {path}";
                return bitmap;
            }
            catch (Exception ex)
            {
                LastDebugInfo = $"EXCEPTION at path={path}: {ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}