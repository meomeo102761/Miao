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
        private const int PageSize = ItemsPerRow * RowsPerPage;

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

            PaginationHelper.Build(PageButtonsPanel, totalPages, _currentPage, p => { _currentPage = p; RenderPage(); });
        }

        private void OnNovelClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not Novel novel) return;

            try
            {
                AppNavigator.NavigateTo(new NovelDetailPage(novel.Id));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnNovelClick] Lỗi khi mở NovelDetailPage: {ex}");
            }
        }
    }
}