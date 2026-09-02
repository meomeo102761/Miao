using System;
using System.Collections.Generic;
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
        private readonly ComboBox _factionBox;
        private readonly InlineImageCropper _cropper;

        private Guid? _existingGroupId;
        private List<CharacterFaction> _factions = new();

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
            _factionBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
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
                    _nameBox,
                    new TextBlock { Text = "Nhóm:", FontSize = 13, Margin = new Avalonia.Thickness(0) },
                    _factionBox,
                    _cropper, pickButton, actionsRow
                }
            };

            _ = LoadFactionsAsync();
        }

        private async Task LoadFactionsAsync()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var group = db.CharacterGroups.FirstOrDefault(g => g.OwnerNovelId == _novelId);
            _existingGroupId = group?.Id;

            _factions = group == null
                ? new List<CharacterFaction>()
                : await CharacterFactionService.GetFactionsAsync(db, group.Id);

            var items = new List<string> { "Không thuộc nhóm" };
            items.AddRange(_factions.Select(f => f.Name));

            _factionBox.ItemsSource = items;
            _factionBox.SelectedIndex = 0;
        }

        private async Task SaveAsync()
        {
            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _nameBox.BorderBrush = Brushes.IndianRed; return; }

            byte[]? imageBytes = _cropper.HasImage ? _cropper.GetCroppedPngBytes() : null;
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var group = _existingGroupId != null
                ? db.CharacterGroups.Find(_existingGroupId.Value)
                : db.CharacterGroups.FirstOrDefault(g => g.OwnerNovelId == _novelId);

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

            var selectedIndex = _factionBox.SelectedIndex;
            Guid? selectedFactionId = selectedIndex > 0 && selectedIndex - 1 < _factions.Count
                ? _factions[selectedIndex - 1].Id
                : null;

            if (selectedFactionId != null)
                await CharacterFactionService.SetCharacterFactionAsync(db, character.Id, selectedFactionId);

            if (!string.Equals(name, _suggestedName, StringComparison.OrdinalIgnoreCase))
                await CharacterService.AddAliasAsync(db, character.Id, _suggestedName);

            ModalService.Close();
            _onSaved();
        }
    }
}