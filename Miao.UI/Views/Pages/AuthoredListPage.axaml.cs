using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class AuthoredListPage : UserControl
    {
        private const int ItemsPerRow = 5;
        private const int RowsPerPage = 3;
        private const int PageSize = ItemsPerRow * RowsPerPage;

        private readonly List<WrittenNovelCardItem> _novels;
        private int _currentPage = 1;

        public AuthoredListPage(List<WrittenNovelCardItem> novels)
        {
            InitializeComponent();
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
            if (sender is not Control fe || fe.Tag is not WrittenNovelCardItem item) return;
            AppNavigator.NavigateTo(new WriteNovelDetailPage(item.Id));
        }
    }
}