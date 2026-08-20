using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Miao.UI.Services
{
    public enum DialogResult { Yes, No, Cancel }

    /// <summary>
    /// Thay thế MessageBox.Show của WPF (không tồn tại trong Avalonia).
    /// Khác biệt quan trọng: MessageBox.Show là đồng bộ (block luồng UI),
    /// còn Avalonia không hỗ trợ dialog blocking kiểu đó — phải dùng
    /// async/await + TaskCompletionSource, dựa trên ModalService đã có.
    /// Chỗ gọi cần đổi "var x = MessageBox.Show(...)" thành "var x = await DialogService.Show...Async(...)".
    /// </summary>
    public static class DialogService
    {
        public static Task<DialogResult> ShowYesNoCancelAsync(string message, string title = "")
            => ShowAsync(message, title, showCancel: true);

        public static Task<DialogResult> ShowYesNoAsync(string message, string title = "")
            => ShowAsync(message, title, showCancel: false);

        private static Task<DialogResult> ShowAsync(string message, string title, bool showCancel)
        {
            var tcs = new TaskCompletionSource<DialogResult>();

            IBrush accentJade = Application.Current?.FindResource("AccentJade") as IBrush ?? Brushes.Teal;
            IBrush textPrimary = Application.Current?.FindResource("TextPrimary") as IBrush ?? Brushes.Black;
            IBrush borderSoft = Application.Current?.FindResource("BorderSoft") as IBrush ?? Brushes.LightGray;

            void Close(DialogResult result)
            {
                ModalService.Close();
                tcs.TrySetResult(result);
            }

            var content = new StackPanel();

            if (!string.IsNullOrWhiteSpace(title))
            {
                content.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.Bold,
                    FontSize = 16,
                    Foreground = textPrimary,
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 15,
                Foreground = textPrimary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var yesButton = new Button
            {
                Content = "Có",
                Width = 90, Height = 36,
                Background = accentJade,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            yesButton.Click += (_, _) => Close(DialogResult.Yes);
            buttonsPanel.Children.Add(yesButton);

            var noButton = new Button
            {
                Content = "Không",
                Width = 90, Height = 36,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                BorderBrush = borderSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            noButton.Click += (_, _) => Close(DialogResult.No);
            buttonsPanel.Children.Add(noButton);

            if (showCancel)
            {
                var cancelButton = new Button
                {
                    Content = "Hủy",
                    Width = 90, Height = 36,
                    Background = Brushes.White,
                    Foreground = Brushes.Gray,
                    BorderBrush = borderSoft,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(18),
                    Margin = new Thickness(8, 0, 0, 0),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                cancelButton.Click += (_, _) => Close(DialogResult.Cancel);
                buttonsPanel.Children.Add(cancelButton);
            }

            content.Children.Add(buttonsPanel);

            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                Width = 420,
                Padding = new Thickness(24),
                Child = content
            };

            ModalService.Show(card);
            return tcs.Task;
        }
    }
}