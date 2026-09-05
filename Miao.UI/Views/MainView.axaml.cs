using System;
using Avalonia;  
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;  
using Miao.Core.Services;
using Miao.UI.Services;
using Miao.UI.Views.Pages;
using Microsoft.EntityFrameworkCore;

namespace Miao.UI.Views
{
    public partial class MainView : UserControl
    {
        private const double NavCollapseThreshold = 1060;

        public static ScrollViewer? Current;

        private bool _readerModeActive;

        public MainView()
        {
            InitializeComponent();
            Current = MainScrollViewer;

            SizeChanged += OnSizeChanged;
            MainScrollViewer.PropertyChanged += OnMainScrollViewerPropertyChanged;

            ReaderHost.SetOuterScrollEnabled = SetOuterScrollEnabled;

            LegacyDatabaseMigrator.MigrateIfNeeded(AppPaths.DbFilePath);

            using (var db = new Miao.Core.Data.MiaoDbContext(AppPaths.DbFilePath))
            {
                db.Database.Migrate();
                Miao.Core.Services.GlossarySetService.BackfillMissingDefaults(db);
            }

            AppNavigator.MainContent = ContentHost;
            ModalService.Register(ModalOverlay, ModalContent);
            AppNavigator.NavigateTo(new LibraryPage());
        }

        private void SetOuterScrollEnabled(bool enabled)
        {
            _readerModeActive = !enabled;

            if (_readerModeActive)
            {
                MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ContentHost.Height = MainScrollViewer.Bounds.Height;
            }
            else
            {
                MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ContentHost.Height = double.NaN;
            }
        }

        private void OnMainScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_readerModeActive && e.Property == BoundsProperty)
                ContentHost.Height = MainScrollViewer.Bounds.Height;
        }

        private void OnModalOverlayClick(object? sender, PointerPressedEventArgs e) => ModalService.Close();

        private void OnModalContentClick(object? sender, PointerPressedEventArgs e) => e.Handled = true;

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdateNavLayout(e.NewSize.Width);
        }

        private void UpdateNavLayout(double width)
        {
            bool isNarrow = width < NavCollapseThreshold;

            NavButtonsPanel.IsVisible = !isNarrow;
            MenuToggleButton.IsVisible = isNarrow;

            if (!isNarrow)
                MobileNavPopup.IsOpen = false;
        }

        private void OnMenuToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            MobileNavPopup.IsOpen = !MobileNavPopup.IsOpen;
        }

        private DispatcherTimer? _downloadCloseTimer;

        private void OnDownloadMenuEnter(object? sender, PointerEventArgs e)
        {
            // Trên cảm ứng không có "hover" thật, và ở phần lớn kích thước điện thoại menu này đã tự ẩn
            // (NavButtonsPanel chỉ hiện khi đủ rộng, xem UpdateNavLayout) — chặn thêm ở đây để an toàn
            // với tablet Android màn hình rộng vẫn hiện NavButtonsPanel.
            if (PlatformServices.IsTouchPlatform) return;

            _downloadCloseTimer?.Stop();
            _downloadCloseTimer = null;

            if (!DownloadPopup.IsOpen)
                DownloadPopup.IsOpen = true;
        }

        private void OnDownloadMenuLeave(object? sender, PointerEventArgs e)
        {
            if (PlatformServices.IsTouchPlatform) return;

            _downloadCloseTimer?.Stop();

            _downloadCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _downloadCloseTimer.Tick += (s, args) =>
            {
                _downloadCloseTimer!.Stop();
                _downloadCloseTimer = null;

                bool overButton = DownloadMenuArea.IsPointerOver;
                bool overPopup = DownloadPopup.Child is Control c && c.IsPointerOver;

                if (!overButton && !overPopup)
                    DownloadPopup.IsOpen = false;
            };
            _downloadCloseTimer.Start();
        }        private void OnMobileGoSearch(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoSearch(s, e); }
        private void OnMobileGoAuthorList(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoAuthorList(s, e); }
        private void OnDownloadMenuToggleClick(object? sender, RoutedEventArgs e) { DownloadPopup.IsOpen = !DownloadPopup.IsOpen; }
        private void OnMobileGoDownloadLink(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoDownloadLink(s, e); }
        private void OnMobileGoDownloadFile(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoDownloadFile(s, e); }
        private void OnMobileGoCustomLibraries(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoCustomLibraries(s, e); }
        private void OnMobileGoWriteNovel(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoWriteNovel(s, e); }
        private void OnMobileGoBookmarks(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoBookmarks(s, e); }
        private void OnMobileGoCharacters(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoCharacters(s, e); }
        private void OnMobileGoGlossary(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoGlossary(s, e); }
        private void OnMobileGoSettings(object? s, Avalonia.Interactivity.RoutedEventArgs e) { MobileNavPopup.IsOpen = false; GoSettings(s, e); }

        private void OnLogoClick(object? sender, PointerPressedEventArgs e) => AppNavigator.NavigateTo(new LibraryPage());

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!DownloadPopup.IsOpen) return;

            var overButton = DownloadMenuArea.IsPointerOver;
            var overPopup = DownloadPopup.Child is Control c && c.IsPointerOver;

            if (!overButton && !overPopup)
                DownloadPopup.IsOpen = false;
        }

        private void GoCustomLibraries(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new CustomLibrariesPage());
        private void GoSearch(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new SearchPage());
        private void GoAuthorList(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new AuthorListPage());
        private void GoDownloadLink(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new DownloadPage());
        private void GoDownloadFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new DownloadFilePage());
        private void GoBookmarks(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new BookmarksPage());
        private void GoGlossary(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new GlossaryPage());
        private void GoSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new SettingsPage());
        private void GoWriteNovel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new WriteNovelPage());
        private void GoCharacters(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AppNavigator.NavigateTo(new CharactersPage());
    }
}