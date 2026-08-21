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
            return await FetchHtmlFastAsync(url);
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