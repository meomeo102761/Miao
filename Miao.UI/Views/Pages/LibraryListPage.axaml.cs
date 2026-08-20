using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Miao.Core.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class LibraryListPage : UserControl
    {
        private const int ItemsPerRow = 5;
        private const int RowsPerPage = 3;
        private const int PageSize = ItemsPerRow * RowsPerPage; // 15

        private readonly List<Novel> _novels;
        private int _currentPage = 1;

        public LibraryListPage(string title, List<Novel> novels)
        {
            InitializeComponent();
            TitleText.Text = title;
            _novels = novels;
            RenderPage();
        }

        private void OnBackClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new LibraryPage());

        private void RenderPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(_novels.Count / (double)PageSize));
            if (_currentPage > totalPages) _currentPage = totalPages;

            NovelsList.ItemsSource = _novels
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            BuildPageButtons(totalPages);
        }

        private void BuildPageButtons(int totalPages)
        {
            PageButtonsPanel.Children.Clear();

            var pageButtonStyle = (Style)this.FindResource("PageButton")!;

            var prevBtn = new Button { Content = "‹ Trước", Style = pageButtonStyle, IsEnabled = _currentPage > 1 };
            prevBtn.Classes.Add("pageButton");
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
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    });
                    continue;
                }

                var btn = new Button { Content = p.ToString(), Style = pageButtonStyle };
                btn.Classes.Add("pageButton");
                if (p == _currentPage) btn.Classes.Add("active");
                int page = p;
                btn.Click += (s, e) => { _currentPage = page; RenderPage(); };
                PageButtonsPanel.Children.Add(btn);
            }

            var nextBtn = new Button { Content = "Sau ›", Style = pageButtonStyle, IsEnabled = _currentPage < totalPages };
            nextBtn.Classes.Add("pageButton");
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

        private void OnNovelClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is Novel novel)
                AppNavigator.NavigateTo(new NovelDetailPage(novel.Id));
        }
    }
}