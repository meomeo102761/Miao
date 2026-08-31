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
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AppSettingsService.Initialize(baseFolder);

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