using System;
using Avalonia;
using Miao.Core.Services;
using Miao.Desktop.Services;
using Miao.UI;
using Miao.UI.Services;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Windows: dùng %AppData%\Roaming
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AppSettingsService.Initialize(baseFolder);

        // Gán trước khi Avalonia khởi động UI — mọi Page trong Miao.UI dùng chung
        // sẽ gọi qua PlatformServices.PageFetcher thay vì tự new BrowserFetchService().
        var browserFetch = new BrowserFetchService();
        PlatformServices.PageFetcher = browserFetch;
        PlatformServices.ScreenshotFetcher = browserFetch;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}