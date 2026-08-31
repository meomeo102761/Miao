using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class TitleEditModal : Border
    {
        public TitleEditModal(string headerText, string? existingTitle, Func<string, Task> onSave, Func<Task>? onDelete)
        {
            Width = 340;
            Background = Brushes.White;
            CornerRadius = new Avalonia.CornerRadius(12);
            Padding = new Avalonia.Thickness(20);

            var nameBox = new TextBox { Classes = { "editTextBox" }, Text = existingTitle ?? "", PlaceholderText = "Tên tiêu đề…" };

            var saveButton = new Button { Content = "Lưu", Classes = { "jade" } };
            saveButton.Click += async (_, _) =>
            {
                var text = nameBox.Text?.Trim();
                if (string.IsNullOrEmpty(text)) { nameBox.BorderBrush = Brushes.IndianRed; return; }
                ModalService.Close();
                await onSave(text);
            };

            var cancelButton = new Button { Content = "Hủy", Classes = { "outline" } };
            cancelButton.Click += (_, _) => ModalService.Close();

            var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancelButton, saveButton } };

            var contentStack = new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Text = headerText, Classes = { "PageTitle" }, FontSize = 18, Margin = new Avalonia.Thickness(0) }, nameBox, actionsRow }
            };

            if (onDelete != null)
            {
                var deleteButton = new Button { Content = "Xóa", Classes = { "outline" }, HorizontalAlignment = HorizontalAlignment.Left };
                deleteButton.Click += async (_, _) =>
                {
                    var result = await DialogService.ShowYesNoAsync("Xóa tiêu đề này cùng toàn bộ nội dung bên trong?", "Xác nhận xóa");
                    if (result != DialogResult.Yes) return;
                    ModalService.Close();
                    await onDelete();
                };
                contentStack.Children.Insert(2, deleteButton);
            }

            Child = contentStack;
        }
    }
}