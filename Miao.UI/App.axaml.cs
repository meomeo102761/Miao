using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Miao.UI.Views;

namespace Miao.UI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            switch (ApplicationLifetime)
            {
                // Windows/Desktop
                case IClassicDesktopStyleApplicationLifetime desktop:
                    desktop.MainWindow = new MainWindow
                    {
                        Content = new MainView()
                    };
                    break;

                // Android/iOS/Browser
                case ISingleViewApplicationLifetime singleView:
                    singleView.MainView = new MainView();
                    break;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}