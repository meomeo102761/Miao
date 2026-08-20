using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class AuthorSummary
    {
        public string AuthorKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Count { get; set; }
    }

    public partial class AuthorListPage : UserControl
    {
        private const int PageSize = 50;

        private readonly List<AuthorSummary> _allAuthors;
        private List<AuthorSummary> _filteredAuthors;
        private int _currentPage = 1;

        public AuthorListPage()
        {
            InitializeComponent();

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var novelAuthors = db.Novels
                .Select(n => new { n.Author, n.TranslatedAuthor })
                .ToList();

            _allAuthors = novelAuthors
                .GroupBy(n => n.Author)
                .Select(g => new AuthorSummary
                {
                    AuthorKey = g.Key,
                    DisplayName = g
                        .Select(n => n.TranslatedAuthor)
                        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                        ?? g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(a => a.Count)
                .ToList();

            _filteredAuthors = _allAuthors;
            RenderPage();
        }

        // ================== Tìm kiếm ==================

        private void OnSearchClick(object? sender, RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void DoSearch()
        {
            var keyword = RemoveDiacritics(SearchBox.Text ?? "").Trim();

            _filteredAuthors = string.IsNullOrEmpty(keyword)
                ? _allAuthors
                : _allAuthors.Where(a =>
                    RemoveDiacritics(a.DisplayName).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || RemoveDiacritics(a.AuthorKey).Contains(keyword, StringComparison.OrdinalIgnoreCase))
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
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString().Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormC);
        }

        // ================== Phân trang ==================

        private int TotalPages => Math.Max(1, (int)Math.Ceiling(_filteredAuthors.Count / (double)PageSize));

        private void GoToPage(int page)
        {
            _currentPage = Math.Clamp(page, 1, TotalPages);
            RenderPage();
        }

        private void RenderPage()
        {
            _currentPage = Math.Clamp(_currentPage, 1, TotalPages);

            AuthorsList.ItemsSource = _filteredAuthors
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            RenderPager();
        }

        private void RenderPager()
        {
            PageButtonsPanel.Children.Clear();

            var totalPages = TotalPages;
            if (totalPages <= 1)
            {
                PageButtonsPanel.IsVisible = false;
                return;
            }

            PageButtonsPanel.IsVisible = true;
            PageButtonsPanel.Children.Add(CreatePageButton("‹", isActive: false, isEnabled: _currentPage > 1, page: _currentPage - 1));

            foreach (var page in Miao.Core.Services.PagerNumbers.Build(_currentPage, totalPages))
            {
                if (page == null)
                {
                    PageButtonsPanel.Children.Add(new TextBlock
                    {
                        Text = "…",
                        Foreground = Brushes.Gray,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(4, 0, 4, 0)
                    });
                }
                else
                {
                    PageButtonsPanel.Children.Add(CreatePageButton(page.Value.ToString(), isActive: page == _currentPage, isEnabled: true, page: page.Value));
                }
            }

            PageButtonsPanel.Children.Add(CreatePageButton("›", isActive: false, isEnabled: _currentPage < totalPages, page: _currentPage + 1));
        }

        private Button CreatePageButton(string content, bool isActive, bool isEnabled, int page)
        {
            var button = new Button
            {
                Content = content,
                Classes = { "pageButton" },
                IsEnabled = isEnabled
            };
            if (isActive) button.Classes.Add("active");
            button.Click += (_, _) => GoToPage(page);
            return button;
        }

        // ================== Điều hướng ==================

        private void OnAuthorClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border b && b.Tag is string authorKey)
                AppNavigator.NavigateTo(new AuthorPage(authorKey));
        }
    }
}