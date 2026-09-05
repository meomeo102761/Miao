using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Miao.Core.Services;
using Miao.Desktop.Services;
using Miao.UI;
using Miao.UI.Services;

class Program
{
    private static string _logFolder = string.Empty;

    [STAThread]
    public static void Main(string[] args)
    {
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logFolder = Path.Combine(baseFolder, "Miao", "Logs");
        Directory.CreateDirectory(_logFolder);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppSettingsService.Initialize(baseFolder);

        var browserFetch = new BrowserFetchService();
        PlatformServices.PageFetcher = browserFetch;
        PlatformServices.ScreenshotFetcher = browserFetch;
        PlatformServices.IsTouchPlatform = false;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var file = Path.Combine(_logFolder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(file, ex.ToString());
        }
        catch
        {
            // Nếu không ghi được log thì đành chịu, không được để việc ghi log
            // lại làm crash thêm lần nữa.
        }
    }
}