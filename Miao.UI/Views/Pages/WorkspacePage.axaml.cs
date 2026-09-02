using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class WorkspacePage : UserControl
    {
        private const int ChaptersPerPage = 100;
        private static readonly DataFormat<ChapterRow> ChapterDragFormat =
            DataFormat.CreateInProcessFormat<ChapterRow>("Miao.ChapterRow");

        private readonly Guid _novelId;

        private List<Chapter> _allChapters = new();
        private Volume? _editingVolume;
        private Action? _pendingConfirmAction;
        private readonly HashSet<Guid> _selectedChapterIds = new();

        public WorkspacePage(Guid novelId)
        {
            InitializeComponent();
            _novelId = novelId;
            LoadWorkspace();
        }

        private void LoadWorkspace()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var novel = db.Novels.Find(_novelId);
            TitleText.Text = novel?.DisplayTitle ?? "";

            _allChapters = db.Chapters
                .Where(c => c.NovelId == _novelId)
                .OrderBy(c => c.Number)
                .ToList();

            var volumes = db.Volumes
                .Where(v => v.NovelId == _novelId)
                .OrderBy(v => v.SortOrder)
                .ToList();

            var orderedForIndex = new List<Chapter>();
            foreach (var vol in volumes)
                orderedForIndex.AddRange(_allChapters.Where(c => c.VolumeId == vol.Id).OrderBy(c => c.Number));
            orderedForIndex.AddRange(_allChapters.Where(c => c.VolumeId == null).OrderBy(c => c.Number));

            var globalIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < orderedForIndex.Count; i++)
                globalIndex[orderedForIndex[i].Id] = i + 1;

            bool showGlobalIndex = volumes.Count > 0;

            ChapterRow ToRow(Chapter c) => new()
            {
                Id = c.Id,
                Number = c.Number,
                Title = c.DisplayTitle,
                HasContent = !string.IsNullOrWhiteSpace(c.DisplayContent),
                StatusText = string.IsNullOrWhiteSpace(c.DisplayContent)
                    ? "Chưa có nội dung"
                    : $"{c.DisplayContent.Length:N0} ký tự",
                GlobalIndex = globalIndex.TryGetValue(c.Id, out var idx) ? idx : c.Number,
                ShowGlobalIndex = showGlobalIndex,
                IsSelected = _selectedChapterIds.Contains(c.Id)
            };

            var groups = new List<VolumeGroup>();

            foreach (var vol in volumes)
            {
                var group = new VolumeGroup(ChaptersPerPage)
                {
                    VolumeId = vol.Id,
                    Name = vol.Name,
                    AllChapters = _allChapters
                        .Where(c => c.VolumeId == vol.Id)
                        .OrderBy(c => c.Number)
                        .Select(ToRow)
                        .ToList()
                };
                foreach (var row in group.AllChapters) row.ParentGroup = group;
                groups.Add(group);
            }

            var unassignedGroup = new VolumeGroup(ChaptersPerPage)
            {
                VolumeId = null,
                Name = "Chưa phân quyển",
                AllChapters = _allChapters
                    .Where(c => c.VolumeId == null)
                    .OrderBy(c => c.Number)
                    .Select(ToRow)
                    .ToList()
            };
            foreach (var row in unassignedGroup.AllChapters) row.ParentGroup = unassignedGroup;
            groups.Add(unassignedGroup);

            VolumesList.ItemsSource = groups;
            UpdateBulkDeleteButton();
        }

        private void OnChapterCheckToggled(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not ChapterRow row) return;

            row.IsSelected = cb.IsChecked == true;
            if (row.IsSelected)
                _selectedChapterIds.Add(row.Id);
            else
                _selectedChapterIds.Remove(row.Id);

            row.ParentGroup?.NotifySelectionChanged();

            UpdateBulkDeleteButton();
        }

        private void OnGroupSelectAllClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not VolumeGroup group) return;

            bool selectAll = cb.IsChecked == true;

            foreach (var row in group.AllChapters)
            {
                row.IsSelected = selectAll;
                if (selectAll) _selectedChapterIds.Add(row.Id);
                else _selectedChapterIds.Remove(row.Id);
            }

            UpdateBulkDeleteButton();

            group.NotifySelectionChanged();
        }

        private void UpdateBulkDeleteButton()
        {
            var count = _selectedChapterIds.Count;
            BulkDeleteButton.Content = $"✕ Xóa đã chọn ({count})";
            BulkDeleteButton.IsVisible = count > 0;
            AddToVolumeButton.Content = $"→ Thêm vào quyển ({count})";
            AddToVolumeButton.IsVisible = count > 0;
        }

        private void OnApplyRangeClick(object? sender, RoutedEventArgs e)
        {
            if (!int.TryParse(RangeFromBox.Text?.Trim(), out var from)) from = int.MinValue;
            if (!int.TryParse(RangeToBox.Text?.Trim(), out var to)) to = int.MaxValue;

            if (VolumesList.ItemsSource is not IEnumerable<VolumeGroup> groups) return;

            foreach (var group in groups)
            {
                foreach (var row in group.AllChapters)
                {
                    row.IsSelected = row.Number >= from && row.Number <= to;
                    if (row.IsSelected) _selectedChapterIds.Add(row.Id);
                    else _selectedChapterIds.Remove(row.Id);
                }
                group.NotifySelectionChanged();
            }

            UpdateBulkDeleteButton();
        }

        private void OnSelectAllChaptersClick(object? sender, RoutedEventArgs e)
        {
            if (VolumesList.ItemsSource is not IEnumerable<VolumeGroup> groups) return;

            foreach (var group in groups)
            {
                foreach (var row in group.AllChapters)
                {
                    row.IsSelected = true;
                    _selectedChapterIds.Add(row.Id);
                }
                group.NotifySelectionChanged();
            }

            UpdateBulkDeleteButton();
        }

        private void OnDeselectAllChaptersClick(object? sender, RoutedEventArgs e)
        {
            if (VolumesList.ItemsSource is not IEnumerable<VolumeGroup> groups) return;

            foreach (var group in groups)
            {
                foreach (var row in group.AllChapters)
                    row.IsSelected = false;
                group.NotifySelectionChanged();
            }

            _selectedChapterIds.Clear();
            UpdateBulkDeleteButton();
        }

        private void OnBulkDeleteChaptersClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedChapterIds.Count == 0) return;

            var count = _selectedChapterIds.Count;
            ShowConfirm($"Xóa {count} chương đã chọn? Không thể hoàn tác.", () =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);

                var chaptersToDelete = db.Chapters.Where(c => _selectedChapterIds.Contains(c.Id)).ToList();

                db.Chapters.RemoveRange(chaptersToDelete);
                db.SaveChanges();

                RenumberAllScopes(db);

                _selectedChapterIds.Clear();
                LoadWorkspace();
            });
        }

        private static readonly IBrush MarkerHasContentBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
        private static readonly IBrush MarkerEmptyBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

        public class ChapterRow : INotifyPropertyChanged
        {
            public Guid Id { get; set; }
            public int Number { get; set; }
            public string Title { get; set; } = "";
            public string StatusText { get; set; } = "";
            public bool HasContent { get; set; }
            public int GlobalIndex { get; set; }
            public bool ShowGlobalIndex { get; set; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
            }

            public VolumeGroup? ParentGroup { get; set; }

            public IBrush MarkerBrush => HasContent ? MarkerHasContentBrush : MarkerEmptyBrush;

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public class PagerItem
        {
            public VolumeGroup Group { get; set; } = null!;
            public int Page { get; set; }
            public bool IsEllipsis { get; set; }
            public bool IsCurrent { get; set; }
            public bool IsPage => !IsEllipsis;
            public string Label => IsEllipsis ? "…" : Page.ToString();
        }

        public class VolumeGroup : INotifyPropertyChanged
        {
            private readonly int _pageSize;
            private int _currentPage = 1;

            public VolumeGroup(int pageSize) => _pageSize = pageSize;

            public Guid? VolumeId { get; set; }
            public string Name { get; set; } = "";
            public List<ChapterRow> AllChapters { get; set; } = new();

            public bool CanManage => VolumeId.HasValue;
            public bool IsAllSelected => AllChapters.Count > 0 && AllChapters.All(c => c.IsSelected);
            public string HeaderText => $"{Name} ({AllChapters.Count} chương)";

            public int CurrentPage => _currentPage;
            public int TotalPages => Math.Max(1, (int)Math.Ceiling(AllChapters.Count / (double)_pageSize));
            public bool ShowPager => TotalPages > 1;

            public List<ChapterRow> PagedChapters =>
                AllChapters.Skip((CurrentPage - 1) * _pageSize).Take(_pageSize).ToList();

            public List<PagerItem> PagerItems => BuildPagerItems();

            private List<PagerItem> BuildPagerItems()
            {
                var items = new List<PagerItem>();
                int total = TotalPages;
                int current = CurrentPage;

                void AddPage(int page) => items.Add(new PagerItem { Group = this, Page = page, IsCurrent = page == current });
                void AddEllipsis() => items.Add(new PagerItem { Group = this, IsEllipsis = true });

                if (total <= 7)
                {
                    for (int page = 1; page <= total; page++)
                        AddPage(page);

                    return items;
                }

                AddPage(1);
                if (current > 3)
                    AddEllipsis();

                int start = Math.Max(2, current - 1);
                int end = Math.Min(total - 1, current + 1);
                for (int page = start; page <= end; page++)
                    AddPage(page);

                if (current < total - 2)
                    AddEllipsis();
                AddPage(total);

                return items;
            }

            public void GoToPage(int page)
            {
                page = Math.Max(1, Math.Min(page, TotalPages));
                if (page == _currentPage) return;

                _currentPage = page;
                OnPropertyChanged(nameof(PagedChapters));
                OnPropertyChanged(nameof(PagerItems));
            }

            public void NotifySelectionChanged() => OnPropertyChanged(nameof(IsAllSelected));

            public void RefreshChapterRows()
            {
                OnPropertyChanged(nameof(PagedChapters));
                OnPropertyChanged(nameof(IsAllSelected));
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void OnBackToNovelClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new NovelDetailPage(_novelId));

        private void OnGroupPrevPageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is VolumeGroup group)
                group.GoToPage(group.CurrentPage - 1);
        }

        private void OnGroupNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is VolumeGroup group)
                group.GoToPage(group.CurrentPage + 1);
        }

        private void OnGroupPageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is PagerItem item && item.IsPage)
                item.Group.GoToPage(item.Page);
        }

        private async void OnChapterDragHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not ChapterRow row) return;

            var item = new DataTransferItem();
            item.Set(ChapterDragFormat, row);

            var data = new DataTransfer();
            data.Add(item);

            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }

        private void OnChapterRowDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(ChapterDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnChapterRowDrop(object? sender, DragEventArgs e)
        {
            if (sender is not Control targetControl || targetControl.Tag is not ChapterRow targetRow) return;

            var sourceRow = e.DataTransfer.TryGetValue(ChapterDragFormat);
            if (sourceRow == null || sourceRow.Id == targetRow.Id) return;

            ReorderChapters(sourceRow, targetRow);
        }

        private void ReorderChapters(ChapterRow sourceRow, ChapterRow targetRow)
        {
            var group = sourceRow.ParentGroup;
            if (group == null || !ReferenceEquals(group, targetRow.ParentGroup))
                return;

            var list = group.AllChapters;
            var sourceIndex = list.FindIndex(c => c.Id == sourceRow.Id);
            var targetIndex = list.FindIndex(c => c.Id == targetRow.Id);
            if (sourceIndex < 0 || targetIndex < 0)
                return;

            list.RemoveAt(sourceIndex);
            list.Insert(targetIndex, sourceRow);

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            for (int i = 0; i < list.Count; i++)
            {
                var chapter = db.Chapters.Find(list[i].Id);
                if (chapter != null)
                    chapter.Number = i + 1;
            }
            db.SaveChanges();

            RenumberAllScopes(db);

            LoadWorkspace();
        }

        private void OnDeleteChapterClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not ChapterRow row) return;

            ShowConfirm($"Xóa chương {row.Number}: {row.Title}?", () =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var chapter = db.Chapters.Find(row.Id);
                if (chapter == null) return;

                db.Chapters.Remove(chapter);
                db.SaveChanges();

                _selectedChapterIds.Remove(row.Id);
                RenumberAllScopes(db);

                LoadWorkspace();
            });
        }

        private void RenumberAllScopes(MiaoDbContext db)
        {
            var volumes = db.Volumes.Where(v => v.NovelId == _novelId).OrderBy(v => v.SortOrder).ToList();
            var allChapters = db.Chapters.Where(c => c.NovelId == _novelId).ToList();

            var renumberMap = new Dictionary<int, int>();
            var running = 1;

            void AssignSequential(IEnumerable<Chapter> group)
            {
                foreach (var chapter in group.OrderBy(c => c.Number))
                {
                    if (chapter.Number != running)
                        renumberMap[chapter.Number] = running;
                    chapter.Number = running;
                    running++;
                }
            }

            foreach (var vol in volumes)
                AssignSequential(allChapters.Where(c => c.VolumeId == vol.Id));

            AssignSequential(allChapters.Where(c => c.VolumeId == null));

            var novel = db.Novels.Find(_novelId);
            if (novel != null && novel.LastReadChapterNumber > 0 &&
                renumberMap.TryGetValue(novel.LastReadChapterNumber, out var mappedNumber))
            {
                novel.LastReadChapterNumber = mappedNumber;
            }

            db.SaveChanges();
        }

        private void OnCreateVolumeClick(object? sender, RoutedEventArgs e)
        {
            NewVolumeNameBox.Text = "";
            CreateVolumeErrorText.IsVisible = false;
            ShowModal(CreateVolumeCard);
        }

        private void OnCreateVolumeSaveClick(object? sender, RoutedEventArgs e)
        {
            var name = NewVolumeNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(CreateVolumeErrorText, "Vui lòng nhập tên quyển.");
                return;
            }

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var maxOrder = db.Volumes
                .Where(v => v.NovelId == _novelId)
                .Select(v => (int?)v.SortOrder)
                .Max() ?? 0;

            db.Volumes.Add(new Volume { NovelId = _novelId, Name = name, SortOrder = maxOrder + 1 });
            db.SaveChanges();

            ModalService.Close();
            LoadWorkspace();
        }

        private void OnCreateVolumeCancelClick(object? sender, RoutedEventArgs e)
        {
            ModalService.Close();
        }

        private void OnRenumberAllClick(object? sender, RoutedEventArgs e)
        {
            ShowConfirm(
                "Đánh lại số thứ tự TẤT CẢ chương trong truyện này theo đúng thứ tự quyển hiện tại " +
                "(quyển sau nối tiếp số cuối quyển trước)? Dùng để sửa ngay các trường hợp số chương " +
                "bị trùng/lộn xộn do dữ liệu cũ trước khi có bản sửa lỗi này.",
                () =>
                {
                    using var db = new MiaoDbContext(AppPaths.DbFilePath);
                    RenumberAllScopes(db);
                    LoadWorkspace();
                });
        }

        private Popup? _addToVolumePopup;

        private void OnAddToVolumeClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button addButton) return;
            if (_addToVolumePopup != null) _addToVolumePopup.IsOpen = false;

            var panel = new StackPanel();

            using (var db = new MiaoDbContext(AppPaths.DbFilePath))
            {
                var volumes = db.Volumes.Where(v => v.NovelId == _novelId).OrderBy(v => v.SortOrder).ToList();

                if (volumes.Count == 0)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "Chưa có quyển nào. Bấm \"Tạo quyển\" trước.",
                        Foreground = (IBrush)(Application.Current?.FindResource("TextMuted") ?? Brushes.Gray),
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 220, Margin = new Thickness(10)
                    });
                }

                foreach (var vol in volumes)
                {
                    var volumeId = vol.Id;
                    var btn = new Button
                    {
                        Content = new TextBlock { Text = vol.Name, TextWrapping = TextWrapping.Wrap, MaxWidth = 180 }
                    };
                    ApplyMenuItemHoverStyle(btn);
                    btn.Click += (_, _) =>
                    {
                        AssignSelectedChaptersToVolume(volumeId);
                        if (_addToVolumePopup != null) _addToVolumePopup.IsOpen = false;
                    };
                    panel.Children.Add(btn);
                }

                panel.Children.Add(new Separator { Margin = new Thickness(4, 3, 4, 3) });

                var unassignBtn = new Button
                {
                    Content = new TextBlock { Text = "Chưa phân quyển", TextWrapping = TextWrapping.Wrap, MaxWidth = 180 }
                };
                ApplyMenuItemHoverStyle(unassignBtn);
                unassignBtn.Click += (_, _) =>
                {
                    AssignSelectedChaptersToVolume(null);
                    if (_addToVolumePopup != null) _addToVolumePopup.IsOpen = false;
                };
                panel.Children.Add(unassignBtn);
            }

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (IBrush)(Application.Current?.FindResource("BorderSoft") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Width = 220,
                Child = panel
            };

            _addToVolumePopup = new Popup
            {
                PlacementTarget = addButton,
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true,
                Child = card,
                IsOpen = true
            };

            ((ISetLogicalParent)_addToVolumePopup).SetParent(this);
        }

        private static void ApplyMenuItemHoverStyle(Button button)
        {
            var hoverBrush = (IBrush)(Application.Current?.FindResource("AccentJadeSoft") ?? Brushes.WhiteSmoke);

            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 10);
            button.Margin = new Thickness(0, 1);
            button.CornerRadius = new CornerRadius(6);
            button.Cursor = new Cursor(StandardCursorType.Hand);

            button.PointerEntered += (_, _) => button.Background = hoverBrush;
            button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        }

        private void AssignSelectedChaptersToVolume(Guid? volumeId)
        {
            if (_selectedChapterIds.Count == 0) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var chapters = db.Chapters.Where(c => _selectedChapterIds.Contains(c.Id)).ToList();
            if (chapters.Count == 0) return;

            foreach (var c in chapters)
                c.VolumeId = volumeId;

            db.SaveChanges();

            RenumberAllScopes(db);

            _selectedChapterIds.Clear();
            LoadWorkspace();
        }

        private void OnEditVolumeClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not VolumeGroup group || group.VolumeId == null)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            _editingVolume = db.Volumes.Find(group.VolumeId.Value);
            if (_editingVolume == null) return;

            EditVolumeNameBox.Text = _editingVolume.Name;
            ShowModal(EditVolumeCard);
        }

        private void OnDeleteVolumeClick(object? sender, RoutedEventArgs e)
            => OnEditVolumeClick(sender, e);

        private void OnEditVolumeSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_editingVolume == null) return;

            var name = EditVolumeNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var vol = db.Volumes.Find(_editingVolume.Id);
            if (vol != null)
            {
                vol.Name = name;
                db.SaveChanges();
            }

            ModalService.Close();
            LoadWorkspace();
        }

        private void OnEditVolumeDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (_editingVolume == null) return;

            var volumeId = _editingVolume.Id;
            var volumeName = _editingVolume.Name;

            ShowConfirm(
                $"Xóa quyển \"{volumeName}\"? Các chương trong quyển sẽ chuyển về mục \"Chưa phân quyển\".",
                () =>
                {
                    using var db = new MiaoDbContext(AppPaths.DbFilePath);

                    var chaptersInVolume = db.Chapters.Where(c => c.VolumeId == volumeId).ToList();
                    foreach (var c in chaptersInVolume)
                        c.VolumeId = null;

                                        var vol = db.Volumes.Find(volumeId);
                    if (vol != null)
                        db.Volumes.Remove(vol);

                    db.SaveChanges();

                    RenumberAllScopes(db);

                    LoadWorkspace();
                });
        }

        private void OnEditVolumeCancelClick(object? sender, RoutedEventArgs e)
            => ModalService.Close();

        private void ShowConfirm(string message, Action onConfirmed)
        {
            ConfirmMessageText.Text = message;
            _pendingConfirmAction = onConfirmed;
            ShowModal(ConfirmCard);
        }

        private void OnConfirmYesClick(object? sender, RoutedEventArgs e)
        {
            var action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            ModalService.Close();
            action?.Invoke();
        }

        private void OnConfirmCancelClick(object? sender, RoutedEventArgs e)
        {
            _pendingConfirmAction = null;
            ModalService.Close();
        }

        private void ShowModal(Control card)
        {
            if (card.Parent is Panel panel)
                panel.Children.Remove(card);

            card.IsVisible = true;
            ModalService.Show(card);
        }

        private static void ShowError(TextBlock errorText, string message)
        {
            errorText.Text = message;
            errorText.IsVisible = true;
        }

        private Guid? _editingChapterId;
        private string _editingChapterNewTitle = "";

        private void OnEditChapterClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not ChapterRow row)
                return;

            _editingChapterId = row.Id;
            EditChapterNumberText.Text = $"Chương {row.Number}";
            EditChapterTitleBox.Text = row.Title;
            EditChapterTitleBox.SelectAll();
            EditChapterTitleBox.Focus();

            ShowModal(EditChapterCard);
        }

        private void OnEditChapterSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_editingChapterId == null) return;

            var newTitle = EditChapterTitleBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(newTitle)) return;

            _editingChapterNewTitle = newTitle;
            ShowConfirm("Lưu thay đổi tên chương này?", SaveEditedChapterTitle);
        }

        private void SaveEditedChapterTitle()
        {
            if (_editingChapterId == null) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var chapter = db.Chapters.FirstOrDefault(c => c.Id == _editingChapterId.Value);
            if (chapter == null) return;

            if (string.IsNullOrWhiteSpace(chapter.TranslatedTitle))
                chapter.Title = _editingChapterNewTitle;
            else
                chapter.TranslatedTitle = _editingChapterNewTitle;

            chapter.LastEditedAt = DateTime.Now;
            db.SaveChanges();

            _editingChapterId = null;
            _editingChapterNewTitle = "";
            LoadWorkspace();
        }

        private void OnEditChapterCancelClick(object? sender, RoutedEventArgs e)
        {
            _editingChapterId = null;
            _editingChapterNewTitle = "";
            ModalService.Close();
        }
    }
}