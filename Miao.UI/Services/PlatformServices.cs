using Miao.Core.Services;

namespace Miao.UI.Services
{
    public static class PlatformServices
    {
        public static IPageFetcher PageFetcher { get; set; } = null!;
        public static IScreenshotFetcher ScreenshotFetcher { get; set; } = null!;

        // true trên Android (không có chuột phải -> phải dùng giữ lâu để mở toolbar định dạng),
        // false trên Desktop (mặc định). Được gán ở entry point của từng nền tảng.
        public static bool IsTouchPlatform { get; set; }
    }
}