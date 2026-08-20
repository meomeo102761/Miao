using System;
using System.Threading.Tasks;
using Miao.Core.Services;

namespace Miao.Android.Services
{
    /// <summary>
    /// WebView2 chỉ chạy trên Windows. Trên Android, các nguồn crawl cần render JS
    /// (Sixty9Shuba, Biquge, Jinjiang, Wikidich, và mục lục chương của Fanqie) tạm thời
    /// chưa hỗ trợ — ném lỗi rõ ràng thay vì crash mù mờ.
    /// Nếu cần hỗ trợ đầy đủ trên Android sau này, thay class này bằng bản dùng
    /// Android.Webkit.WebView (native), giữ nguyên interface IPageFetcher/IScreenshotFetcher.
    /// </summary>
    public class NotSupportedPageFetcher : IPageFetcher, IScreenshotFetcher
    {
        public Task<string> FetchHtmlAsync(string url)
            => throw new NotSupportedException("Tính năng tải trang cần trình duyệt chưa được hỗ trợ trên Android.");

        public Task<string> FetchHtmlFastAsync(string url, int waitMs = 500)
            => throw new NotSupportedException("Tính năng tải trang cần trình duyệt chưa được hỗ trợ trên Android.");

        public Task<byte[]> CaptureScreenshotAsync(string url, int extraWaitMs = 2500)
            => throw new NotSupportedException("Tính năng chụp ảnh trang chưa được hỗ trợ trên Android.");
    }
}