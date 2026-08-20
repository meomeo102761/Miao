using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Miao.Android.Services;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.Android;

[Activity(
    Label = "Miao.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AppSettingsService.Initialize(FilesDir!.AbsolutePath);

        var fetcher = new NotSupportedPageFetcher();
        PlatformServices.PageFetcher = fetcher;
        PlatformServices.ScreenshotFetcher = fetcher;

        base.OnCreate(savedInstanceState);
    }
}