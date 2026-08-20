using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class BookmarkRow : INotifyPropertyChanged
    {
        public int NovelId { get; set; }
        public int? ChapterNumber { get; set; }
        public string NovelTitle { get; set; } = "";
        public string AuthorLabel { get; set; } = "";
        public string ChapterLabel { get; set; } = "";
        public string CoverPath { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class BookmarksPage : ConfirmablePage
    {
        private const int PageSize = 50;

        private bool _showingReading = true;
        private List<BookmarkRow> _allRows = new();
        private int _currentPage = 1;

        // BookmarksPage không dùng modal xác nhận, nhưng vẫn phải override
        // vì kế thừa ConfirmablePage. Trả về null! an toàn vì không bao giờ được gọi tới.
        protected override Control ConfirmCardElement => null!;
        protected override TextBlock ConfirmMessageTextElement => null!;

        public BookmarksPage()
        {
            InitializeComponent();
            LoadReading();
        }

        // ================= TAB ĐANG ĐỌC / YÊU THÍCH =================

        private void OnReadingTabClick(object? sender, RoutedEventArgs e) => LoadReading();

        private void OnFavoriteTabClick(object? sender, RoutedEventArgs e) => LoadFavorites();

        private void LoadReading()
        {
            _showingReading = true;
            UpdateTabStyles();

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var rows = (from c in db.Chapters
                        join n in db.Novels on c.NovelId equals n.Id
                        where c.IsPinned
                        select new BookmarkRow
                        {
                            NovelId = n.Id,
                            ChapterNumber = c.Number,
                            NovelTitle = n.DisplayTitle,
                            AuthorLabel = $"Tác giả: {n.Author}",
                            ChapterLabel = $"Chương {c.Number}: {c.DisplayTitle}",
                            CoverPath = n.CoverImagePath
                        }).ToList();

            SetRows(rows, "Chưa ghim chương nào — bấm 📌 khi đọc để đánh dấu.");
        }

        private void LoadFavorites()
        {
            _showingReading = false;
            UpdateTabStyles();

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var rows = db.Novels.Where(n => n.IsFavorite).Select(n => new BookmarkRow
            {
                NovelId = n.Id,
                ChapterNumber = n.LastReadChapterNumber > 0 ? n.LastReadChapterNumber : (int?)null,
                NovelTitle = n.DisplayTitle,
                AuthorLabel = $"Tác giả: {n.Author}",
                ChapterLabel = n.LastReadChapterNumber > 0 ? $"Đang đọc chương {n.LastReadChapterNumber}" : "Chưa đọc",
                CoverPath = n.CoverImagePath
            }).ToList();

            SetRows(rows, "Chưa có truyện yêu thích — bấm ⭐ khi đọc để đánh dấu.");
        }

        private void UpdateTabStyles()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var readingCount = db.Chapters.Count(c => c.IsPinned);
            var favCount = db.Novels.Count(n => n.IsFavorite);

            ReadingTabButton.Content = $"Đang đọc ({readingCount})";
            FavoriteTabButton.Content = $"Yêu thích ({favCount})";

            ReadingTabButton.Background = _showingReading ? (IBrush)this.FindResource("AccentJadeHover")! : Brushes.White;
            FavoriteTabButton.Background = !_showingReading ? (IBrush)this.FindResource("AccentJadeHover")! : Brushes.White;
        }

        // ================= DANH SÁCH + PHÂN TRANG (50 dòng/trang) =================

        private void SetRows(List<BookmarkRow> rows, string emptyMessage)
        {
            _allRows = rows;
            _currentPage = 1;
            EmptyText.Text = rows.Count == 0 ? emptyMessage : "";
            RenderPage();
        }

        private void RenderPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_allRows.Count / (double)PageSize));
            if (_currentPage > totalPages) _currentPage = totalPages;

            ItemsList.ItemsSource = _allRows
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            SelectAllCheckBox.IsChecked = false;
            UpdateSelectedCount();
            BuildPageButtons(totalPages);
        }

        private void BuildPageButtons(int totalPages)
        {
            PageButtonsPanel.Children.Clear();
            PageButtonsPanel.IsVisible = totalPages > 1;

            var prevBtn = new Button
            {
                Content = "‹ Trước",
                Classes = { "pageButton" },
                IsEnabled = _currentPage > 1
            };
            prevBtn.Click += (s, e) => { _currentPage--; RenderPage(); };
            PageButtonsPanel.Children.Add(prevBtn);

            foreach (var p in GetPageNumbersToShow(totalPages))
            {
                if (p == -1)
                {
                    PageButtonsPanel.Children.Add(new TextBlock
                    {
                        Text = "...",
                        Foreground = Brushes.Gray,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(4, 0, 4, 0)
                    });
                    continue;
                }

                var btn = new Button
                {
                    Content = p.ToString(),
                    Classes = { "pageButton" }
                };
                if (p == _currentPage) btn.Classes.Add("active");
                int page = p;
                btn.Click += (s, e) => { _currentPage = page; RenderPage(); };
                PageButtonsPanel.Children.Add(btn);
            }

            var nextBtn = new Button
            {
                Content = "Sau ›",
                Classes = { "pageButton" },
                IsEnabled = _currentPage < totalPages
            };
            nextBtn.Click += (s, e) => { _currentPage++; RenderPage(); };
            PageButtonsPanel.Children.Add(nextBtn);
        }

        private IEnumerable<int> GetPageNumbersToShow(int totalPages)
        {
            const int windowSize = 2;
            var pages = new List<int> { 1 };

            int start = Math.Max(2, _currentPage - windowSize);
            int end = Math.Min(totalPages - 1, _currentPage + windowSize);

            if (start > 2) pages.Add(-1);
            for (int i = start; i <= end; i++) pages.Add(i);
            if (end < totalPages - 1) pages.Add(-1);

            if (totalPages > 1) pages.Add(totalPages);

            return pages.Distinct();
        }

        // ================= CHỌN / XOÁ =================

        private void OnSelectAllChanged(object? sender, RoutedEventArgs e)
        {
            var isChecked = SelectAllCheckBox.IsChecked == true;
            foreach (var item in _allRows)
                item.IsSelected = isChecked;

            UpdateSelectedCount();
        }

        private void UpdateSelectedCount()
        {
            var count = _allRows.Count(i => i.IsSelected);
            SelectedCountText.Text = count > 0 ? $"{count} mục được chọn" : "";
        }

        private void OnDeleteSelectedClick(object? sender, RoutedEventArgs e)
        {
            var selected = _allRows.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            if (_showingReading)
            {
                foreach (var item in selected)
                {
                    var chapter = db.Chapters.FirstOrDefault(c => c.NovelId == item.NovelId && c.Number == item.ChapterNumber);
                    if (chapter != null)
                        chapter.IsPinned = false;
                }
            }
            else
            {
                foreach (var item in selected)
                {
                    var novel = db.Novels.Find(item.NovelId);
                    if (novel != null)
                        novel.IsFavorite = false;
                }
            }

            db.SaveChanges();

            if (_showingReading)
                LoadReading();
            else
                LoadFavorites();
        }

        private void OnItemClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control c && c.Tag is BookmarkRow row)
                AppNavigator.NavigateTo(new ReaderPage(row.NovelId, row.ChapterNumber ?? 1));
        }
    }
}