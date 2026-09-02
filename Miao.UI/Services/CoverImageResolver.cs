using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Miao.UI.Services
{
    public static class CoverImageResolver
    {
        public static IImage? Load(Control context, string path)
        {
            if (context.FindResource("CoverImageConverter") is not IValueConverter converter)
                return null;

            return converter.Convert(path, typeof(IImage), null, CultureInfo.CurrentCulture) as IImage;
        }
    }
}