using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Miao.UI.Services
{
    public static class AppNavigator
    {
        // ContentControl chính, được gán từ MainView khi khởi tạo
        public static ContentControl? MainContent { get; set; }

        // Lưu lịch sử điều hướng để hỗ trợ nút "Quay lại" nếu cần sau này
        private static readonly Stack<Control> _history = new();

        public static void NavigateTo(Control page)
        {
            if (MainContent is null)
                throw new InvalidOperationException("AppNavigator.MainContent chưa được gán. Hãy gọi từ MainView trước.");

            if (MainContent.Content is Control current)
                _history.Push(current);

            MainContent.Content = page;
        }

        public static bool CanGoBack => _history.Count > 0;

        public static void GoBack()
        {
            if (MainContent is null || _history.Count == 0)
                return;

            MainContent.Content = _history.Pop();
        }
    }
}