using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Miao.Core.Services;

namespace Miao.Desktop.Services
{
    /// <summary>
    /// Dùng WebView2 (Microsoft Edge) để tải trang cần JS render hoặc chụp ảnh màn hình.
    /// CHỈ CHẠY ĐƯỢC TRÊN WINDOWS — WebView2 không tồn tại trên macOS/Linux/Android/iOS,
    /// nên class này đặt ở Miao.Desktop, không đặt trong Miao.UI dùng chung.
    /// </summary>
    public class BrowserFetchService : IPageFetcher, IScreenshotFetcher
    {
        private WebView2? _webView;
        private System.Windows.Window? _hiddenWindow;
        private bool _initialized = false;

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;

            _hiddenWindow = new System.Windows.Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = System.Windows.WindowStyle.None,
                ShowInTaskbar = false,
                Left = -2000,
                Top = -2000
            };

            _webView = new WebView2();
            _hiddenWindow.Content = _webView;
            _hiddenWindow.Show();

            await _webView.EnsureCoreWebView2Async();

            _webView.CoreWebView2.AddWebResourceRequestedFilter(
                "*",
                CoreWebView2WebResourceContext.All);

            _initialized = true;
        }

        public async Task<string> FetchFanqieChapterAsync(string url)
        {
            await EnsureInitializedAsync();

            // QUAN TRỌNG: cửa sổ ẩn mặc định chỉ 1x1 pixel. Trang đọc Fanqie
            // lazy-load đoạn văn/ảnh phía sau bằng IntersectionObserver dựa
            // theo vị trí cuộn trong viewport — với viewport 1x1 thì gần như
            // không có gì được coi là "đang hiển thị" nên nội dung sau đoạn
            // đầu và ảnh không bao giờ được kích hoạt load. Phóng to tạm thời
            // (giống CaptureScreenshotAsync) trước khi điều hướng.
            _hiddenWindow!.Width = 1200;
            _hiddenWindow.Height = 3000;
            _webView!.Width = 1200;
            _webView.Height = 3000;

            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(
                object? s,
                CoreWebView2NavigationCompletedEventArgs e)
            {
                tcs.TrySetResult(e.IsSuccess);
            }

            _webView!.NavigationCompleted += Handler;

            try
            {
                _webView.CoreWebView2.Navigate(url);

                var completedTask = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(TimeSpan.FromSeconds(30)));

                if (completedTask != tcs.Task)
                {
                    throw new TimeoutException(
                        $"Không thể tải trang trong 30 giây: {url}");
                }

                if (!await tcs.Task)
                {
                    throw new Exception(
                        $"WebView2 không tải được trang: {url}");
                }
            }
            finally
            {
                _webView.NavigationCompleted -= Handler;
            }

            // Trang Fanqie render nội dung chương bằng JS SAU KHI điều hướng xong,
            // nên chờ cố định một khoảng ngắn (như FetchHtmlFastAsync mặc định
            // 500ms) không ổn định — mạng/máy chậm một chút là chưa kịp render,
            // gây ra tình trạng "có chương tải được có chương không". Thay vào đó,
            // poll cho tới khi thấy div.muye-reader-content xuất hiện trong DOM
            // (hoặc hết thời gian chờ tối đa) rồi mới chụp HTML.
            const int maxWaitMs = 8000;
            const int pollIntervalMs = 300;
            var waitedMs = 0;

            while (waitedMs < maxWaitMs)
            {
                var hasContentJson = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('div[class*=\"muye-reader-content\"]') ? '1' : '0'");

                var hasContent =
                    System.Text.Json.JsonSerializer.Deserialize<string>(hasContentJson) == "1";

                if (hasContent)
                    break;

                await Task.Delay(pollIntervalMs);
                waitedMs += pollIntervalMs;
            }

            try
            {
                // Cuộn xuống đáy nhiều lần để kích hoạt lazy-load hết đoạn văn/ảnh
                // còn lại, dừng khi chiều cao trang không tăng thêm nữa (đã tải hết).
                // Nếu bước này lỗi (VD script tạm thời fail) thì bỏ qua, vẫn chụp
                // HTML hiện có thay vì làm hỏng luôn cả chương.
                await ScrollToLoadAllContentAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE] Scroll-to-load lỗi (bỏ qua, vẫn chụp HTML hiện có): {ex}");
            }

            var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                "document.documentElement.outerHTML");

            // Trả cửa sổ về kích thước ẩn nhỏ như cũ (luôn chạy dù có lỗi ở trên).
            _hiddenWindow.Width = 1;
            _hiddenWindow.Height = 1;
            _webView.Width = 1;
            _webView.Height = 1;

            return System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
        }

        private async Task ScrollToLoadAllContentAsync()
        {
            const int maxRounds = 15;
            const int settleDelayMs = 500;
            var stableRounds = 0;
            var lastHeight = -1;

            for (var i = 0; i < maxRounds; i++)
            {
                await _webView!.CoreWebView2.ExecuteScriptAsync(
                    "window.scrollTo(0, document.body.scrollHeight)");

                await Task.Delay(settleDelayMs);

                var heightJson = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "document.body.scrollHeight");

                if (!int.TryParse(heightJson, out var height))
                    break;

                if (height == lastHeight)
                {
                    stableRounds++;
                    // Chiều cao không đổi 2 lần liên tiếp -> coi như đã load hết.
                    if (stableRounds >= 2)
                        break;
                }
                else
                {
                    stableRounds = 0;
                }

                lastHeight = height;
            }

            // Đợi thêm một chút để nội dung render nốt, tránh chụp đúng lúc DOM
            // đang cập nhật dở dang (cắt cụt giữa chừng).
            await Task.Delay(400);
        }

        public async Task<string> FetchHtmlAsync(string url)
        {
            await EnsureInitializedAsync();

            var tcs = new TaskCompletionSource<bool>();

            void Handler(
                object? s,
                CoreWebView2NavigationCompletedEventArgs e)
            {
                tcs.TrySetResult(e.IsSuccess);
            }

            _webView!.NavigationCompleted += Handler;
            _webView.CoreWebView2.Navigate(url);

            await tcs.Task;

            _webView.NavigationCompleted -= Handler;

            await Task.Delay(2500);

            var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                "document.documentElement.outerHTML");

            return System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
        }

        public async Task<string> FetchHtmlFastAsync(
            string url,
            int waitMs = 500)
        {
            await EnsureInitializedAsync();

            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(
                object? s,
                CoreWebView2NavigationCompletedEventArgs e)
            {
                tcs.TrySetResult(e.IsSuccess);
            }

            _webView!.NavigationCompleted += Handler;

            try
            {
                _webView.CoreWebView2.Navigate(url);

                var completedTask = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(TimeSpan.FromSeconds(30)));

                if (completedTask != tcs.Task)
                {
                    throw new TimeoutException(
                        $"Không thể tải trang trong 30 giây: {url}");
                }

                if (!await tcs.Task)
                {
                    throw new Exception(
                        $"WebView2 không tải được trang: {url}");
                }
            }
            finally
            {
                _webView.NavigationCompleted -= Handler;
            }

            if (waitMs > 0)
                await Task.Delay(waitMs);

            var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                "document.documentElement.outerHTML");

            return System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
        }

        public async Task<byte[]> CaptureScreenshotAsync(
            string url,
            int extraWaitMs = 2500)
        {
            await EnsureInitializedAsync();

            _hiddenWindow!.Width = 1200;
            _hiddenWindow.Height = 9000;

            _webView!.Width = 1200;
            _webView.Height = 9000;

            var tcs = new TaskCompletionSource<bool>();

            void Handler(
                object? s,
                CoreWebView2NavigationCompletedEventArgs e)
            {
                tcs.TrySetResult(e.IsSuccess);
            }

            _webView.NavigationCompleted += Handler;
            _webView.CoreWebView2.Navigate(url);

            await tcs.Task;

            _webView.NavigationCompleted -= Handler;

            await Task.Delay(extraWaitMs);

            using var stream = new System.IO.MemoryStream();

            await _webView.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png,
                stream);

            return stream.ToArray();
        }
    }
}