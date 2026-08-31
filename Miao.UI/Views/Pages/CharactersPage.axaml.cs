using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class CharactersPage : UserControl
    {
        private bool _editMode;
        private List<CharacterGroup> _groups = new();

        private Border? _draggingCard;
        private Point _dragStartPointerPos;
        private bool _hasDraggedPastThreshold;

        public CharactersPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            _groups = await CharacterGroupService.GetAllAsync(db);
            RenderCards();
        }

        private string _searchText = "";

        private void RenderCards()
        {
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? _groups
                : _groups.Where(g => g.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            GroupsPanel.Children.Clear();
            EmptyHint.IsVisible = _groups.Count == 0;
            if (_groups.Count > 0 && filtered.Count == 0)
            {
                GroupsPanel.Children.Add(new TextBlock
                {
                    Text = "Không tìm thấy dàn nhân vật nào khớp.", Classes = { "bodyText" },
                    Foreground = Application.Current?.FindResource("TextMuted") as IBrush ?? Brushes.Gray
                });
                return;
            }
            foreach (var group in filtered.OrderBy(g => g.SortOrder))
                GroupsPanel.Children.Add(BuildCard(group));
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text ?? "";
            RenderCards();
        }
        
        private static readonly Geometry PencilGeometry = Geometry.Parse(
            "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34a.9959.9959 0 00-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z");

        private Border BuildCard(CharacterGroup group)
        {
            var cover = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = string.IsNullOrEmpty(group.CoverImagePath) ? null : new Bitmap(group.CoverImagePath)
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
                Content = "✕", Classes = { "cornerDanger" }, IsVisible = _editMode, Tag = group.Id,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0)
            };
            deleteButton.Click += OnDeleteGroupClick;

            var coverLayer = new Panel { Children = { coverClip, deleteButton } };

            var editButton = new Button
            {
                Classes = { "iconOutline" },
                IsVisible = _editMode,
                Tag = group.Id,
                Content = new Path
                {
                    Data = PencilGeometry,
                    Fill = Application.Current?.FindResource("AccentJade") as IBrush ?? Brushes.SeaGreen,
                    Stretch = Stretch.Uniform,
                    Width = 12,
                    Height = 12
                }
            };
            editButton.Click += OnEditGroupClick;

            var nameText = new TextBlock
            {
                Text = group.Name,
                Classes = { "bodyText" },
                FontWeight = FontWeight.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,          
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameRow = new Panel { Margin = new Thickness(10, 8) };
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
                BorderBrush = Application.Current?.FindResource("BorderSoft") as IBrush ?? Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = group.Id,
                Child = new StackPanel { Children = { coverLayer, nameRow } }
            };

            card.PointerPressed += OnCardPointerPressed;
            card.PointerMoved += OnCardPointerMoved;
            card.PointerReleased += OnCardPointerReleased;
            return card;
        }

        private void OnToggleEditModeClick(object? sender, RoutedEventArgs e)
        {
            _editMode = !_editMode;
            EditModeButton.Content = _editMode ? "Xong" : "Sửa";
            RenderCards();
        }

        private void OnEditGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: Guid groupId }) return;
            e.Handled = true;
            var group = _groups.First(g => g.Id == groupId);
            ModalService.Show(new CharacterGroupEditModal(group.Id, async () => await ReloadAsync(),
                existingName: group.Name, existingCover: group.CoverImagePath));
        }

        private async void OnDeleteGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: Guid groupId }) return;
            e.Handled = true;
            var group = _groups.First(g => g.Id == groupId);

            var result = await DialogService.ShowYesNoAsync(
                $"Xóa dàn \"{group.Name}\" sẽ xóa toàn bộ nhân vật và ảnh bên trong. Tiếp tục?", "Xóa dàn nhân vật");
            if (result != DialogResult.Yes) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            await CharacterGroupService.DeleteAsync(db, groupId);
            await ReloadAsync();
        }

        private void OnAddGroupClick(object? sender, RoutedEventArgs e)
            => ModalService.Show(new CharacterGroupEditModal(null, async () => await ReloadAsync()));

        private void OnCardClick(Guid groupId)
        {
            if (_editMode) return;
            AppNavigator.NavigateTo(new CharacterGroupPage(groupId));
        }

        private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border card) return;
            _draggingCard = card;
            _hasDraggedPastThreshold = false;
            _dragStartPointerPos = e.GetPosition(GroupsPanel);
            e.Pointer.Capture(card);
        }

        private void OnCardPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_draggingCard != sender || sender is not Border card) return;
            var pos = e.GetPosition(GroupsPanel);
            var delta = pos - _dragStartPointerPos;

            var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            if (!_hasDraggedPastThreshold && distance < 6) return;
            _hasDraggedPastThreshold = true;

            card.ZIndex = 100;
            card.Opacity = 0.85;
            card.RenderTransform = new TranslateTransform(delta.X, delta.Y);
        }

        private async void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_draggingCard != sender || sender is not Border card) return;
            e.Pointer.Capture(null);
            card.RenderTransform = null;
            card.Opacity = 1;
            card.ZIndex = 0;

            if (!_hasDraggedPastThreshold)
            {
                if (card.Tag is Guid clickedId) OnCardClick(clickedId);
                _draggingCard = null;
                return;
            }

            var dropPos = e.GetPosition(GroupsPanel);
            Border? target = null;
            foreach (var child in GroupsPanel.Children.OfType<Border>())
            {
                if (child == card) continue;
                var topLeft = child.TranslatePoint(new Point(0, 0), GroupsPanel) ?? default;
                if (new Rect(topLeft, child.Bounds.Size).Contains(dropPos)) { target = child; break; }
            }

            if (target != null && card.Tag is Guid draggedId && target.Tag is Guid targetId)
                await SwapSortOrderAsync(draggedId, targetId);

            _draggingCard = null;
        }

        private async Task SwapSortOrderAsync(Guid aId, Guid bId)
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var a = await db.CharacterGroups.FindAsync(aId);
            var b = await db.CharacterGroups.FindAsync(bId);
            if (a == null || b == null) return;

            (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
            await db.SaveChangesAsync();
            await ReloadAsync();
        }
    }
}