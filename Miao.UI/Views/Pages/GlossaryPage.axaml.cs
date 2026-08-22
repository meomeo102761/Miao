using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class GlossaryPage : UserControl
    {
        // Ngưỡng tối thiểu để tính là "đang kéo" thay vì chỉ nhấp chuột — Avalonia
        // không có SystemParameters.MinimumHorizontalDragDistance như WPF.
        private const double DragThreshold = 5.0;

        private readonly SinoVietnameseConverter _sinoVietnamese;

        private List<GlossarySet> _allSets = new();
        private Dictionary<Guid, string> _novelTitles = new();
        private GlossarySetEntry? _editingEntry;

        private bool _isEditMode;
        private Point _dragStartPoint;
        private GlossarySetRowViewModel? _draggedSet;
        private PointerPressedEventArgs? _dragPressedEvent;

        private ObservableCollection<string> BuildPageItems(int currentPage, int totalPages)
        {
            var items = new ObservableCollection<string>();

            if (totalPages <= 7)
            {
                for (int i = 1; i <= totalPages; i++)
                    items.Add(i.ToString());

                return items;
            }

            items.Add("1");

            if (currentPage > 4)
                items.Add("...");

            int start = Math.Max(2, currentPage - 1);
            int end = Math.Min(totalPages - 1, currentPage + 1);

            if (currentPage <= 4)
            {
                start = 2;
                end = 4;
            }
            else if (currentPage >= totalPages - 3)
            {
                start = totalPages - 3;
                end = totalPages - 1;
            }

            for (int i = start; i <= end; i++)
                items.Add(i.ToString());

            if (currentPage < totalPages - 3)
                items.Add("...");

            items.Add(totalPages.ToString());

            return items;
        }

        public GlossaryPage()
        {
            InitializeComponent();

            var handataPath = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "handata");
            _sinoVietnamese = new SinoVietnameseConverter(handataPath);

            LoadSets();
        }

        public GlossaryPage(Guid preselectNovelId) : this()
        {
            using var db = OpenDb();
            var novel = db.Novels.FirstOrDefault(n => n.Id == preselectNovelId);
            if (novel != null) NovelSearchBox.Text = novel.DisplayTitle;
        }

        private static MiaoDbContext OpenDb() =>
            new(Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "miao.db"));

        private void LoadSets()
        {
            using var db = OpenDb();
            _allSets = db.GlossarySets.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
            _novelTitles = db.Novels.ToDictionary(n => n.Id, n => n.DisplayTitle);

            ApplyFilter();

            // Avalonia: Dispatcher.UIThread.Post thay cho Dispatcher.BeginInvoke của WPF
            Dispatcher.UIThread.Post(UpdateSetEditControlsVisibility, DispatcherPriority.Loaded);
        }

        private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var nameKeyword = SetNameSearchBox.Text?.Trim() ?? "";
            var novelKeyword = NovelSearchBox.Text?.Trim() ?? "";

            var shared = _allSets.Where(s => s.IsShared);
            var priv = _allSets.Where(s => !s.IsShared);

            if (!string.IsNullOrEmpty(nameKeyword))
            {
                shared = shared.Where(s => s.Name.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase));
                priv = priv.Where(s => s.Name.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(novelKeyword))
            {
                priv = priv.Where(s => s.OwnerNovelId.HasValue
                    && _novelTitles.TryGetValue(s.OwnerNovelId.Value, out var title)
                    && title.Contains(novelKeyword, StringComparison.OrdinalIgnoreCase));
            }

            var sharedVms = shared.Select(ToRowViewModel).ToList();
            var privVms = priv.Select(ToRowViewModel).ToList();

            SharedList.ItemsSource = sharedVms;
            PrivateList.ItemsSource = privVms;

            SharedSection.IsVisible = sharedVms.Count > 0;
            PrivateSection.IsVisible = privVms.Count > 0;
        }

        private GlossarySetRowViewModel ToRowViewModel(GlossarySet set)
        {
            string badge = set.IsShared
                ? ""
                : (set.OwnerNovelId.HasValue && _novelTitles.TryGetValue(set.OwnerNovelId.Value, out var t) ? t : "");

            return new GlossarySetRowViewModel
            {
                Id = set.Id,
                Name = set.Name,
                IsShared = set.IsShared,
                BadgeText = badge,
                SortOrder = set.SortOrder
            };
        }

        private void OnCreateSetClick(object? sender, RoutedEventArgs e)
        {
            var name = NewSetNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return;

            using var db = OpenDb();
            var maxOrder = db.GlossarySets.Any() ? db.GlossarySets.Max(x => x.SortOrder) : -1;
            db.GlossarySets.Add(new GlossarySet { Name = name, IsShared = true, SortOrder = maxOrder + 1 });
            db.SaveChanges();

            NewSetNameBox.Text = "";
            LoadSets();
        }

        private void OnToggleSetClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not GlossarySetRowViewModel vm) return;

            vm.IsExpanded = !vm.IsExpanded;
            if (vm.IsExpanded && vm.AllEntries.Count == 0)
                LoadEntriesForSet(vm);
        }

        private Guid? _renamingSetId;

        private void OnRenameSetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;

            _renamingSetId = vm.Id;
            RenameNameBox.Text = vm.Name;

            RenameCard.IsVisible = true;
            if (RenameCard.Parent is Panel panel) panel.Children.Remove(RenameCard);
            ModalService.Show(RenameCard);
        }

        private void OnRenameSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_renamingSetId == null) return;

            var newName = RenameNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(newName)) return;

            using var db = OpenDb();
            var set = db.GlossarySets.Find(_renamingSetId.Value);
            if (set != null)
            {
                set.Name = newName;
                db.SaveChanges();
            }

            ModalService.Close();
            LoadSets();
        }

        private void OnRenameCancelClick(object? sender, RoutedEventArgs e) => ModalService.Close();

        private void LoadEntriesForSet(GlossarySetRowViewModel vm)
        {
            using var db = OpenDb();
            vm.AllEntries = db.GlossarySetEntries
                .Where(e => e.GlossarySetId == vm.Id)
                .OrderBy(e => e.OriginalTerm)
                .ToList();

            vm.CurrentPage = 1;
            RenderPage(vm);
        }

        private void RenderPage(GlossarySetRowViewModel vm)
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(vm.AllEntries.Count / (double)vm.PageSize));
            if (vm.CurrentPage > totalPages)
                vm.CurrentPage = totalPages;

            vm.PageEntries = new ObservableCollection<GlossarySetEntry>(
                vm.AllEntries.Skip((vm.CurrentPage - 1) * vm.PageSize).Take(vm.PageSize));

            vm.PageLabel = $"(tổng {vm.AllEntries.Count} tên)";
            vm.PageItems = BuildPageItems(vm.CurrentPage, totalPages);

            vm.RaiseEmptyChanged();
        }

        private void OnPageSizeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo || combo.Tag is not GlossarySetRowViewModel vm) return;
            if (combo.SelectedItem is not ComboBoxItem item) return;

            vm.PageSize = int.Parse((string)item.Content!);
            vm.CurrentPage = 1;
            RenderPage(vm);
        }

        private void OnPrevPageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            if (vm.CurrentPage <= 1) return;
            vm.CurrentPage--;
            RenderPage(vm);
        }

        private void OnNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            vm.CurrentPage++;
            RenderPage(vm);
        }

        private void OnPageNumberClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn ||
                btn.Tag is not GlossarySetRowViewModel vm ||
                !int.TryParse(btn.Content?.ToString(), out var page))
                return;

            if (page < 1) return;

            int totalPages = Math.Max(1, (int)Math.Ceiling(vm.AllEntries.Count / (double)vm.PageSize));
            if (page > totalPages) return;

            vm.CurrentPage = page;
            RenderPage(vm);
        }

        // ================= THÊM TÊN MỚI (Gốc / Hán Việt / Dịch) =================

        private void OnNewOriginalTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox originalBox || originalBox.Parent is not Grid grid) return;
            if (grid.Children.Count < 2 || grid.Children[1] is not TextBox hanVietBox) return;

            var original = originalBox.Text ?? "";
            var converted = _sinoVietnamese.ToHanViet(original);
            hanVietBox.Text = string.IsNullOrWhiteSpace(converted) ? original : converted;
        }

        private void OnNewNameTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox nameBox || nameBox.Tag is not GlossarySetRowViewModel vm) return;
            if (nameBox.Parent is not Grid grid || grid.Parent is not StackPanel panel) return;

            var warning = panel.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "AddDupWarning");
            if (warning == null) return;

            var name = nameBox.Text?.Trim() ?? "";
            var matches = string.IsNullOrWhiteSpace(name)
                ? new List<GlossarySetEntry>()
                : vm.AllEntries.Where(x => string.Equals(x.TranslatedTerm?.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();

            warning.IsVisible = matches.Count > 0;
            warning.Text = matches.Count > 0
                ? $"⚠ Tên dịch \"{name}\" đã dùng cho: {string.Join(", ", matches.Select(m => m.OriginalTerm))}"
                : "";
        }

        private void OnAddEntryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            if (btn.Parent is not Grid grid) return;

            var originalBox = (TextBox)grid.Children[0];
            var hanVietBox = (TextBox)grid.Children[1];
            var nameBox = (TextBox)grid.Children[2];

            var original = originalBox.Text?.Trim() ?? "";
            var hanViet = hanVietBox.Text?.Trim() ?? "";
            var name = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(name)) return;

            using var db = OpenDb();
            if (db.GlossarySetEntries.Any(x => x.GlossarySetId == vm.Id && x.OriginalTerm == original)) return;

            db.GlossarySetEntries.Add(new GlossarySetEntry
            {
                GlossarySetId = vm.Id,
                OriginalTerm = original,
                HanViet = string.IsNullOrWhiteSpace(hanViet) ? _sinoVietnamese.ToHanViet(original) : hanViet,
                PinYin = _sinoVietnamese.ToPinYin(original),
                TranslatedTerm = name
            });
            db.SaveChanges();

            originalBox.Text = "";
            hanVietBox.Text = "";
            nameBox.Text = "";
            LoadEntriesForSet(vm);
        }

        private void OnDeleteSetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;

            using var db = OpenDb();
            int entryCount = db.GlossarySetEntries.Count(x => x.GlossarySetId == vm.Id);

            ShowConfirm($"Xóa bộ tên \"{vm.Name}\"? Toàn bộ {entryCount} tên trong bộ này sẽ bị xóa vĩnh viễn.", () =>
            {
                using var db2 = OpenDb();
                var set = db2.GlossarySets.Find(vm.Id);
                if (set != null) { db2.GlossarySets.Remove(set); db2.SaveChanges(); }

                LoadSets();
            });
        }

        // ================= XÁC NHẬN DÙNG CHUNG =================

        private Action? _pendingConfirmAction;

        private void ShowConfirm(string message, Action onConfirm)
        {
            ConfirmMessageText.Text = message;
            _pendingConfirmAction = onConfirm;

            ConfirmCard.IsVisible = true;
            if (ConfirmCard.Parent is Panel panel) panel.Children.Remove(ConfirmCard);
            ModalService.Show(ConfirmCard);
        }

        private void OnConfirmYesClick(object? sender, RoutedEventArgs e)
        {
            var action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            ModalService.Close();
            action?.Invoke();
        }

        private void OnConfirmNoClick(object? sender, RoutedEventArgs e)
        {
            _pendingConfirmAction = null;
            ModalService.Close();
        }

        // ================= SỬA 1 TÊN (Hán Việt / Dịch) =================

        private void OnEditEntryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetEntry entry) return;

            _editingEntry = entry;
            EditOriginalText.Text = entry.OriginalTerm;
            EditHanVietBox.Text = entry.HanViet ?? "";
            EditNameBox.Text = entry.TranslatedTerm;

            EditCard.IsVisible = true;
            if (EditCard.Parent is Panel panel) panel.Children.Remove(EditCard);
            ModalService.Show(EditCard);
        }

        private void OnDeleteEntryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetEntry entry) return;

            ShowConfirm($"Xóa tên \"{entry.OriginalTerm} → {entry.TranslatedTerm}\"?", () =>
            {
                using var db = OpenDb();
                var toRemove = db.GlossarySetEntries.Find(entry.Id);
                if (toRemove != null) { db.GlossarySetEntries.Remove(toRemove); db.SaveChanges(); }

                var vm = FindVmContaining(entry.GlossarySetId);
                if (vm != null) LoadEntriesForSet(vm);
            });
        }

        private void OnEntryCheckChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not GlossarySetEntry entry) return;

            var vm = FindVmContaining(entry.GlossarySetId);
            if (vm == null) return;

            if (cb.IsChecked == true) vm.SelectedEntryIds.Add(entry.Id);
            else vm.SelectedEntryIds.Remove(entry.Id);
        }

        private void OnDeleteSelectedEntriesClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            if (vm.SelectedEntryIds.Count == 0) return;

            ShowConfirm($"Xóa {vm.SelectedEntryIds.Count} tên đã chọn?", () =>
            {
                using var db = OpenDb();
                var toRemove = db.GlossarySetEntries.Where(x => vm.SelectedEntryIds.Contains(x.Id)).ToList();
                if (toRemove.Count > 0)
                {
                    db.GlossarySetEntries.RemoveRange(toRemove);
                    db.SaveChanges();
                }

                vm.SelectedEntryIds.Clear();
                LoadEntriesForSet(vm);
            });
        }

        private void OnEditSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_editingEntry == null) return;

            var original = EditOriginalText.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(original)) return;

            using var db = OpenDb();
            var entry = db.GlossarySetEntries.Find(_editingEntry.Id);
            if (entry == null) return;

            bool isDuplicate = db.GlossarySetEntries.Any(x =>
                x.GlossarySetId == entry.GlossarySetId &&
                x.Id != entry.Id &&
                x.OriginalTerm == original);
            if (isDuplicate) return;

            entry.OriginalTerm = original;
            entry.HanViet = EditHanVietBox.Text?.Trim() ?? "";
            entry.TranslatedTerm = EditNameBox.Text ?? "";
            db.SaveChanges();

            ModalService.Close();

            var vm = FindVmContaining(_editingEntry.GlossarySetId);
            if (vm != null) LoadEntriesForSet(vm);
        }

        private void OnEditNameTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_editingEntry == null) return;

            var name = EditNameBox.Text?.Trim() ?? "";
            var vm = FindVmContaining(_editingEntry.GlossarySetId);

            var matches = string.IsNullOrWhiteSpace(name) || vm == null
                ? new List<GlossarySetEntry>()
                : vm.AllEntries.Where(x => x.Id != _editingEntry.Id &&
                    string.Equals(x.TranslatedTerm?.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();

            EditNameDupWarning.IsVisible = matches.Count > 0;
            EditNameDupWarning.Text = matches.Count > 0
                ? $"⚠ Tên dịch \"{name}\" đã dùng cho: {string.Join(", ", matches.Select(m => m.OriginalTerm))}"
                : "";
        }

        private void OnEditCancelClick(object? sender, RoutedEventArgs e) => ModalService.Close();

        private GlossarySetRowViewModel? FindVmContaining(Guid setId)
        {
            return (SharedList.ItemsSource as IEnumerable<GlossarySetRowViewModel>)?.FirstOrDefault(x => x.Id == setId)
                ?? (PrivateList.ItemsSource as IEnumerable<GlossarySetRowViewModel>)?.FirstOrDefault(x => x.Id == setId);
        }

        // ================= CHẾ ĐỘ SỬA CHO DANH SÁCH BỘ TÊN =================

        private void OnEditModeClick(object? sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            EditModeButton.Content = _isEditMode ? "Xong" : "Sửa";
            BulkActionsBar.IsVisible = _isEditMode;

            if (!_isEditMode)
            {
                foreach (var vm in AllRowViewModels())
                    vm.SelectedEntryIds.Clear();
            }

            UpdateSetEditControlsVisibility();
        }

        private IEnumerable<GlossarySetRowViewModel> AllRowViewModels()
        {
            var shared = SharedList.ItemsSource as IEnumerable<GlossarySetRowViewModel> ?? Enumerable.Empty<GlossarySetRowViewModel>();
            var priv = PrivateList.ItemsSource as IEnumerable<GlossarySetRowViewModel> ?? Enumerable.Empty<GlossarySetRowViewModel>();
            return shared.Concat(priv);
        }

        private void UpdateSetEditControlsVisibility()
        {
            foreach (var list in new ItemsControl[] { SharedList, PrivateList })
            {
                // Avalonia: GetVisualDescendants() (Avalonia.VisualTree) thay cho
                // VisualTreeHelper.GetChild(...) đệ quy thủ công của WPF
                foreach (var el in list.GetVisualDescendants().OfType<Control>())
                {
                    if (el.Name is "SetDragHandleIcon" or "SetSelectCheckBox" or "EntrySelectCheckBox" or "EntryBulkActionsBar" or "EntryActionsPanel")
                        el.IsVisible = _isEditMode;

                    if (el is CheckBox cb && el.Name == "EntrySelectCheckBox")
                        cb.IsChecked = false;
                }
            }
        }

        // ----- Kéo-thả đổi thứ tự bộ tên -----

        private void OnSetDragHandleMouseDown(object? sender, PointerPressedEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragPressedEvent = e;
        }

        private async void OnSetDragHandleMouseMove(object? sender, PointerEventArgs e)
        {
            if (!_isEditMode ||
                _dragPressedEvent == null ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (sender is not Control control || control.DataContext is not GlossarySetRowViewModel vm)
                return;

            var pos = e.GetPosition(this);
            var diff = _dragStartPoint - pos;

            if (Math.Abs(diff.X) <= DragThreshold && Math.Abs(diff.Y) <= DragThreshold)
                return;

            _draggedSet = vm;

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(vm.Id.ToString()));

            var pressedEvent = _dragPressedEvent;
            _dragPressedEvent = null;

            await DragDrop.DoDragDropAsync(
                pressedEvent,
                data,
                DragDropEffects.Move);
        }

        private void OnSetItemDrop(object? sender, DragEventArgs e)
        {
            if (_draggedSet == null) return;
            if (sender is not StyledElement fe || fe.DataContext is not GlossarySetRowViewModel target)
            {
                _draggedSet = null;
                return;
            }

            // Chỉ cho kéo-thả trong cùng khu (Bộ tên chung hoặc Bộ tên riêng)
            if (target.Id == _draggedSet.Id || target.IsShared != _draggedSet.IsShared)
            {
                _draggedSet = null;
                return;
            }

            var listControl = _draggedSet.IsShared ? SharedList : PrivateList;
            if (listControl.ItemsSource is not List<GlossarySetRowViewModel> items)
            {
                _draggedSet = null;
                return;
            }

            var oldIndex = items.FindIndex(x => x.Id == _draggedSet.Id);
            var newIndex = items.FindIndex(x => x.Id == target.Id);
            if (oldIndex < 0 || newIndex < 0)
            {
                _draggedSet = null;
                return;
            }

            var moved = _draggedSet;
            items.RemoveAt(oldIndex);
            items.Insert(newIndex, moved);

            using (var db = OpenDb())
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var entity = db.GlossarySets.Find(items[i].Id);
                    if (entity != null) entity.SortOrder = i;
                }
                db.SaveChanges();
            }

            _draggedSet = null;
            LoadSets();
        }

        // ----- Chọn nhiều để xóa bộ tên -----

        private void OnDeleteSelectedSetsClick(object? sender, RoutedEventArgs e)
        {
            var selectedShared = (SharedList.ItemsSource as List<GlossarySetRowViewModel>)?.Where(x => x.IsSelected)
                ?? Enumerable.Empty<GlossarySetRowViewModel>();
            var selectedPrivate = (PrivateList.ItemsSource as List<GlossarySetRowViewModel>)?.Where(x => x.IsSelected)
                ?? Enumerable.Empty<GlossarySetRowViewModel>();

            var selectedIds = selectedShared.Concat(selectedPrivate).Select(x => x.Id).ToList();
            if (selectedIds.Count == 0) return;

            ShowConfirm($"Xóa {selectedIds.Count} bộ tên đã chọn? Toàn bộ tên bên trong các bộ này sẽ bị xóa vĩnh viễn.", () =>
            {
                using var db = OpenDb();
                var sets = db.GlossarySets.Where(x => selectedIds.Contains(x.Id)).ToList();
                if (sets.Count > 0)
                {
                    db.GlossarySets.RemoveRange(sets);
                    db.SaveChanges();
                }

                LoadSets();
            });
        }
    }

    public class GlossarySetRowViewModel : INotifyPropertyChanged
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsShared { get; set; }
        public string BadgeText { get; set; } = "";
        public int SortOrder { get; set; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnChanged(nameof(IsSelected)); } }

        private bool _isExpanded;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnChanged(nameof(IsExpanded)); } }

        public List<GlossarySetEntry> AllEntries { get; set; } = new();

        private ObservableCollection<GlossarySetEntry> _pageEntries = new();
        public ObservableCollection<GlossarySetEntry> PageEntries
        {
            get => _pageEntries;
            set { _pageEntries = value; OnChanged(nameof(PageEntries)); }
        }

        public int PageSize { get; set; } = 50;
        public int CurrentPage { get; set; } = 1;

        private string _pageLabel = "";
        public string PageLabel { get => _pageLabel; set { _pageLabel = value; OnChanged(nameof(PageLabel)); } }

        private ObservableCollection<string> _pageItems = new();
        public ObservableCollection<string> PageItems
        {
            get => _pageItems;
            set { _pageItems = value; OnChanged(nameof(PageItems)); }
        }

        public HashSet<Guid> SelectedEntryIds { get; } = new();

        // Avalonia: bool trực tiếp cho IsVisible, thay cho Visibility + converter của WPF
        public bool IsEmpty => AllEntries.Count == 0;
        public void RaiseEmptyChanged() => OnChanged(nameof(IsEmpty));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
