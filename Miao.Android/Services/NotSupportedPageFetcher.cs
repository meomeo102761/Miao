using System;
using System.Threading.Tasks;
using Miao.Core.Services;

namespace Miao.Android.Services
{
    /// <summary>
    /// WebView2 chỉ chạy trên Windows. Trên Android, các nguồn crawl cần render JS
    /// tạm thời chưa hỗ trợ — ném lỗi rõ ràng thay vì crash mù mờ.
    /// </summary>
    public class NotSupportedPageFetcher : IPageFetcher, IScreenshotFetcher
    {
        public Task<string> FetchHtmlAsync(string url)
            => throw new NotSupportedException(
                "Tính năng tải trang cần trình duyệt chưa được hỗ trợ trên Android.");

        public Task<string> FetchHtmlFastAsync(string url, int waitMs = 500)
            => throw new NotSupportedException(
                "Tính năng tải trang cần trình duyệt chưa được hỗ trợ trên Android.");

        public Task<string> FetchFanqieChapterAsync(string url)
            => throw new NotSupportedException(
                "Tính năng tải chương Fanqie cần trình duyệt chưa được hỗ trợ trên Android.");

        public Task<byte[]> CaptureScreenshotAsync(
            string url,
            int extraWaitMs = 2500)
            => throw new NotSupportedException(
                "Tính năng chụp ảnh trang chưa được hỗ trợ trên Android.");
    }
}