using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Miao.Android.Services;
using Miao.Core.Services;
using Miao.UI;
using Miao.UI.Services;

namespace Miao.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            SQLitePCL.Batteries_V2.Init();
            AppSettingsService.Initialize(FilesDir!.AbsolutePath);

            var fetcher = new NotSupportedPageFetcher();
            PlatformServices.PageFetcher = fetcher;
            PlatformServices.ScreenshotFetcher = fetcher;
            PlatformServices.IsTouchPlatform = true;

            base.OnCreate();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
