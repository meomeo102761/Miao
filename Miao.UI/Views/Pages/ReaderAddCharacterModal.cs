using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class ReaderAddCharacterModal : Border
    {
        private readonly Guid _novelId;
        private readonly string _suggestedName;
        private readonly Action _onSaved;
        private readonly TextBox _nameBox;
        private readonly InlineImageCropper _cropper;

        public ReaderAddCharacterModal(Guid novelId, string suggestedName, Action onSaved)
        {
            _novelId = novelId;
            _suggestedName = suggestedName;
            _onSaved = onSaved;

            Width = 380;
            Background = Brushes.White;
            CornerRadius = new Avalonia.CornerRadius(12);
            Padding = new Avalonia.Thickness(20);

            _nameBox = new TextBox { Classes = { "editTextBox" }, Text = suggestedName, PlaceholderText = "Tên nhân vật…" };
            _cropper = new InlineImageCropper();

            var pickButton = new Button { Content = "Chọn ảnh", Classes = { "jade" } };
            pickButton.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider is not { } storage) return;
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Chọn ảnh nhân vật", AllowMultiple = false, FileTypeFilter = new[] { FilePickerFileTypes.ImageAll } });
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
                    new TextBlock { Text = "Thêm nhân vật", Classes = { "PageTitle" }, FontSize = 18, Margin = new Avalonia.Thickness(0) },
                    _nameBox, _cropper, pickButton, actionsRow
                }
            };
        }

        private async Task SaveAsync()
        {
            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _nameBox.BorderBrush = Brushes.IndianRed; return; }

            byte[]? imageBytes = _cropper.HasImage ? _cropper.GetCroppedPngBytes() : null;
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var group = db.CharacterGroups.FirstOrDefault(g => g.OwnerNovelId == _novelId);
            if (group == null)
            {
                group = new CharacterGroup { OwnerNovelId = _novelId, Name = "Nhân vật truyện" };
                db.CharacterGroups.Add(group);
                await db.SaveChangesAsync();
            }

            if (group == null)
            {
                group = new CharacterGroup { OwnerNovelId = _novelId, Name = "Nhân vật truyện" };
                db.CharacterGroups.Add(group);
                await db.SaveChangesAsync();
            }
            await NovelCharacterGroupService.AttachAsync(db, _novelId, group.Id);

            var sortOrder = db.Characters.Count(c => c.CharacterGroupId == group.Id);
            var character = await CharacterService.CreateCharacterAsync(db, group.Id, name, imagePath: "", description: "", sortOrder: sortOrder);

            if (imageBytes != null)
                await CharacterService.UpdateCharacterAsync(db, character.Id, name, imageBytes, "");

            if (!string.Equals(name, _suggestedName, StringComparison.OrdinalIgnoreCase))
                await CharacterService.AddAliasAsync(db, character.Id, _suggestedName);

            ModalService.Close();
            _onSaved();
        }
    }
}