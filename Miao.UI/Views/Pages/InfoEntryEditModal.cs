using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class InfoEntryEditModal : Border
    {
        public InfoEntryEditModal(string? existingLabel, string? existingValue, Func<string, string, Task> onSave, Func<Task>? onDelete)
        {
            Width = 360;
            Background = Brushes.White;
            CornerRadius = new Avalonia.CornerRadius(12);
            Padding = new Avalonia.Thickness(20);

            var labelBox = new TextBox { Classes = { "editTextBox" }, Text = existingLabel ?? "", PlaceholderText = "VD: Chiều cao, Tên…" };
            var valueBox = new TextBox
            {
                Classes = { "editTextBox", "multilineBox" },
                Text = existingValue ?? "",
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 36, Height = double.NaN,
                PlaceholderText = "Giá trị… (nếu nhãn là \"Tên\", mỗi dòng là 1 cách gọi khác)"
            };

            var saveButton = new Button { Content = "Lưu", Classes = { "jade" } };
            saveButton.Click += async (_, _) =>
            {
                var label = labelBox.Text?.Trim();
                if (string.IsNullOrEmpty(label)) { labelBox.BorderBrush = Brushes.IndianRed; return; }
                ModalService.Close();
                await onSave(label, valueBox.Text ?? "");
            };

            var cancelButton = new Button { Content = "Hủy", Classes = { "outline" } };
            cancelButton.Click += (_, _) => ModalService.Close();

            var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancelButton, saveButton } };

            var stack = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = existingLabel == null ? "Thêm tiêu đề con" : "Sửa tiêu đề con", Classes = { "PageTitle" }, FontSize = 18, Margin = new Avalonia.Thickness(0) },
                    labelBox, valueBox, actionsRow
                }
            };

            if (onDelete != null)
            {
                var deleteButton = new Button { Content = "Xóa", Classes = { "outline" }, HorizontalAlignment = HorizontalAlignment.Left };
                deleteButton.Click += async (_, _) =>
                {
                    var result = await DialogService.ShowYesNoAsync("Xóa mục thông tin này?", "Xác nhận xóa");
                    if (result != DialogResult.Yes) return;
                    ModalService.Close();
                    await onDelete();
                };
                stack.Children.Insert(3, deleteButton);
            }

            Child = stack;
        }
    }
}