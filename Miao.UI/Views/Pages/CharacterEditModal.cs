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
    public class CharacterEditModal : Border
    {
        private readonly Guid _groupId;
        private readonly Guid? _existingId;
        private readonly Func<Task> _onSaved;
        private readonly TextBox _nameBox;
        private readonly ComboBox _factionBox;
        private readonly List<CharacterFaction> _factions;
        private readonly InlineImageCropper _cropper;

        public CharacterEditModal(Guid groupId, Guid? existingId, Func<Task> onSaved,
            string? existingName = null, string? existingImage = null, string? existingDescription = null, Guid? existingFactionId = null)
        {
            _groupId = groupId;
            _existingId = existingId;
            _onSaved = onSaved;

            Width = 380;
            Background = Brushes.White;
            CornerRadius = new Avalonia.CornerRadius(12);
            Padding = new Avalonia.Thickness(20);

            _nameBox = new TextBox { Classes = { "editTextBox" }, Text = existingName ?? "", PlaceholderText = "VD: Tsunayoshi Sawada" };

            _cropper = new InlineImageCropper();
            if (!string.IsNullOrEmpty(existingImage)) _cropper.SetSource(existingImage);

            var pickButton = new Button { Content = "Chọn ảnh khác", Classes = { "jade" } };
            pickButton.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider is not { } storage) return;

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Chọn ảnh nhân vật",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
                });

                var file = files.FirstOrDefault();
                if (file != null) _cropper.SetSource(file.Path.LocalPath);
            };

            var saveButton = new Button { Content = "Lưu", Classes = { "jade" } };
            saveButton.Click += async (_, _) => await SaveAsync();

            var cancelButton = new Button { Content = "Hủy", Classes = { "outline" } };
            cancelButton.Click += (_, _) => ModalService.Close();

            var actionsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Children = { cancelButton, saveButton }
            };

            using (var db = new MiaoDbContext(AppPaths.DbFilePath))
                _factions = CharacterFactionService.GetFactionsAsync(db, groupId).Result;

            _factionBox = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

            var factionOptions = new List<(string Label, Guid? Id)> { ("Chưa phân nhóm", null) };
            factionOptions.AddRange(_factions.Select(f => (f.Name, (Guid?)f.Id)));

            _factionBox.ItemsSource = factionOptions.Select(o => o.Label).ToList();
            _factionBox.SelectedIndex = 0;

            if (existingFactionId != null)
            {
                var idx = factionOptions.FindIndex(o => o.Id == existingFactionId);
                if (idx >= 0) _factionBox.SelectedIndex = idx;
            }

            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = existingId == null ? "Thêm nhân vật" : "Sửa nhân vật", Classes = { "PageTitle" }, FontSize = 18, Margin = new Avalonia.Thickness(0) },
                    _nameBox,
                    _cropper,
                    pickButton,
                    _factionBox,
                    actionsRow
                }
            };
        }

        private async Task SaveAsync()
        {
            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _nameBox.BorderBrush = Brushes.IndianRed; return; }

            byte[]? imageBytes = _cropper.HasImage ? _cropper.GetCroppedPngBytes() : null;
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            Guid savedCharacterId;

            if (_existingId is { } id)
            {
                savedCharacterId = id;
                await CharacterService.UpdateCharacterAsync(db, id, name, imageBytes, "");
            }
            else
            {
                var count = db.Characters.Count(c => c.CharacterGroupId == _groupId);
                var character = await CharacterService.CreateCharacterAsync(db, _groupId, name, imagePath: "", description: "", sortOrder: count);

                savedCharacterId = character.Id;

                if (imageBytes != null) await CharacterService.UpdateCharacterAsync(db, character.Id, name, imageBytes, "");
            }

            var selectedIdx = _factionBox.SelectedIndex;
            Guid? selectedFactionId = selectedIdx > 0 ? _factions[selectedIdx - 1].Id : null;

            await CharacterFactionService.SetCharacterFactionAsync(db, savedCharacterId, selectedFactionId);

            ModalService.Close();
            await _onSaved();
        }
    }
}