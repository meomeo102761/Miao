using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class WrittenNovelRowViewModel
    {
        public Guid Id { get; set; }
        public string DisplayTitle { get; set; } = "";
        public string CoverImagePath { get; set; } = "";
        public IImage? CoverImageSource { get; set; }
        public string ChapterCountLabel { get; set; } = "";
        public string UpdatedLabel { get; set; } = "";
    }

    public partial class WriteNovelPage : UserControl
    {
        private const int PageSize = 20;

        private List<WrittenNovel> _allNovels = new();
        private List<WrittenNovel> _filteredNovels = new();
        private Dictionary<Guid, int> _chapterCounts = new();
        private int _currentPage = 1;

        private Point _dragStartPoint;
        private PointerPressedEventArgs? _dragPressedEvent;
        private WrittenNovelRowViewModel? _draggedRow;

        public WriteNovelPage()
        {
            InitializeComponent();
            LoadNovels();
        }

        private static MiaoDbContext OpenDb() => new(AppPaths.DbFilePath);

        private void LoadNovels()
        {
            using var db = OpenDb();

            _allNovels = db.WrittenNovels.OrderBy(n => n.SortOrder).ToList();

            _chapterCounts = db.WrittenChapters
                .GroupBy(c => c.NovelId)
                .ToDictionary(g => g.Key, g => g.Count());

            _filteredNovels = _allNovels;
            _currentPage = 1;
            RenderPage();
        }

        private WrittenNovelRowViewModel ToRow(WrittenNovel n) => new()
        {
            Id = n.Id,
            DisplayTitle = n.DisplayTitle,
            CoverImagePath = n.CoverImagePath,
            CoverImageSource = CoverImageResolver.Load(this, n.CoverImagePath),
            ChapterCountLabel = _chapterCounts.TryGetValue(n.Id, out var count)
                ? $"{count} chương"
                : "Chưa có chương nào",
            UpdatedLabel = $"Đã cập nhật {n.UpdatedAt:dd/MM/yyyy HH:mm}"
        };

        private void RenderPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_filteredNovels.Count / (double)PageSize));
            if (_currentPage > totalPages) _currentPage = totalPages;

            var pageItems = _filteredNovels
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .Select(ToRow)
                .ToList();

            NovelsList.ItemsSource = pageItems;
            EmptyStateText.IsVisible = _allNovels.Count == 0;

            PaginationHelper.Build(PageButtonsPanel, totalPages, _currentPage, p => { _currentPage = p; RenderPage(); });
        }

        private void OnCreateNovelClick(object? sender, RoutedEventArgs e)
        {
            using var db = OpenDb();

            var nextOrder = db.WrittenNovels.Any() ? db.WrittenNovels.Max(n => n.SortOrder) + 1 : 0;
            var novel = new WrittenNovel { SortOrder = nextOrder };

            db.WrittenNovels.Add(novel);
            db.SaveChanges();

            AppNavigator.NavigateTo(new WriteNovelDetailPage(novel.Id));
        }

        private void OnNovelTitleClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.DataContext is not WrittenNovelRowViewModel row) return;
            AppNavigator.NavigateTo(new WriteNovelDetailPage(row.Id));
        }

        private async void OnDeleteNovelClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.DataContext is not WrittenNovelRowViewModel row) return;

            var choice = await DialogService.ShowYesNoAsync(
                $"Xóa truyện \"{row.DisplayTitle}\"? Toàn bộ chương bên trong sẽ bị xóa vĩnh viễn.",
                "Xóa truyện");

            if (choice != DialogResult.Yes) return;

            using var db = OpenDb();
            db.WrittenChapters.RemoveRange(db.WrittenChapters.Where(c => c.NovelId == row.Id));
            var novel = db.WrittenNovels.Find(row.Id);
            if (novel != null) db.WrittenNovels.Remove(novel);
            db.SaveChanges();

            LoadNovels();
        }

        // ----- Kéo thả sắp xếp thứ tự truyện -----

        private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.DataContext is not WrittenNovelRowViewModel row) return;

            _dragStartPoint = e.GetPosition(this);
            _dragPressedEvent = e;
            _draggedRow = row;
        }

        private async void OnDragHandleMoved(object? sender, PointerEventArgs e)
        {
            if (_dragPressedEvent == null || _draggedRow == null ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            var pos = e.GetPosition(this);
            var diff = _dragStartPoint - pos;
            if (Math.Abs(diff.X) <= 5 && Math.Abs(diff.Y) <= 5) return;

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(_draggedRow.Id.ToString()));

            var pressedEvent = _dragPressedEvent;
            _dragPressedEvent = null;

            await DragDrop.DoDragDropAsync(pressedEvent, data, DragDropEffects.Move);
        }

        private void OnNovelDrop(object? sender, DragEventArgs e)
        {
            try
            {
                if (_draggedRow == null) return;
                if (sender is not Control fe || fe.DataContext is not WrittenNovelRowViewModel target) return;
                if (target.Id == _draggedRow.Id) return;

                var currentPageItems = (NovelsList.ItemsSource as List<WrittenNovelRowViewModel>)?.ToList();
                if (currentPageItems == null) return;

                var fromIndex = currentPageItems.FindIndex(x => x.Id == _draggedRow.Id);
                var toIndex = currentPageItems.FindIndex(x => x.Id == target.Id);
                if (fromIndex < 0 || toIndex < 0) return;

                var moved = currentPageItems[fromIndex];
                currentPageItems.RemoveAt(fromIndex);
                currentPageItems.Insert(toIndex, moved);

                // Ghi lại SortOrder cho các truyện đang hiển thị trên trang hiện tại.
                using var db = OpenDb();
                var baseOrder = (_currentPage - 1) * PageSize;
                for (int i = 0; i < currentPageItems.Count; i++)
                {
                    var entity = db.WrittenNovels.Find(currentPageItems[i].Id);
                    if (entity != null) entity.SortOrder = baseOrder + i;
                }
                db.SaveChanges();

                LoadNovels();
            }
            finally
            {
                _draggedRow = null;
                _dragPressedEvent = null;
            }
        }

        // ----- Tìm kiếm -----

        private void OnSearchClick(object? sender, RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void DoSearch()
        {
            var rawKeyword = SearchBox.Text ?? "";
            var keyword = RemoveDiacritics(rawKeyword).Trim();

            _filteredNovels = string.IsNullOrEmpty(keyword)
                ? _allNovels
                : _allNovels
                    .Where(n => RemoveDiacritics(n.DisplayTitle).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(n => n.UpdatedAt)
                    .ToList();

            _currentPage = 1;
            RenderPage();
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormC);
        }
    }
}