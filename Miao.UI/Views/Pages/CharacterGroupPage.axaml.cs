using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class CharacterGroupPage : UserControl
    {
        private static readonly Geometry PencilGeometry = Geometry.Parse(
            "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34a.9959.9959 0 00-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z");

        private const double BannerHeight = 220;

        private readonly Guid _groupId;
        private CharacterGroup? _group;
        private List<Character> _characters = new();
        private List<CharacterFaction> _factions = new();

        private readonly HashSet<Guid> _collapsedFactions = new();

        private bool _editMode;

        private Bitmap? _bannerBitmap;
        private bool _bannerDragging;
        private Point _bannerDragStart;
        private double _bannerDragStartFocalX;
        private double _bannerDragStartFocalY;

        private Border? _draggingCard;
        private Point _dragStartPointerPos;
        private bool _hasDraggedPastThreshold;

        private StackPanel? _draggingFaction;
        private Point _factionDragStartPointerPos;
        private bool _hasDraggedFactionPastThreshold;

        private double _bannerScale;
        private string _searchText = "";

        public CharacterGroupPage(Guid groupId)
        {
            InitializeComponent();
            _groupId = groupId;
            Loaded += async (_, _) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            _group = await db.CharacterGroups.FindAsync(_groupId);

            if (_group == null)
            {
                AppNavigator.NavigateTo(new CharactersPage());
                return;
            }

            GroupNameText.Text = _group.Name;

            _bannerBitmap = string.IsNullOrEmpty(_group.CoverImagePath)
                ? null
                : new Bitmap(_group.CoverImagePath);

            ApplyBannerTransform();

            _characters = await db.Characters
                .Where(c => c.CharacterGroupId == _groupId)
                .OrderBy(c => c.SortOrder)
                .ToListAsync();

            _factions = await CharacterFactionService.GetFactionsAsync(db, _groupId);

            RenderCards();
        }

        private void OnAddFactionClick(object? sender, RoutedEventArgs e)
        {
            ModalService.Show(new TitleEditModal("Thêm nhóm", null, async title =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                await CharacterFactionService.AddFactionAsync(db, _groupId, title);
                await ReloadAsync();
            }, null));
        }

        private void RenderCards()
        {
            SectionsPanel.Children.Clear();

            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? _characters
                : _characters
                    .Where(c => c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            EmptyHint.IsVisible = _characters.Count == 0;

            if (_characters.Count == 0)
                return;

            var unassigned = filtered
                .Where(c => c.FactionId == null)
                .OrderBy(c => c.SortOrder)
                .ToList();

            if (unassigned.Count > 0)
                SectionsPanel.Children.Add(BuildFactionSection(null, unassigned));

            foreach (var faction in _factions.OrderBy(f => f.SortOrder))
            {
                var members = filtered
                    .Where(c => c.FactionId == faction.Id)
                    .OrderBy(c => c.SortOrder)
                    .ToList();

                if (members.Count == 0 && !string.IsNullOrWhiteSpace(_searchText))
                    continue;

                SectionsPanel.Children.Add(BuildFactionSection(faction, members));
            }
        }

        private Control BuildFactionSection(CharacterFaction? faction, List<Character> members)
        {
            var stack = new StackPanel
            {
                Spacing = 10,
                Tag = faction?.Id
            };

            if (faction != null)
            {
                var isCollapsed = _collapsedFactions.Contains(faction.Id);

                var dragHandle = new TextBlock
                {
                    Text = "::",
                    FontSize = 14,
                    Width = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.SizeAll),
                    IsVisible = _editMode
                };

                var arrowPath = new Path
                {
                    Data = Geometry.Parse(isCollapsed
                        ? "M 1,0 L 7,4 L 1,8 Z"
                        : "M 0,1 L 8,1 L 4,7 Z"),
                    Fill = Brushes.Black,
                    Width = 10,
                    Height = 10,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var arrowButton = new Button
                {
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Padding = new Thickness(2),
                    Width = 18,
                    Height = 18,
                    Content = arrowPath,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                var titleText = new TextBlock
                {
                    Text = faction.Name,
                    FontWeight = FontWeight.Bold,
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                var editBtn = new Button
                {
                    Classes = { "iconGhost" },
                    IsVisible = _editMode,
                    Width = 24,
                    Height = 24,
                    Content = new Path
                    {
                        Data = PencilGeometry,
                        Fill = Application.Current?.FindResource("AccentJade") as IBrush ?? Brushes.SeaGreen,
                        Stretch = Stretch.Uniform,
                        Width = 12,
                        Height = 12
                    }
                };

                editBtn.Click += (_, _) => ModalService.Show(new TitleEditModal(
                    "Sửa tên nhóm",
                    faction.Name,
                    async newName =>
                    {
                        using var db = new MiaoDbContext(AppPaths.DbFilePath);
                        await CharacterFactionService.RenameFactionAsync(db, faction.Id, newName);
                        await ReloadAsync();
                    },
                    async () =>
                    {
                        using var db = new MiaoDbContext(AppPaths.DbFilePath);
                        await CharacterFactionService.DeleteFactionAsync(db, faction.Id);
                        await ReloadAsync();
                    }));

                var titleRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { dragHandle, arrowButton, titleText, editBtn }
                };

                arrowButton.Click += (_, _) => ToggleFactionCollapse(faction.Id);

                titleText.PointerPressed += (_, e) =>
                {
                    ToggleFactionCollapse(faction.Id);
                    e.Handled = true;
                };

                dragHandle.PointerPressed += OnFactionDragHandlePressed;
                dragHandle.PointerMoved += OnFactionDragHandleMoved;
                dragHandle.PointerReleased += OnFactionDragHandleReleased;

                stack.Children.Add(titleRow);
            }

            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 16,
                LineSpacing = 16,
                IsVisible = faction == null || !_collapsedFactions.Contains(faction.Id)
            };

            foreach (var ch in members)
                panel.Children.Add(BuildCard(ch, panel));

            stack.Children.Add(panel);

            return stack;
        }

        private void ToggleFactionCollapse(Guid factionId)
        {
            if (_collapsedFactions.Contains(factionId))
                _collapsedFactions.Remove(factionId);
            else
                _collapsedFactions.Add(factionId);

            RenderCards();
        }

        private void OnFactionDragHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_editMode || sender is not TextBlock dragHandle)
                return;

            if (dragHandle.Parent is not StackPanel titleRow)
                return;

            if (titleRow.Parent is not StackPanel factionStack)
                return;

            _draggingFaction = factionStack;
            _hasDraggedFactionPastThreshold = false;
            _factionDragStartPointerPos = e.GetPosition(SectionsPanel);

            e.Pointer.Capture(dragHandle);
            e.Handled = true;
        }

        private void OnFactionDragHandleMoved(object? sender, PointerEventArgs e)
        {
            if (_draggingFaction == null || sender is not TextBlock dragHandle)
                return;

            var pos = e.GetPosition(SectionsPanel);
            var delta = pos - _factionDragStartPointerPos;

            var distance = Math.Sqrt(
                delta.X * delta.X +
                delta.Y * delta.Y);

            if (!_hasDraggedFactionPastThreshold && distance < 6)
                return;

            _hasDraggedFactionPastThreshold = true;

            _draggingFaction.ZIndex = 100;
            _draggingFaction.Opacity = 0.75;
            _draggingFaction.RenderTransform = new TranslateTransform(0, delta.Y);

            e.Handled = true;
        }

        private async void OnFactionDragHandleReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_draggingFaction == null || sender is not TextBlock dragHandle)
                return;

            var factionStack = _draggingFaction;

            e.Pointer.Capture(null);

            factionStack.RenderTransform = null;
            factionStack.Opacity = 1;
            factionStack.ZIndex = 0;

            if (!_hasDraggedFactionPastThreshold)
            {
                _draggingFaction = null;
                e.Handled = true;
                return;
            }

            if (factionStack.Tag is not Guid draggedFactionId)
            {
                _draggingFaction = null;
                e.Handled = true;
                return;
            }

            var dropPos = e.GetPosition(SectionsPanel);
            StackPanel? targetFaction = null;

            foreach (var section in SectionsPanel.Children.OfType<StackPanel>())
            {
                if (section == factionStack)
                    continue;

                if (section.Tag is not Guid)
                    continue;

                var topLeft = section.TranslatePoint(
                    new Point(0, 0),
                    SectionsPanel) ?? default;

                var rect = new Rect(
                    topLeft,
                    section.Bounds.Size);

                if (rect.Contains(dropPos))
                {
                    targetFaction = section;
                    break;
                }
            }

            if (targetFaction?.Tag is Guid targetFactionId)
                await SwapFactionSortOrderAsync(draggedFactionId, targetFactionId);

            _draggingFaction = null;
            e.Handled = true;
        }

        private async Task SwapFactionSortOrderAsync(Guid aId, Guid bId)
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var a = await db.CharacterFactions.FindAsync(aId);
            var b = await db.CharacterFactions.FindAsync(bId);

            if (a == null || b == null)
                return;

            (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);

            await db.SaveChangesAsync();
            await ReloadAsync();
        }

        private void ApplyBannerTransform()
        {
            BannerImage.Source = _bannerBitmap;

            if (_bannerBitmap == null || _group == null)
                return;

            var frameW = BannerFrame.Bounds.Width > 0
                ? BannerFrame.Bounds.Width
                : 1100;

            var frameH = BannerFrame.Bounds.Height > 0
                ? BannerFrame.Bounds.Height
                : 300;

            var srcW = _bannerBitmap.PixelSize.Width;
            var srcH = _bannerBitmap.PixelSize.Height;

            var minScale = Math.Max(
                frameW / srcW,
                frameH / srcH);

            _bannerScale = _group.BannerScale > 0
                ? Math.Max(_group.BannerScale, minScale)
                : minScale;

            var scaledW = srcW * _bannerScale;
            var scaledH = srcH * _bannerScale;

            BannerImage.Width = scaledW;
            BannerImage.Height = scaledH;

            var maxOffsetX = Math.Max(0, scaledW - frameW);
            var maxOffsetY = Math.Max(0, scaledH - frameH);

            var offsetX = -_group.BannerFocalX * maxOffsetX;
            var offsetY = -_group.BannerFocalY * maxOffsetY;

            Canvas.SetLeft(BannerImage, offsetX);
            Canvas.SetTop(BannerImage, offsetY);

            bool canDrag = _editMode &&
                           (maxOffsetX > 0 || maxOffsetY > 0);

            BannerImage.Cursor = canDrag
                ? new Cursor(StandardCursorType.SizeAll)
                : new Cursor(StandardCursorType.Arrow);

            BannerDragHint.IsVisible = canDrag;

            BannerZoomSlider.IsVisible = _editMode;
            BannerZoomSlider.Minimum = minScale;
            BannerZoomSlider.Maximum = minScale * 3;

            if (Math.Abs(BannerZoomSlider.Value - _bannerScale) > 0.0001)
                BannerZoomSlider.Value = _bannerScale;
        }

        private void OnBannerZoomChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_group == null || _bannerBitmap == null)
                return;

            var frameW = BannerFrame.Bounds.Width > 0
                ? BannerFrame.Bounds.Width
                : 1100;

            var frameH = BannerFrame.Bounds.Height > 0
                ? BannerFrame.Bounds.Height
                : 300;

            var srcW = _bannerBitmap.PixelSize.Width;
            var srcH = _bannerBitmap.PixelSize.Height;

            var oldScaledW = srcW * _bannerScale;
            var oldScaledH = srcH * _bannerScale;

            var oldMaxX = Math.Max(
                0,
                oldScaledW - frameW);

            var oldMaxY = Math.Max(
                0,
                oldScaledH - frameH);

            var imgCenterX =
                (_group.BannerFocalX * oldMaxX + frameW / 2)
                / _bannerScale;

            var imgCenterY =
                (_group.BannerFocalY * oldMaxY + frameH / 2)
                / _bannerScale;

            _bannerScale = e.NewValue;

            var newScaledW = srcW * _bannerScale;
            var newScaledH = srcH * _bannerScale;

            var newMaxX = Math.Max(
                0,
                newScaledW - frameW);

            var newMaxY = Math.Max(
                0,
                newScaledH - frameH);

            _group.BannerFocalX = newMaxX > 0
                ? Math.Clamp(
                    (imgCenterX * _bannerScale - frameW / 2) / newMaxX,
                    0,
                    1)
                : 0.5;

            _group.BannerFocalY = newMaxY > 0
                ? Math.Clamp(
                    (imgCenterY * _bannerScale - frameH / 2) / newMaxY,
                    0,
                    1)
                : 0.5;

            ApplyBannerTransform();
        }

        private async void OnBannerZoomReleased(
            object? sender,
            PointerReleasedEventArgs e)
        {
            if (_group == null)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            await CharacterGroupService.UpdateBannerScaleAsync(
                db,
                _groupId,
                _bannerScale);

            await CharacterGroupService.UpdateBannerFocalAsync(
                db,
                _groupId,
                _group.BannerFocalX,
                _group.BannerFocalY);
        }

        private void OnBannerPointerPressed(
            object? sender,
            PointerPressedEventArgs e)
        {
            if (!_editMode ||
                _bannerBitmap == null ||
                _group == null)
                return;

            _bannerDragging = true;

            _bannerDragStart = e.GetPosition(BannerFrame);
            _bannerDragStartFocalX = _group.BannerFocalX;
            _bannerDragStartFocalY = _group.BannerFocalY;

            e.Pointer.Capture(BannerImage);
        }

        private void OnBannerPointerMoved(
            object? sender,
            PointerEventArgs e)
        {
            if (!_editMode ||
                !_bannerDragging ||
                _bannerBitmap == null ||
                _group == null)
                return;

            var frameW = BannerFrame.Bounds.Width > 0
                ? BannerFrame.Bounds.Width
                : 1100;

            var frameH = BannerHeight;

            var srcW = _bannerBitmap.PixelSize.Width;
            var srcH = _bannerBitmap.PixelSize.Height;

            var scale = Math.Max(
                frameW / srcW,
                frameH / srcH);

            var maxOffsetX = Math.Max(
                0,
                srcW * scale - frameW);

            var maxOffsetY = Math.Max(
                0,
                srcH * scale - frameH);

            var pos = e.GetPosition(BannerFrame);
            var delta = pos - _bannerDragStart;

            var dx = maxOffsetX > 0
                ? -delta.X / maxOffsetX
                : 0;

            var dy = maxOffsetY > 0
                ? -delta.Y / maxOffsetY
                : 0;

            _group.BannerFocalX = Math.Clamp(
                _bannerDragStartFocalX + dx,
                0,
                1);

            _group.BannerFocalY = Math.Clamp(
                _bannerDragStartFocalY + dy,
                0,
                1);

            ApplyBannerTransform();
        }

        private async void OnBannerPointerReleased(
            object? sender,
            PointerReleasedEventArgs e)
        {
            if (!_bannerDragging || _group == null)
                return;

            _bannerDragging = false;

            e.Pointer.Capture(null);

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            await CharacterGroupService.UpdateBannerFocalAsync(
                db,
                _groupId,
                _group.BannerFocalX,
                _group.BannerFocalY);
        }

        private void OnBackClick(
            object? sender,
            PointerPressedEventArgs e)
        {
            AppNavigator.NavigateTo(new CharactersPage());
        }

        private Control BuildEmptyState()
        {
            var muted = Application.Current?.FindResource("TextMuted") as IBrush
                        ?? Brushes.Gray;

            return new Border
            {
                MinHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "₍^. .^₎Ⳋ Miao",
                            FontSize = 22,
                            FontWeight = FontWeight.Bold,
                            Foreground = muted,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "Chưa có nhân vật, hãy nhấn nút \"+ Thêm nhân vật\" để bắt đầu",
                            FontSize = 14,
                            Foreground = muted,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
        }

        private void OnSearchTextChanged(
            object? sender,
            TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text ?? "";
            RenderCards();
        }

        private Border BuildCard(
            Character ch,
            WrapPanel ownerPanel)
        {
            var cover = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = string.IsNullOrEmpty(ch.ImagePath)
                    ? null
                    : new Bitmap(ch.ImagePath)
            };

            var coverClip = new Border
            {
                Height = 150,
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                ClipToBounds = true,
                Child = cover
            };

            var deleteButton = new Button
            {
                Content = "✕",
                Classes = { "cornerDanger" },
                IsVisible = _editMode,
                Tag = ch.Id,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0)
            };

            deleteButton.Click += OnDeleteCharacterClick;

            var coverLayer = new Panel
            {
                Children = { coverClip, deleteButton }
            };

            var editButton = new Button
            {
                Classes = { "iconOutline" },
                IsVisible = _editMode,
                Tag = ch.Id,
                Content = new Path
                {
                    Data = PencilGeometry,
                    Fill = Application.Current?.FindResource("AccentJade") as IBrush
                           ?? Brushes.SeaGreen,
                    Stretch = Stretch.Uniform,
                    Width = 12,
                    Height = 12
                }
            };

            editButton.Click += OnEditCharacterClick;

            var nameText = new TextBlock
            {
                Text = ch.Name,
                Classes = { "bodyText" },
                FontWeight = FontWeight.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameRow = new Panel
            {
                Margin = new Thickness(10, 8)
            };

            nameText.HorizontalAlignment = HorizontalAlignment.Center;

            editButton.HorizontalAlignment = HorizontalAlignment.Right;
            editButton.VerticalAlignment = VerticalAlignment.Center;

            nameRow.Children.Add(nameText);
            nameRow.Children.Add(editButton);

            var card = new Border
            {
                Width = 160,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.White,
                BorderBrush = Application.Current?.FindResource("BorderSoft") as IBrush
                             ?? Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = ch.Id,
                Child = new StackPanel
                {
                    Children =
                    {
                        coverLayer,
                        nameRow
                    }
                }
            };

            card.PointerPressed += (s, e) =>
                OnCardPointerPressed(s, e, ownerPanel);

            card.PointerMoved += (s, e) =>
                OnCardPointerMoved(s, e, ownerPanel);

            card.PointerReleased += (s, e) =>
                OnCardPointerReleased(s, e, ownerPanel);

            return card;
        }

        private void OnToggleEditModeClick(
            object? sender,
            RoutedEventArgs e)
        {
            _editMode = !_editMode;

            EditModeButton.Content = _editMode
                ? "Xong"
                : "Sửa";

            ApplyBannerTransform();
            RenderCards();
        }

        private void OnAddCharacterClick(
            object? sender,
            RoutedEventArgs e)
        {
            ModalService.Show(
                new CharacterEditModal(
                    _groupId,
                    null,
                    async () => await ReloadAsync()));
        }

        private void OnEditCharacterClick(
            object? sender,
            RoutedEventArgs e)
        {
            if (sender is not Button
                {
                    Tag: Guid characterId
                })
                return;

            e.Handled = true;

            var ch = _characters.First(
                c => c.Id == characterId);

            ModalService.Show(
                new CharacterEditModal(
                    _groupId,
                    ch.Id,
                    async () => await ReloadAsync(),
                    existingName: ch.Name,
                    existingImage: ch.ImagePath,
                    existingDescription: ch.Description,
                    existingFactionId: ch.FactionId));
        }

        private async void OnDeleteCharacterClick(
            object? sender,
            RoutedEventArgs e)
        {
            if (sender is not Button
                {
                    Tag: Guid characterId
                })
                return;

            e.Handled = true;

            var ch = _characters.First(
                c => c.Id == characterId);

            var result = await DialogService.ShowYesNoAsync(
                $"Xóa nhân vật \"{ch.Name}\"?",
                "Xóa nhân vật");

            if (result != DialogResult.Yes)
                return;

            using var db = new MiaoDbContext(
                AppPaths.DbFilePath);

            await CharacterService.DeleteCharacterAsync(
                db,
                characterId);

            await ReloadAsync();
        }

        private void OnCardClick(Guid characterId)
        {
            if (_editMode)
                return;

            AppNavigator.NavigateTo(
                new CharacterDetailPage(
                    _groupId,
                    characterId));
        }

        private void OnCardPointerPressed(
            object? sender,
            PointerPressedEventArgs e,
            WrapPanel panel)
        {
            if (sender is not Border card)
                return;

            _draggingCard = card;
            _hasDraggedPastThreshold = false;
            _dragStartPointerPos = e.GetPosition(panel);

            e.Pointer.Capture(card);
        }

        private void OnCardPointerMoved(
            object? sender,
            PointerEventArgs e,
            WrapPanel panel)
        {
            if (_draggingCard != sender ||
                sender is not Border card)
                return;

            var pos = e.GetPosition(panel);
            var delta = pos - _dragStartPointerPos;

            var distance = Math.Sqrt(
                delta.X * delta.X +
                delta.Y * delta.Y);

            if (!_hasDraggedPastThreshold &&
                distance < 6)
                return;

            _hasDraggedPastThreshold = true;

            card.ZIndex = 100;
            card.Opacity = 0.85;
            card.RenderTransform =
                new TranslateTransform(
                    delta.X,
                    delta.Y);
        }

        private async void OnCardPointerReleased(
            object? sender,
            PointerReleasedEventArgs e,
            WrapPanel panel)
        {
            if (_draggingCard != sender ||
                sender is not Border card)
                return;

            e.Pointer.Capture(null);

            card.RenderTransform = null;
            card.Opacity = 1;
            card.ZIndex = 0;

            if (!_hasDraggedPastThreshold)
            {
                if (card.Tag is Guid clickedId)
                    OnCardClick(clickedId);

                _draggingCard = null;
                return;
            }

            var dropPos = e.GetPosition(panel);
            Border? target = null;

            foreach (var child in panel.Children.OfType<Border>())
            {
                if (child == card)
                    continue;

                var topLeft = child.TranslatePoint(
                    new Point(0, 0),
                    panel) ?? default;

                var rect = new Rect(
                    topLeft,
                    child.Bounds.Size);

                if (rect.Contains(dropPos))
                {
                    target = child;
                    break;
                }
            }

            if (target != null &&
                card.Tag is Guid draggedId &&
                target.Tag is Guid targetId)
            {
                await SwapSortOrderAsync(
                    draggedId,
                    targetId);
            }

            _draggingCard = null;
        }

        private async Task SwapSortOrderAsync(
            Guid aId,
            Guid bId)
        {
            using var db = new MiaoDbContext(
                AppPaths.DbFilePath);

            var a = await db.Characters.FindAsync(aId);
            var b = await db.Characters.FindAsync(bId);

            if (a == null || b == null)
                return;

            (a.SortOrder, b.SortOrder) =
                (b.SortOrder, a.SortOrder);

            await db.SaveChangesAsync();
            await ReloadAsync();
        }
    }
}