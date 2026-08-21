using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class CustomLibraryDetailPage : ConfirmablePage
    {
        private const int ItemsPerRow = 5;
        private const int RowsPerPage = 3;
        private const int PageSize = ItemsPerRow * RowsPerPage;

        private readonly Guid _libraryId;
        private bool _isEditMode;

        private List<Novel> _allNovels = new();
        private int _currentPage = 1;

        protected override Control ConfirmCardElement => ConfirmCard;
        protected override TextBlock ConfirmMessageTextElement => ConfirmMessageText;

        public CustomLibraryDetailPage(Guid libraryId, string libraryName)
        {
            InitializeComponent();
            _libraryId = libraryId;
            TitleText.Text = libraryName;

            LoadNovels();
        }

        private void LoadNovels()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            _allNovels = (from cln in db.CustomLibraryNovels
                        join n in db.Novels on cln.NovelId equals n.Id
                        where cln.CustomLibraryId == _libraryId
                        select n).ToList();

            NovelEnrichmentService.ApplyDisplayInfo(db, _allNovels);

            RenderCurrentPage();
        }

        private int TotalPages => Math.Max(1, (int)Math.Ceiling(_allNovels.Count / (double)PageSize));

        private void GoToPage(int page)
        {
            _currentPage = Math.Clamp(page, 1, TotalPages);
            RenderCurrentPage();
        }

        private void RenderCurrentPage()
        {
            _currentPage = Math.Clamp(_currentPage, 1, TotalPages);

            var pageItems = _allNovels
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            NovelsList.ItemsSource = pageItems;
            EmptyText.IsVisible = _allNovels.Count == 0;

            RenderPager();

            if (_isEditMode)
                Dispatcher.UIThread.Post(UpdateDeleteBadges, DispatcherPriority.Loaded);
        }

        private void RenderPager()
        {
            PagerPanel.Children.Clear();

            var totalPages = TotalPages;
            if (totalPages <= 1)
            {
                PagerPanel.IsVisible = false;
                return;
            }

            PagerPanel.IsVisible = true;
            PagerPanel.Children.Add(CreatePagerButton("‹", isCurrent: false, isEnabled: _currentPage > 1, page: _currentPage - 1));

            // Đã chuyển PagerNumbers.Build vào Miao.Core (dùng chung, không tự viết lại BuildPagerNumbers ở đây nữa)
            foreach (var page in PagerNumbers.Build(_currentPage, totalPages))
            {
                if (page == null)
                {
                    PagerPanel.Children.Add(new TextBlock
                    {
                        Text = "…",
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 15,
                        Foreground = (IBrush)this.FindResource("TextMuted")!,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(4, 0, 4, 0)
                    });
                }
                else
                {
                    PagerPanel.Children.Add(CreatePagerButton(page.Value.ToString(), isCurrent: page == _currentPage, isEnabled: true, page: page.Value));
                }
            }

            PagerPanel.Children.Add(CreatePagerButton("›", isCurrent: false, isEnabled: _currentPage < totalPages, page: _currentPage + 1));
        }

        private Button CreatePagerButton(string content, bool isCurrent, bool isEnabled, int page)
        {
            var button = new Button
            {
                Content = content,
                IsEnabled = isEnabled,
                Classes = { "pagerNav" }
            };
            if (isCurrent) button.Classes.Add("pagerCurrent");
            button.Click += (_, _) => GoToPage(page);
            return button;
        }

        private void OnNovelClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control c && c.Tag is Novel novel)
                AppNavigator.NavigateTo(new NovelDetailPage(novel.Id));
        }

        private void OnReadClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.Tag is Novel novel)
                AppNavigator.NavigateTo(new ReaderPage(novel.Id, 1));
        }

        private void OnBackToLibrariesClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new CustomLibrariesPage());

        private void OnEditModeClick(object? sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            EditButton.Content = _isEditMode ? "Xong" : "Sửa";
            UpdateDeleteBadges();
        }

        private void UpdateDeleteBadges()
        {
            foreach (var badge in FindVisualChildren<Border>(NovelsList))
                if (badge.Name == "DeleteBadge")
                    badge.IsVisible = _isEditMode;
        }

        private void OnDeleteBadgeClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control c || c.Tag is not Novel novel) return;

            ShowConfirm($"Xóa \"{novel.DisplayTitle}\" khỏi thư viện này?", () =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var link = db.CustomLibraryNovels
                    .FirstOrDefault(x => x.CustomLibraryId == _libraryId && x.NovelId == novel.Id);
                if (link == null) return;

                db.CustomLibraryNovels.Remove(link);
                db.SaveChanges();
                LoadNovels();
            });
        }
    }
}