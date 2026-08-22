using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Miao.UI.Views;

namespace Miao.UI.Services
{
    public static class AppNavigator
    {
        public static ContentControl? MainContent { get; set; }
        private static readonly Stack<Control> _history = new();

        public static void NavigateTo(Control page)
        {
            if (MainContent is null)
                throw new InvalidOperationException("AppNavigator.MainContent chưa được gán. Hãy gọi từ MainView trước.");

            if (MainContent.Content is Control current)
                _history.Push(current);

            MainContent.Content = page;

            if (MainView.Current != null)
                MainView.Current.Offset = new Vector(0, 0);
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