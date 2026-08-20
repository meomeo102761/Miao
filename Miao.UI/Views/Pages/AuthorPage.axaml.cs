using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.Core.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class AuthorPage : UserControl
    {
        private const int ItemsPerRow = 5;
        private const int RowsPerPage = 3;
        private const int PageSize = ItemsPerRow * RowsPerPage;

        private readonly List<Novel> _novels;
        private int _currentPage = 1;

        public AuthorPage(string authorName)
        {
            InitializeComponent();

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            _novels = db.Novels.Where(n => n.Author == authorName).ToList();

            NovelEnrichmentService.ApplyDisplayInfo(db, _novels);

            AuthorNameText.Text = _novels
                .Select(n => n.TranslatedAuthor)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                ?? authorName;

            RenderPage();
        }
        
        private int TotalPages => Math.Max(1, (int)Math.Ceiling(_novels.Count / (double)PageSize));

        private void GoToPage(int page)
        {
            _currentPage = Math.Clamp(page, 1, TotalPages);
            RenderPage();
        }

        private void RenderPage()
        {
            _currentPage = Math.Clamp(_currentPage, 1, TotalPages);

            NovelsList.ItemsSource = _novels
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

            foreach (var page in PagerNumbers.Build(_currentPage, totalPages))
            {
                if (page == null)
                {
                    PageButtonsPanel.Children.Add(new TextBlock
                    {
                        Text = "…",
                        Foreground = Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
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

        private void OnNovelClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control c && c.Tag is Novel novel)
                AppNavigator.NavigateTo(new NovelDetailPage(novel.Id));
        }

        private void OnBackToAuthorListClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new AuthorListPage());
    }
}