using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class CharacterGroupEditModal : Border
    {
        private readonly Guid? _existingId;
        private readonly Func<Task> _onSaved;
        private readonly TextBox _nameBox;
        private readonly InlineImageCropper _cropper;

        public CharacterGroupEditModal(Guid? existingId, Func<Task> onSaved,
            string? existingName = null, string? existingCover = null)
        {
            _existingId = existingId;
            _onSaved = onSaved;

            Width = 380;
            Background = Brushes.White;
            CornerRadius = new Avalonia.CornerRadius(12);
            Padding = new Avalonia.Thickness(20);

            _nameBox = new TextBox { Classes = { "editTextBox" }, Text = existingName ?? "", PlaceholderText = "VD: Naruto, One Piece…" };

            _cropper = new InlineImageCropper();
            if (!string.IsNullOrEmpty(existingCover)) _cropper.SetSource(existingCover);

            var pickButton = new Button { Content = "Chọn ảnh khác", Classes = { "jade" } };
            pickButton.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider is not { } storage) return;
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Chọn ảnh bìa", AllowMultiple = false, FileTypeFilter = new[] { FilePickerFileTypes.ImageAll } });
                var file = files.FirstOrDefault();
                if (file != null) _cropper.SetSource(file.Path.LocalPath);
            };

            var saveButton = new Button { Content = "Lưu", Classes = { "jade" } };
            saveButton.Click += async (_, _) => await SaveAsync();
            var cancelButton = new Button { Content = "Hủy", Classes = { "outline" } };
            cancelButton.Click += (_, _) => ModalService.Close();
            var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancelButton, saveButton } };

            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = existingId == null ? "Thêm dàn nhân vật" : "Sửa dàn nhân vật", Classes = { "PageTitle" }, FontSize = 18, Margin = new Avalonia.Thickness(0) },
                    _nameBox, _cropper, pickButton, actionsRow
                }
            };
        }

        private async Task SaveAsync()
        {
            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _nameBox.BorderBrush = Brushes.IndianRed; return; }

            byte[]? coverBytes = _cropper.HasImage ? _cropper.GetCroppedPngBytes() : null;
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            if (_existingId is { } id)
                await CharacterGroupService.UpdateAsync(db, id, name, coverBytes);
            else
                await CharacterGroupService.CreateAsync(db, name, coverBytes);

            ModalService.Close();
            await _onSaved();
        }
    }
}