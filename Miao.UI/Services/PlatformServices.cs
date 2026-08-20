using Miao.Core.Services;

namespace Miao.UI.Services
{
    /// <summary>
    /// Mỗi platform head (Desktop, Android) tự gán PageFetcher phù hợp lúc khởi động app,
    /// trước khi bất kỳ Page nào trong Miao.UI cần dùng tới.
    /// </summary>
    public static class PlatformServices
    {
        public static IPageFetcher PageFetcher { get; set; } = null!;
        public static IScreenshotFetcher ScreenshotFetcher { get; set; } = null!;
    }
}