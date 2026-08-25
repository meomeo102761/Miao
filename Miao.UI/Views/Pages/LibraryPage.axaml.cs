using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class LibraryPage : UserControl
    {
        private const int ItemsPerRow = 5;
        private const int RowsPerPage = 3;
        private const int PageSize = ItemsPerRow * RowsPerPage;
        private const string CompletedStatus = "Hoàn thành";

        private List<Novel> _allNovels = new();
        private List<Novel> _filteredNovels = new();
        private List<Novel> _latestFull = new();
        private List<Novel> _completedFull = new();
        private int _currentPage = 1;
        private const int PreviewRowsPerPage = 2;
        private const int PreviewPageSize = ItemsPerRow * PreviewRowsPerPage;

        private int _latestPage = 1;
        private int _completedPage = 1;

        private sealed class NovelTagInfo
        {
            public string Name { get; init; } = string.Empty;
            public string Category { get; init; } = string.Empty;
        }

        public LibraryPage()
        {
            InitializeComponent();
            LoadNovels();
        }

        private void LoadNovels()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            _allNovels = db.Novels.ToList();

            var chapterCounts = db.Chapters
                .GroupBy(c => c.NovelId)
                .Select(g => new { NovelId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.NovelId, x => x.Count);

            var tagRows = (
                from nt in db.NovelTags
                join t in db.Tags on nt.TagId equals t.Id
                select new { nt.NovelId, t.Name, t.Category })
                .ToList();

            var tagsByNovel = tagRows
                .GroupBy(x => x.NovelId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new NovelTagInfo { Name = x.Name, Category = x.Category }).ToList());

            foreach (var novel in _allNovels)
            {
                novel.TotalChapterCount = chapterCounts.TryGetValue(novel.Id, out var count) ? count : 0;
                novel.DirectionTag = GetDirectionTag(tagsByNovel.TryGetValue(novel.Id, out var tags) ? tags : new List<NovelTagInfo>());
            }

            RefreshHomeSections();
            ShowFullList();
        }

        private static string GetDirectionTag(IEnumerable<NovelTagInfo> tags)
        {
            var direction = tags.FirstOrDefault(t =>
                t.Category.Contains("hướng", StringComparison.OrdinalIgnoreCase)
                || t.Category.Contains("giới tính", StringComparison.OrdinalIgnoreCase)
                || t.Category.Contains("giới", StringComparison.OrdinalIgnoreCase));

            if (direction != null && !string.IsNullOrWhiteSpace(direction.Name))
                return direction.Name.Trim();

            var knownDirectionTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Ngôn tình", "Đam mỹ", "Bách hợp", "Vô CP", "Không CP",
                "BG", "BL", "GL", "言情", "纯爱", "百合", "无CP"
            };

            return tags
                .Select(t => t.Name.Trim())
                .FirstOrDefault(knownDirectionTags.Contains)
                ?? string.Empty;
        }

        private static List<Novel> OrderByRecentUpdate(IEnumerable<Novel> novels)
            => novels.OrderByDescending(n => n.LastUpdatedAt ?? n.AddedAt).ToList();

        private void RefreshHomeSections()
        {
            _latestFull = OrderByRecentUpdate(_allNovels);
            LatestSection.IsVisible = _latestFull.Count > 0;
            _latestPage = 1;
            RenderLatestPage();

            _completedFull = OrderByRecentUpdate(_allNovels.Where(n => n.Status == CompletedStatus));
            CompletedSection.IsVisible = _completedFull.Count > 0;
            _completedPage = 1;
            RenderCompletedPage();
        }

        private void RenderLatestPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_latestFull.Count / (double)PreviewPageSize));
            if (_latestPage > totalPages) _latestPage = totalPages;
            LatestPreviewList.ItemsSource = _latestFull.Skip((_latestPage - 1) * PreviewPageSize).Take(PreviewPageSize).ToList();
            PaginationHelper.Build(LatestPageButtonsPanel, totalPages, _latestPage, p => { _latestPage = p; RenderLatestPage(); });
        }

        private void RenderCompletedPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_completedFull.Count / (double)PreviewPageSize));
            if (_completedPage > totalPages) _completedPage = totalPages;
            CompletedPreviewList.ItemsSource = _completedFull.Skip((_completedPage - 1) * PreviewPageSize).Take(PreviewPageSize).ToList();
            PaginationHelper.Build(CompletedPageButtonsPanel, totalPages, _completedPage, p => { _completedPage = p; RenderCompletedPage(); });
        }

        private void OnShowAllLatestClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new LibraryListPage("Mới nhất", _latestFull));

        private void OnShowAllCompletedClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new LibraryListPage(CompletedStatus, _completedFull));

        private void OnBackToFullListClick(object? sender, PointerPressedEventArgs e) => ShowFullList();

        private void ShowFilteredList(string title, List<Novel> novels)
        {
            _filteredNovels = novels;
            _currentPage = 1;
            ListSectionTitleText.Text = title;
            BackToFullListLink.IsVisible = true;
            RenderPage();
        }

        private void ShowFullList()
        {
            SearchBox.Text = "";
            _filteredNovels = _allNovels;
            _currentPage = 1;
            ListSectionTitleText.Text = "Tất cả truyện";
            BackToFullListLink.IsVisible = false;
            RenderPage();
        }

        private void OnSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                DoSearch();
        }

        private void DoSearch()
        {
            var rawKeyword = SearchBox.Text ?? "";
            var keyword = RemoveDiacritics(rawKeyword).Trim();

            if (string.IsNullOrEmpty(keyword))
            {
                ShowFullList();
                return;
            }

            var results = _allNovels
                .Where(n => RemoveDiacritics(n.DisplayTitle ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || RemoveDiacritics(n.DisplayAuthor ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            AppNavigator.NavigateTo(new LibraryListPage($"Kết quả tìm kiếm: \"{rawKeyword.Trim()}\"", results));
        }

        private void RenderPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_filteredNovels.Count / (double)PageSize));
            if (_currentPage > totalPages) _currentPage = totalPages;
            NovelsList.ItemsSource = _filteredNovels.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();
            PaginationHelper.Build(PageButtonsPanel, totalPages, _currentPage, p => { _currentPage = p; RenderPage(); });
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

        private void OnNovelClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not Novel novel) return;

            AppNavigator.NavigateTo(new NovelDetailPage(novel.Id));   // bỏ hẳn try/catch tạm thời
        }
    }
}