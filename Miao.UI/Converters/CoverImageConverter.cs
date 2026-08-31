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
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null;

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
                    return null;

                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            }
            catch
            {
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