using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class WorkspacePage : UserControl
    {
        private const int ChaptersPerPage = 100;

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

        // ================= NẠP DỮ LIỆU =================

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
                groups.Add(new VolumeGroup(ChaptersPerPage)
                {
                    VolumeId = vol.Id,
                    Name = vol.Name,
                    AllChapters = _allChapters
                        .Where(c => c.VolumeId == vol.Id)
                        .OrderBy(c => c.Number)
                        .Select(ToRow)
                        .ToList()
                });
            }

            groups.Add(new VolumeGroup(ChaptersPerPage)
            {
                VolumeId = null,
                Name = "Chưa phân quyển",
                AllChapters = _allChapters
                    .Where(c => c.VolumeId == null)
                    .OrderBy(c => c.Number)
                    .Select(ToRow)
                    .ToList()
            });

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

            UpdateBulkDeleteButton();
        }

        private void UpdateBulkDeleteButton()
        {
            var count = _selectedChapterIds.Count;
            BulkDeleteButton.Content = $"🗑 Xóa đã chọn ({count})";
            BulkDeleteButton.IsVisible = count > 0;
        }

        private void OnBulkDeleteChaptersClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedChapterIds.Count == 0) return;

            var count = _selectedChapterIds.Count;
            ShowConfirm($"Xóa {count} chương đã chọn? Không thể hoàn tác.", () =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);

                var chaptersToDelete = db.Chapters.Where(c => _selectedChapterIds.Contains(c.Id)).ToList();
                var affectedVolumeIds = chaptersToDelete.Select(c => c.VolumeId).Distinct().ToList();

                db.Chapters.RemoveRange(chaptersToDelete);
                db.SaveChanges();

                foreach (var volumeId in affectedVolumeIds)
                    RenumberScope(db, volumeId);

                _selectedChapterIds.Clear();
                LoadWorkspace();
            });
        }

        // ================= LỚP DỮ LIỆU DÙNG RIÊNG CHO TRANG =================

        private static readonly IBrush MarkerHasContentBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
        private static readonly IBrush MarkerEmptyBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

        public class ChapterRow
        {
            public Guid Id { get; set; }
            public int Number { get; set; }
            public string Title { get; set; } = "";
            public string StatusText { get; set; } = "";
            public bool HasContent { get; set; }
            public int GlobalIndex { get; set; }
            public bool ShowGlobalIndex { get; set; }
            public bool IsSelected { get; set; }

            public IBrush MarkerBrush => HasContent ? MarkerHasContentBrush : MarkerEmptyBrush;
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

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class PickItem
        {
            public Guid ChapterId { get; set; }
            public string Label { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        // ================= ĐIỀU HƯỚNG =================

        private void OnBackToNovelClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new NovelDetailPage(_novelId));

        private void OnChapterClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is ChapterRow row)
                AppNavigator.NavigateTo(new ReaderPage(_novelId, row.Number, startInEditMode: true));
        }

        // ================= PHÂN TRANG THEO TỪNG QUYỂN =================

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

        // ================= XÓA CHƯƠNG =================

        private void OnDeleteChapterClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not ChapterRow row) return;

            ShowConfirm($"Xóa chương {row.Number}: {row.Title}?", () =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var chapter = db.Chapters.Find(row.Id);
                if (chapter == null) return;

                var volumeId = chapter.VolumeId;

                db.Chapters.Remove(chapter);
                db.SaveChanges();

                _selectedChapterIds.Remove(row.Id);
                RenumberScope(db, volumeId);

                LoadWorkspace();
            });
        }

        private void RenumberScope(MiaoDbContext db, Guid? volumeId)
        {
            var remaining = db.Chapters
                .Where(c => c.NovelId == _novelId && c.VolumeId == volumeId)
                .OrderBy(c => c.Number)
                .ToList();

            if (remaining.Count == 0) return;

            var renumberMap = new Dictionary<int, int>();
            for (int i = 0; i < remaining.Count; i++)
            {
                var newNumber = i + 1;
                if (remaining[i].Number != newNumber)
                {
                    renumberMap[remaining[i].Number] = newNumber;
                    remaining[i].Number = newNumber;
                }
            }

            if (renumberMap.Count == 0) return;

            var novel = db.Novels.Find(_novelId);
            if (volumeId == null && novel != null && novel.LastReadChapterNumber > 0 &&
                renumberMap.TryGetValue(novel.LastReadChapterNumber, out var mappedNumber))
            {
                novel.LastReadChapterNumber = mappedNumber;
            }

            db.SaveChanges();
        }

        // ================= TẠO QUYỂN =================

        private void OnCreateVolumeClick(object? sender, RoutedEventArgs e)
        {
            NewVolumeNameBox.Text = "";
            RangeFromBox.Text = "";
            RangeToBox.Text = "";
            RangeModeRadio.IsChecked = true;
            RangePanel.IsVisible = true;
            ManualPanel.IsVisible = false;
            CreateVolumeErrorText.IsVisible = false;

            var unassigned = _allChapters
                .Where(c => c.VolumeId == null)
                .OrderBy(c => c.Number)
                .ToList();

            ManualPickList.ItemsSource = unassigned
                .Select(c => new PickItem { ChapterId = c.Id, Label = $"Chương {c.Number}: {c.DisplayTitle}" })
                .ToList();

            if (unassigned.Count == 0)
                ShowError(CreateVolumeErrorText, "Không còn chương nào chưa được phân quyển.");

            ShowModal(CreateVolumeCard);
        }

        private void OnAssignModeChanged(object? sender, RoutedEventArgs e)
        {
            if (RangePanel == null || ManualPanel == null) return;

            bool isRange = RangeModeRadio.IsChecked == true;
            RangePanel.IsVisible = isRange;
            ManualPanel.IsVisible = !isRange;
        }

        private void OnCreateVolumeSaveClick(object? sender, RoutedEventArgs e)
        {
            var name = NewVolumeNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(CreateVolumeErrorText, "Vui lòng nhập tên quyển.");
                return;
            }

            List<Guid> chapterIdsToAssign;

            if (RangeModeRadio.IsChecked == true)
            {
                if (!int.TryParse(RangeFromBox.Text?.Trim(), out var from) ||
                    !int.TryParse(RangeToBox.Text?.Trim(), out var to) ||
                    from > to)
                {
                    ShowError(CreateVolumeErrorText, "Phạm vi chương không hợp lệ.");
                    return;
                }

                chapterIdsToAssign = _allChapters
                    .Where(c => c.VolumeId == null && c.Number >= from && c.Number <= to)
                    .Select(c => c.Id)
                    .ToList();
            }
            else
            {
                chapterIdsToAssign = (ManualPickList.ItemsSource as IEnumerable<PickItem>)?
                    .Where(p => p.IsSelected)
                    .Select(p => p.ChapterId)
                    .ToList() ?? new List<Guid>();
            }

            if (chapterIdsToAssign.Count == 0)
            {
                ShowError(CreateVolumeErrorText, "Chưa chọn chương nào cho quyển này.");
                return;
            }

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var maxOrder = db.Volumes
                .Where(v => v.NovelId == _novelId)
                .Select(v => (int?)v.SortOrder)
                .Max() ?? 0;

            var volume = new Volume
            {
                NovelId = _novelId,
                Name = name,
                SortOrder = maxOrder + 1
            };
            db.Volumes.Add(volume);
            db.SaveChanges();

            var chapters = db.Chapters.Where(c => chapterIdsToAssign.Contains(c.Id)).ToList();
            foreach (var c in chapters)
                c.VolumeId = volume.Id;
            db.SaveChanges();

            ModalService.Close();
            LoadWorkspace();
        }

        private void OnCreateVolumeCancelClick(object? sender, RoutedEventArgs e)
            => ModalService.Close();

        // ================= SỬA / XÓA QUYỂN =================

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
                    LoadWorkspace();
                });
        }

        private void OnEditVolumeCancelClick(object? sender, RoutedEventArgs e)
            => ModalService.Close();

        // ================= POPUP XÁC NHẬN =================

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

        // ================= HELPERS =================

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

        // ================= SỬA TÊN CHƯƠNG =================

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