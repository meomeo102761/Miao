using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    /// <summary>
    /// Base cho các trang có sẵn hộp thoại xác nhận (Xóa/Hủy) kiểu modal.
    /// Gom logic ShowModal/ShowConfirm/FindVisualChildren từng bị copy-paste
    /// giống hệt nhau ở CustomLibrariesPage và CustomLibraryDetailPage.
    ///
    /// Trang con cần có sẵn trong XAML: 1 Border/Control tên "ConfirmCard"
    /// và 1 TextBlock tên "ConfirmMessageText", rồi override 2 property bên dưới
    /// để trỏ tới đúng field được XAML sinh ra.
    /// </summary>
    public abstract class ConfirmablePage : UserControl
    {
        protected abstract Control ConfirmCardElement { get; }
        protected abstract TextBlock ConfirmMessageTextElement { get; }

        private Action? _pendingConfirmAction;

        protected void ShowModal(Control card)
        {
            if (card.Parent is Panel panel)
                panel.Children.Remove(card);

            card.IsVisible = true;
            ModalService.Show(card);
        }

        protected void ShowConfirm(string message, Action onConfirm)
        {
            ConfirmMessageTextElement.Text = message;
            _pendingConfirmAction = onConfirm;
            ShowModal(ConfirmCardElement);
        }

        protected void OnConfirmYesClick(object? sender, RoutedEventArgs e)
        {
            var action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            ModalService.Close();
            action?.Invoke();
        }

        protected void OnConfirmNoClick(object? sender, RoutedEventArgs e)
        {
            _pendingConfirmAction = null;
            ModalService.Close();
        }

        // Avalonia đã có sẵn GetVisualDescendants() trong Avalonia.VisualTree,
        // không cần tự viết đệ quy như WPF (VisualTreeHelper).
        protected static IEnumerable<T> FindVisualChildren<T>(Visual root) where T : Visual
        {
            if (root is null) yield break;

            foreach (var descendant in root.GetVisualDescendants())
            {
                if (descendant is T match)
                    yield return match;
            }
        }
    }
}