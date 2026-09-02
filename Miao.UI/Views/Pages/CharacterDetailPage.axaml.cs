using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class CharacterDetailPage : UserControl
    {
        private static readonly Geometry PencilGeometry = Geometry.Parse(
            "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34a.9959.9959 0 00-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z");
        private static readonly Geometry XGeometry = Geometry.Parse("M18 6L6 18M6 6l12 12");
        private static readonly Geometry PlusGeometry = Geometry.Parse("M12 5v14M5 12h14");
        private static readonly Geometry BoldGeometry = Geometry.Parse("M6 4h6a3.5 3.5 0 010 7H6zM6 11h7a3.5 3.5 0 010 7H6z");
        private static readonly Geometry ItalicGeometry = Geometry.Parse("M10 4h8M6 20h8M14 4l-4 16");
        private static readonly Geometry ImageGeometry = Geometry.Parse("M4 5h16v14H4zM8 11l3 3 4-5 4 6H5z");

        private readonly Guid _groupId;
        private readonly Guid _characterId;
        private Character? _character;
        private bool _editMode;

        public CharacterDetailPage(Guid groupId, Guid characterId)
        {
            InitializeComponent();
            _groupId = groupId;
            _characterId = characterId;
            Loaded += async (_, _) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            _character = await db.Characters.FindAsync(_characterId);
            if (_character == null) { AppNavigator.NavigateTo(new CharacterGroupPage(_groupId)); return; }

            NameText.Text = _character.Name;
            RenderButtonsIcons();

            var descSections = await CharacterContentService.GetDescriptionSectionsAsync(db, _characterId);
            var infoSections = await CharacterContentService.GetInfoSectionsAsync(db, _characterId);

            RenderDescriptionColumn(descSections);
            RenderInfoColumn(infoSections);
        }

        private void RenderButtonsIcons()
        {
            RenameButton.IsVisible = _editMode;
            DeleteButton.IsVisible = _editMode;
            RenameButton.Content = new AvaloniaPath { Data = PencilGeometry, Fill = JadeBrush(), Stretch = Stretch.Uniform, Width = 14, Height = 14 };
            DeleteButton.Content = new AvaloniaPath { Data = XGeometry, Stroke = Brushes.IndianRed, StrokeThickness = 2, Stretch = Stretch.Uniform, Width = 14, Height = 14 };
            EditModeButton.Content = _editMode ? "Xong" : "Sửa";
        }

        private static IBrush JadeBrush() => Application.Current?.FindResource("AccentJade") as IBrush ?? Brushes.SeaGreen;

        private void OnBackClick(object? sender, PointerPressedEventArgs e) => AppNavigator.NavigateTo(new CharacterGroupPage(_groupId));

        private void OnToggleEditModeClick(object? sender, RoutedEventArgs e)
        {
            _editMode = !_editMode;
            _ = ReloadAsync();
        }

        private void OnRenameClick(object? sender, RoutedEventArgs e)
        {
            if (_character == null) return;
            ModalService.Show(new CharacterEditModal(_groupId, _characterId, async () => await ReloadAsync(),
                existingName: _character.Name, existingImage: _character.ImagePath));
        }

        private async void OnDeleteCharacterClick(object? sender, RoutedEventArgs e)
        {
            var result = await DialogService.ShowYesNoAsync("Bạn có chắc muốn xóa nhân vật này?", "Xóa nhân vật");
            if (result != DialogResult.Yes) return;
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            await CharacterService.DeleteCharacterAsync(db, _characterId);
            AppNavigator.NavigateTo(new CharacterGroupPage(_groupId));
        }

        private void RenderDescriptionColumn(List<CharacterDescriptionSection> sections)
        {
            DescriptionColumn.Children.Clear();

            if (_character != null && !string.IsNullOrEmpty(_character.ImagePath))
            {
                var hero = new Border
                {
                    MaxWidth = 420, MaxHeight = 260,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(10), ClipToBounds = true,
                    Child = new Image { Source = new Bitmap(_character.ImagePath), Stretch = Stretch.Uniform }
                };
                DescriptionColumn.Children.Add(hero);
            }

            DescriptionColumn.Children.Add(ColumnHeader("GIỚI THIỆU", async () =>
            {
                ModalService.Show(new TitleEditModal("Thêm tiêu đề", null, async title =>
                {
                    using var db = new MiaoDbContext(AppPaths.DbFilePath);
                    await CharacterContentService.AddDescriptionSectionAsync(db, _characterId, title);
                    await ReloadAsync();
                }, null));
                await Task.CompletedTask;
            }));

            foreach (var section in sections)
                DescriptionColumn.Children.Add(BuildDescriptionSection(section));
        }

        private Control ColumnHeader(string title, Func<Task> onAdd)
        {
            var header = new TextBlock { Text = title, Classes = { "PageTitle" }, FontSize = 18, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            var addBtn = new Button { Classes = { "addSmall" }, VerticalAlignment = VerticalAlignment.Center,
                Content = new AvaloniaPath { Data = PlusGeometry, Stroke = Brushes.White, StrokeThickness = 2, Stretch = Stretch.Uniform, Width = 12, Height = 12 } };
            if (!_editMode) addBtn.IsVisible = false;
            addBtn.Click += async (_, _) => await onAdd();

            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            Grid.SetColumn(header, 0); Grid.SetColumn(addBtn, 1);
            row.Children.Add(header); row.Children.Add(addBtn);
            return row;
        }

        private Control BuildDescriptionSection(CharacterDescriptionSection section)
        {
            var titleText = new TextBlock { Text = section.Title, FontWeight = FontWeight.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };

            var editTitleBtn = new Button
            {
                Classes = { "iconGhost" }, IsVisible = _editMode, Width = 24, Height = 24,
                Content = new AvaloniaPath { Data = PencilGeometry, Fill = JadeBrush(), Stretch = Stretch.Uniform, Width = 12, Height = 12 }
            };
            editTitleBtn.Click += (_, _) => ModalService.Show(new TitleEditModal("Sửa tiêu đề", section.Title,
                async newTitle => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.RenameDescriptionSectionAsync(db, section.Id, newTitle); await ReloadAsync(); },
                async () => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.DeleteDescriptionSectionAsync(db, section.Id); await ReloadAsync(); }));

            var addTextBtn = new Button
            {
                Classes = { "iconGhost" }, IsVisible = _editMode, Width = 24, Height = 24,
                Content = new TextBlock { Text = "T+", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = JadeBrush() }
            };
            addTextBtn.Click += async (_, _) =>
            {
                var count = section.Blocks.Count;
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                await CharacterContentService.InsertTextBlockAsync(db, section.Id, count);
                await ReloadAsync();
            };

            var addImageBtn = new Button
            {
                Classes = { "iconGhost" }, IsVisible = _editMode, Width = 24, Height = 24,
                Content = new AvaloniaPath { Data = ImageGeometry, Stroke = JadeBrush(), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 12, Height = 12 }
            };
            addImageBtn.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider is not { } storage) return;
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Chọn ảnh", AllowMultiple = false, FileTypeFilter = new[] { FilePickerFileTypes.ImageAll } });
                var file = files.FirstOrDefault();
                if (file == null) return;

                var bitmap = new Bitmap(file.Path.LocalPath);
                using var msOut = new MemoryStream();
        #pragma warning disable CS0618
                bitmap.Save(msOut);
        #pragma warning restore CS0618

                var count = section.Blocks.Count;
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                await CharacterContentService.InsertImageBlockAsync(db, section.Id, count, msOut.ToArray());
                await ReloadAsync();
            };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { titleText, editTitleBtn, addTextBtn, addImageBtn } };
            var divider = new Border { Classes = { "infoDivider" } };

            var blocksPanel = new StackPanel { Spacing = 8 };
            foreach (var block in section.Blocks.OrderBy(b => b.SortOrder))
                blocksPanel.Children.Add(BuildBlock(block));

            return new StackPanel { Spacing = 6, Children = { titleRow, divider, blocksPanel } };
        }

        private Control BuildBlock(CharacterDescriptionBlock block)
        {
            if (block.Type == DescriptionBlockType.Image)
            {
                var img = new Image { Source = string.IsNullOrEmpty(block.ImagePath) ? null : new Bitmap(block.ImagePath), Stretch = Stretch.Uniform, MaxHeight = 320 };
                if (!_editMode) return img;

                var delBtn = new Button { Classes = { "cornerDanger" }, Content = "✕", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
                delBtn.Click += async (_, _) => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.DeleteBlockAsync(db, block.Id); await ReloadAsync(); };
                return new Panel { Children = { img, delBtn } };
            }

            if (!_editMode)
            {
                var view = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24 };
                view.Inlines?.AddRange(MiniMarkdown.ToInlines(block.TextContent));
                return view;
            }

            var textBox = new TextBox
            {
                Classes = { "editTextBox", "multilineBox" },
                Text = block.TextContent,
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 36, Height = double.NaN
            };
            textBox.LostFocus += async (_, _) => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.UpdateTextBlockAsync(db, block.Id, textBox.Text ?? ""); };

            var boldBtn = new Button { Classes = { "iconGhost" }, Width = 26, Height = 26, Focusable = false,
                Content = new AvaloniaPath { Data = BoldGeometry, Stroke = JadeBrush(), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 12, Height = 12 } };
            boldBtn.Click += (_, _) => WrapTextBoxSelection(textBox, "**");

            var italicBtn = new Button { Classes = { "iconGhost" }, Width = 26, Height = 26, Focusable = false,
                Content = new AvaloniaPath { Data = ItalicGeometry, Stroke = JadeBrush(), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 12, Height = 12 } };
            italicBtn.Click += (_, _) => WrapTextBoxSelection(textBox, "*");

            var delBlockBtn = new Button { Classes = { "cornerDanger" }, Content = "✕" };
            delBlockBtn.Click += async (_, _) => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.DeleteBlockAsync(db, block.Id); await ReloadAsync(); };

            var toolbar = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            Grid.SetColumn(boldBtn, 0); Grid.SetColumn(italicBtn, 1); Grid.SetColumn(delBlockBtn, 3);
            toolbar.Children.Add(boldBtn); toolbar.Children.Add(italicBtn); toolbar.Children.Add(delBlockBtn);

            return new StackPanel { Spacing = 4, Children = { toolbar, textBox } };
        }

        private static void WrapTextBoxSelection(TextBox box, string marker)
        {
            var text = box.Text ?? "";
            var (newText, newStart, newEnd) = MiniMarkdown.WrapSelection(text, box.SelectionStart, box.SelectionEnd, marker);
            box.Text = newText;
            box.SelectionStart = newStart;
            box.SelectionEnd = newEnd;
        }

        private void RenderInfoColumn(List<CharacterInfoSection> sections)
        {
            InfoColumn.Children.Clear();

            InfoColumn.Children.Add(ColumnHeader("THÔNG TIN", async () =>
            {
                ModalService.Show(new TitleEditModal("Thêm tiêu đề", null, async title =>
                {
                    using var db = new MiaoDbContext(AppPaths.DbFilePath);
                    await CharacterContentService.AddInfoSectionAsync(db, _characterId, title);
                    await ReloadAsync();
                }, null));
                await Task.CompletedTask;
            }));

            foreach (var section in sections)
                InfoColumn.Children.Add(BuildInfoSection(section));
        }

        private Control BuildInfoSection(CharacterInfoSection section)
        {
            var titleText = new TextBlock { Text = section.Title, FontWeight = FontWeight.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };

            var editTitleBtn = new Button { Classes = { "iconGhost" }, IsVisible = _editMode,
                Content = new AvaloniaPath { Data = PencilGeometry, Fill = JadeBrush(), Stretch = Stretch.Uniform, Width = 12, Height = 12 } };
            editTitleBtn.Click += (_, _) => ModalService.Show(new TitleEditModal("Sửa tiêu đề", section.Title,
                async newTitle => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.RenameInfoSectionAsync(db, section.Id, newTitle); await ReloadAsync(); },
                async () => { using var db = new MiaoDbContext(AppPaths.DbFilePath); await CharacterContentService.DeleteInfoSectionAsync(db, section.Id); await ReloadAsync(); }));

            var addEntryBtn = new Button { Classes = { "addSmall" }, IsVisible = _editMode,
                Content = new AvaloniaPath { Data = PlusGeometry, Stroke = Brushes.White, StrokeThickness = 2, Stretch = Stretch.Uniform, Width = 10, Height = 10 } };
            addEntryBtn.Click += (_, _) => ModalService.Show(new InfoEntryEditModal(null, null, async (label, value) =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                await CharacterContentService.AddInfoEntryAsync(db, section.Id, label, value);
                await ReloadAsync();
            }, null));

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { titleText, editTitleBtn, addEntryBtn } };
            var divider = new Border { Classes = { "infoDivider" } };

            var entriesGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, RowSpacing = 6, ColumnSpacing = 10 };
            var entries = section.Entries.OrderBy(e => e.SortOrder).ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                entriesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var entry = entries[i];

                var labelText = new TextBlock { Text = entry.Label, FontWeight = FontWeight.Bold, Foreground = JadeBrush(), TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
                Grid.SetRow(labelText, i); Grid.SetColumn(labelText, 0);

                Control valueControl;
                var lines = entry.Value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

                if (lines.Count > 1)
                {
                    valueControl = BuildBulletList(lines);
                }
                else
                {
                    valueControl = new TextBlock { Text = lines.Count == 1 ? lines[0] : entry.Value, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
                }
                Grid.SetRow(valueControl, i); Grid.SetColumn(valueControl, 1);

                entriesGrid.Children.Add(labelText);
                entriesGrid.Children.Add(valueControl);

                if (_editMode)
                {
                    var editBtn = new Button { Classes = { "iconGhost" }, Width = 20, Height = 20,
                        Content = new AvaloniaPath { Data = PencilGeometry, Fill = JadeBrush(), Stretch = Stretch.Uniform, Width = 10, Height = 10 } };
                    Grid.SetRow(editBtn, i); Grid.SetColumn(editBtn, 1); editBtn.HorizontalAlignment = HorizontalAlignment.Right;
                    editBtn.Click += (_, _) => ModalService.Show(new InfoEntryEditModal(entry.Label, entry.Value, async (label, value) =>
                    {
                        using var db = new MiaoDbContext(AppPaths.DbFilePath);
                        await CharacterContentService.UpdateInfoEntryAsync(db, entry.Id, label, value);
                        await ReloadAsync();
                    }, async () =>
                    {
                        using var db = new MiaoDbContext(AppPaths.DbFilePath);
                        await CharacterContentService.DeleteInfoEntryAsync(db, entry.Id);
                        await ReloadAsync();
                    }));
                    entriesGrid.Children.Add(editBtn);
                }
            }

            return new StackPanel { Spacing = 6, Children = { titleRow, divider, entriesGrid } };
        }

        private static Control BuildBulletList(List<string> lines)
        {
            var stack = new StackPanel { Spacing = 4 };
            foreach (var line in lines)
            {
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
                var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top, LineHeight = 22 };
                var text = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
                Grid.SetColumn(bullet, 0); Grid.SetColumn(text, 1);
                row.Children.Add(bullet); row.Children.Add(text);
                stack.Children.Add(row);
            }
            return stack;
        }
    }
}