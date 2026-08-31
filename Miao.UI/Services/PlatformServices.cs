using Miao.Core.Services;

namespace Miao.UI.Services
{
    public static class PlatformServices
    {
        public static IPageFetcher PageFetcher { get; set; } = null!;
        public static IScreenshotFetcher ScreenshotFetcher { get; set; } = null!;
    }
}