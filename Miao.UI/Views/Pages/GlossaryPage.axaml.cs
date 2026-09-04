using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class GlossaryPage : UserControl
    {
        private const double DragThreshold = 5.0;

        private readonly SinoVietnameseConverter _sinoVietnamese;

        private List<GlossarySet> _allSets = new();
        private List<GlossaryGroup> _allGroups = new();
        private List<GlossarySetRowViewModel> _sharedSetVms = new();
        private List<GlossarySetRowViewModel> _privateSetVms = new();
        private readonly Dictionary<Guid, bool> _groupExpandState = new();
        private Dictionary<Guid, string> _novelTitles = new();
        private GlossarySetEntry? _editingEntry;
        private System.Threading.CancellationTokenSource? _newEntryHanVietCts;
        private System.Threading.CancellationTokenSource? _editEntryHanVietCts;

        private bool _isEditMode;
        private Point _dragStartPoint;
        private GlossarySetRowViewModel? _draggedSet;
        private Guid? _draggedSetGroupId;
        private GlossaryGroupRowViewModel? _draggedGroup;
        private PointerPressedEventArgs? _dragPressedEvent;

        private GlossarySetRowViewModel? _entryPickerSourceVm;
        private List<GlossarySetEntry> _entryPickerEntries = new();
        private List<GlossarySet> _entryPickerAllSets = new();
        private HashSet<Guid> _entryPickerSelectedSetIds = new();

        private ObservableCollection<PagerItemVm> BuildPageItems(int currentPage, int totalPages)
        {
            var items = new ObservableCollection<PagerItemVm>();

            void AddPage(int page) =>
                items.Add(new PagerItemVm { Label = page.ToString(), IsCurrent = page == currentPage, Clickable = true });

            void AddEllipsis() =>
                items.Add(new PagerItemVm { Label = "...", IsCurrent = false, Clickable = false });

            if (totalPages <= 7)
            {
                for (int i = 1; i <= totalPages; i++)
                    AddPage(i);

                return items;
            }

            AddPage(1);

            if (currentPage > 4)
                AddEllipsis();

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
                AddPage(i);

            if (currentPage < totalPages - 3)
                AddEllipsis();

            AddPage(totalPages);

            return items;
        }

        public GlossaryPage()
        {
            InitializeComponent();

            var handataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "handata");
            var hanVietDictionaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translate", "zh_to_vi", "HanViet.json");
            _sinoVietnamese = new SinoVietnameseConverter(handataPath, hanVietDictionaryPath);

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
            _allGroups = db.GlossaryGroups.Include(g => g.Sets)
                .OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToList();
            _novelTitles = db.Novels.ToDictionary(n => n.Id, n => n.DisplayTitle);

            ApplyFilter();

            Dispatcher.UIThread.Post(UpdateSetEditControlsVisibility, DispatcherPriority.Loaded);
        }

        private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var nameKeyword = SetNameSearchBox.Text?.Trim() ?? "";
            var novelKeyword = NovelSearchBox.Text?.Trim() ?? "";

            bool NameOk(GlossarySet s) =>
                string.IsNullOrEmpty(nameKeyword) || s.Name.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase);

            bool NovelOk(GlossarySet s) =>
                s.IsShared || string.IsNullOrEmpty(novelKeyword) ||
                (s.OwnerNovelId.HasValue && _novelTitles.TryGetValue(s.OwnerNovelId.Value, out var title) &&
                    title.Contains(novelKeyword, StringComparison.OrdinalIgnoreCase));

            var setVmLookup = _allSets.ToDictionary(s => s.Id, ToRowViewModel);

            var sharedMatchedIds = _allSets.Where(s => s.IsShared && NameOk(s) && NovelOk(s)).Select(s => s.Id).ToHashSet();
            var privMatchedIds = _allSets.Where(s => !s.IsShared && NameOk(s) && NovelOk(s)).Select(s => s.Id).ToHashSet();

            bool GroupMatches(GlossaryGroup g, HashSet<Guid> matchedIds) =>
                string.IsNullOrEmpty(nameKeyword) ||
                g.Name.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase) ||
                g.Sets.Any(s => matchedIds.Contains(s.Id));

            var sharedGroupVms = _allGroups.Where(g => g.IsShared && GroupMatches(g, sharedMatchedIds))
                .Select(g => ToGroupRowViewModel(g, setVmLookup, sharedMatchedIds)).ToList();
            var privGroupVms = _allGroups.Where(g => !g.IsShared && GroupMatches(g, privMatchedIds))
                .Select(g => ToGroupRowViewModel(g, setVmLookup, privMatchedIds)).ToList();

            var groupedIdsShared = _allGroups.Where(g => g.IsShared).SelectMany(g => g.Sets.Select(s => s.Id)).ToHashSet();
            var groupedIdsPriv = _allGroups.Where(g => !g.IsShared).SelectMany(g => g.Sets.Select(s => s.Id)).ToHashSet();

            var sharedVms = _allSets.Where(s => s.IsShared && !groupedIdsShared.Contains(s.Id) && sharedMatchedIds.Contains(s.Id))
                .Select(s => setVmLookup[s.Id]).ToList();
            var privVms = _allSets.Where(s => !s.IsShared && !groupedIdsPriv.Contains(s.Id) && privMatchedIds.Contains(s.Id))
                .Select(s => setVmLookup[s.Id]).ToList();

            _sharedSetVms = setVmLookup.Values.Where(v => v.IsShared).ToList();
            _privateSetVms = setVmLookup.Values.Where(v => !v.IsShared).ToList();

            SharedList.ItemsSource = sharedVms;
            PrivateList.ItemsSource = privVms;
            SharedGroupsList.ItemsSource = sharedGroupVms;
            PrivateGroupsList.ItemsSource = privGroupVms;

            SharedSection.IsVisible = sharedVms.Count > 0 || sharedGroupVms.Count > 0;
            PrivateSection.IsVisible = privVms.Count > 0 || privGroupVms.Count > 0;
        }

        private GlossaryGroupRowViewModel ToGroupRowViewModel(
            GlossaryGroup group, Dictionary<Guid, GlossarySetRowViewModel> lookup, HashSet<Guid> matchedIds)
        {
            _groupExpandState.TryGetValue(group.Id, out var expanded);
            return new GlossaryGroupRowViewModel
            {
                Id = group.Id,
                Name = group.Name,
                IsShared = group.IsShared,
                SortOrder = group.SortOrder,
                IsExpanded = expanded,
                Sets = group.Sets.Where(s => matchedIds.Contains(s.Id))
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => lookup[s.Id]).ToList()
            };
        }

        private void OnToggleGroupClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not GlossaryGroupRowViewModel vm) return;

            vm.IsExpanded = !vm.IsExpanded;
            _groupExpandState[vm.Id] = vm.IsExpanded;
        }

        private GlossarySetRowViewModel ToRowViewModel(GlossarySet set)
        {
            string badge = set.IsShared
                ? ""
                : (set.OwnerNovelId.HasValue && _novelTitles.TryGetValue(set.OwnerNovelId.Value, out var t) && t != set.Name ? t : "");
            return new GlossarySetRowViewModel
            {
                Id = set.Id,
                Name = set.Name,
                IsShared = set.IsShared,
                BadgeText = badge,
                SortOrder = set.SortOrder
            };
        }

        private void OnAddSelectedEntriesToSetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            if (vm.SelectedEntryIds.Count == 0) return;

            using var db = OpenDb();
            _entryPickerSourceVm = vm;
            _entryPickerEntries = db.GlossarySetEntries
                .Where(x => vm.SelectedEntryIds.Contains(x.Id))
                .ToList();

            _entryPickerAllSets = db.GlossarySets
                .Where(s => s.Id != vm.Id)
                .OrderBy(s => s.Name)
                .ToList();
            _entryPickerSelectedSetIds = new HashSet<Guid>();

            EntrySetPickerSearchBox.Text = "";
            RenderEntrySetPickerList("");

            EntrySetPickerCard.IsVisible = true;
            if (EntrySetPickerCard.Parent is Panel panel) panel.Children.Remove(EntrySetPickerCard);
            ModalService.Show(EntrySetPickerCard);
        }

        private void OnEntrySetPickerSearchChanged(object? sender, TextChangedEventArgs e) =>
            RenderEntrySetPickerList(EntrySetPickerSearchBox.Text?.Trim() ?? "");

        private void RenderEntrySetPickerList(string keyword)
        {
            EntrySetPickerList.Children.Clear();

            var sets = string.IsNullOrEmpty(keyword)
                ? _entryPickerAllSets
                : _entryPickerAllSets.Where(s => s.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

            if (sets.Count == 0)
            {
                EntrySetPickerList.Children.Add(new TextBlock
                {
                    Text = "Chưa có bộ tên nào khớp.", FontStyle = FontStyle.Italic, FontSize = 14,
                    Foreground = Application.Current?.FindResource("TextMuted") as IBrush
                });
                return;
            }

            foreach (var set in sets)
            {
                var label = set.IsShared ? set.Name : $"{set.Name} (riêng)";
                var cb = new CheckBox { Content = label, IsChecked = _entryPickerSelectedSetIds.Contains(set.Id), Tag = set.Id };
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.Tag is not Guid sid) return;
                    if (cb.IsChecked == true) _entryPickerSelectedSetIds.Add(sid);
                    else _entryPickerSelectedSetIds.Remove(sid);
                };
                EntrySetPickerList.Children.Add(cb);
            }
        }

        private void OnEntrySetPickerSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_entryPickerSelectedSetIds.Count == 0 || _entryPickerEntries.Count == 0)
            {
                ModalService.Close();
                return;
            }

            using var db = OpenDb();
            foreach (var targetSetId in _entryPickerSelectedSetIds)
            {
                var existingTerms = db.GlossarySetEntries
                    .Where(x => x.GlossarySetId == targetSetId)
                    .Select(x => x.OriginalTerm)
                    .ToHashSet();

                foreach (var entry in _entryPickerEntries)
                {
                    if (existingTerms.Contains(entry.OriginalTerm)) continue;

                    db.GlossarySetEntries.Add(new GlossarySetEntry
                    {
                        GlossarySetId = targetSetId,
                        OriginalTerm = entry.OriginalTerm,
                        HanViet = entry.HanViet,
                        PinYin = entry.PinYin,
                        TranslatedTerm = entry.TranslatedTerm
                    });
                }
            }
            db.SaveChanges();

            ModalService.Close();

            _entryPickerSourceVm?.SelectedEntryIds.Clear();
            if (_entryPickerSourceVm != null) LoadEntriesForSet(_entryPickerSourceVm);
        }

        private void OnEntrySetPickerCancelClick(object? sender, RoutedEventArgs e) => ModalService.Close();

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
        private Guid? _renamingGroupId;

        private void OnRenameSetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;

            _renamingSetId = vm.Id;
            _renamingGroupId = null;
            RenameNameBox.Text = vm.Name;

            RenameCard.IsVisible = true;
            if (RenameCard.Parent is Panel panel) panel.Children.Remove(RenameCard);
            ModalService.Show(RenameCard);
        }

        private void OnRenameSaveClick(object? sender, RoutedEventArgs e)
        {
            var newName = RenameNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(newName)) return;

            using var db = OpenDb();

            if (_renamingGroupId != null)
            {
                var group = db.GlossaryGroups.Find(_renamingGroupId.Value);
                if (group != null) { group.Name = newName; db.SaveChanges(); }
                _renamingGroupId = null;
            }
            else if (_renamingSetId != null)
            {
                var set = db.GlossarySets.Find(_renamingSetId.Value);
                if (set != null) { set.Name = newName; db.SaveChanges(); }
                _renamingSetId = null;
            }

            ModalService.Close();
            LoadSets();
        }

        private void OnRenameGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossaryGroupRowViewModel vm) return;

            _renamingGroupId = vm.Id;
            _renamingSetId = null;
            RenameNameBox.Text = vm.Name;

            RenameCard.IsVisible = true;
            if (RenameCard.Parent is Panel panel) panel.Children.Remove(RenameCard);
            ModalService.Show(RenameCard);
        }

        private void OnDeleteGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossaryGroupRowViewModel vm) return;

            ShowConfirm($"Xóa nhóm \"{vm.Name}\"? Các bộ tên trong nhóm sẽ KHÔNG bị xóa, chỉ gỡ khỏi nhóm.", () =>
            {
                using var db = OpenDb();
                var toRemove = db.GlossaryGroups.Find(vm.Id);
                if (toRemove != null) { db.GlossaryGroups.Remove(toRemove); db.SaveChanges(); }
                LoadSets();
            });
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
            vm.CanGoPrev = vm.CurrentPage > 1;
            vm.CanGoNext = vm.CurrentPage < totalPages;

            vm.RaiseEmptyChanged();

            if (_isEditMode)
                Dispatcher.UIThread.Post(UpdateSetEditControlsVisibility, DispatcherPriority.Loaded);
        }

        private void OnQuickAddToSetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;

            if (!vm.IsExpanded)
            {
                vm.IsExpanded = true;
                if (vm.AllEntries.Count == 0) LoadEntriesForSet(vm);
            }
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
                btn.DataContext is not PagerItemVm item ||
                !int.TryParse(item.Label, out var page))
                return;

            if (page < 1) return;

            int totalPages = Math.Max(1, (int)Math.Ceiling(vm.AllEntries.Count / (double)vm.PageSize));
            if (page > totalPages) return;

            vm.CurrentPage = page;
            RenderPage(vm);
        }

        private async void OnNewOriginalTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox originalBox || originalBox.Parent is not Grid grid) return;
            if (grid.Children.Count < 3 || grid.Children[1] is not TextBox hanVietBox || grid.Children[2] is not TextBox pinYinBox) return;

            var original = originalBox.Text ?? "";

            var quickGuess = _sinoVietnamese.ToHanViet(original);
            hanVietBox.Text = string.IsNullOrWhiteSpace(quickGuess) ? original : quickGuess;
            pinYinBox.Text = _sinoVietnamese.ToPinYin(original);

            _newEntryHanVietCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _newEntryHanVietCts = cts;

            try { await Task.Delay(250, cts.Token); }
            catch (TaskCanceledException) { return; }

            if (cts.IsCancellationRequested) return;

            var accurate = await NameHanVietLookup.ToHanVietAsync(original);

            if (cts.IsCancellationRequested) return;

            if (!string.IsNullOrWhiteSpace(accurate) && originalBox.Text == original)
                hanVietBox.Text = accurate;
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

        private async void OnAddEntryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            if (btn.Parent is not Grid grid) return;

            var originalBox = (TextBox)grid.Children[0];
            var hanVietBox = (TextBox)grid.Children[1];
            var pinYinBox = (TextBox)grid.Children[2];
            var nameBox = (TextBox)grid.Children[3];

            var original = originalBox.Text?.Trim() ?? "";
            var hanViet = hanVietBox.Text?.Trim() ?? "";
            var pinYin = pinYinBox.Text?.Trim() ?? "";
            var name = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(name)) return;

            if (string.IsNullOrWhiteSpace(hanViet))
            {
                var accurate = await NameHanVietLookup.ToHanVietAsync(original);
                hanViet = string.IsNullOrWhiteSpace(accurate) ? _sinoVietnamese.ToHanViet(original) : accurate;
            }

            if (string.IsNullOrWhiteSpace(pinYin))
                pinYin = _sinoVietnamese.ToPinYin(original);

            using var db = OpenDb();
            if (GlossaryApplicationService.FindEntryByOriginalTerm(db, vm.Id, original) != null) return;

            db.GlossarySetEntries.Add(new GlossarySetEntry
            {
                GlossarySetId = vm.Id,
                OriginalTerm = original,
                HanViet = hanViet,
                PinYin = pinYin,
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

        private void OnEditEntryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetEntry entry) return;

            _editingEntry = entry;
            EditOriginalText.Text = entry.OriginalTerm;
            EditHanVietBox.Text = entry.HanViet ?? "";
            EditPinYinBox.Text = entry.PinYin ?? "";
            EditNameBox.Text = entry.TranslatedTerm;

            EditCard.IsVisible = true;
            if (EditCard.Parent is Panel panel) panel.Children.Remove(EditCard);
            ModalService.Show(EditCard);
        }

        private async void OnEditOriginalTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_editingEntry == null) return;

            var original = EditOriginalText.Text ?? "";

            var quickGuess = _sinoVietnamese.ToHanViet(original);
            EditHanVietBox.Text = string.IsNullOrWhiteSpace(quickGuess) ? original : quickGuess;
            EditPinYinBox.Text = _sinoVietnamese.ToPinYin(original);

            _editEntryHanVietCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _editEntryHanVietCts = cts;

            try { await Task.Delay(250, cts.Token); }
            catch (TaskCanceledException) { return; }

            if (cts.IsCancellationRequested) return;

            var accurate = await NameHanVietLookup.ToHanVietAsync(original);

            if (cts.IsCancellationRequested) return;

            if (!string.IsNullOrWhiteSpace(accurate) && EditOriginalText.Text == original)
                EditHanVietBox.Text = accurate;
        }

        private void OnDeleteEntryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetEntry entry) return;

            ShowConfirm(
                $"Xóa tên \"{entry.OriginalTerm} → {entry.TranslatedTerm}\"? Máy sẽ dịch lại \"{entry.OriginalTerm}\" và thay thế trong TOÀN BỘ truyện đang dùng bộ tên này.",
                () => _ = DeleteEntryAndRevertAsync(entry));
        }

        private async Task DeleteEntryAndRevertAsync(GlossarySetEntry entry)
        {
            using var db = OpenDb();
            await GlossaryApplicationService.DeleteEntryAndRevertAsync(db, entry.Id);

            var vm = FindVmContaining(entry.GlossarySetId);
            if (vm != null) LoadEntriesForSet(vm);
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

            ShowConfirm(
                $"Xóa {vm.SelectedEntryIds.Count} tên đã chọn? Máy sẽ dịch lại các tên gốc tương ứng và thay thế trong TOÀN BỘ truyện đang dùng bộ tên này.",
                () => _ = DeleteSelectedEntriesAndRevertAsync(vm));
        }

        private async Task DeleteSelectedEntriesAndRevertAsync(GlossarySetRowViewModel vm)
        {
            var ids = vm.SelectedEntryIds.ToList();

            foreach (var id in ids)
            {
                using var db = OpenDb();
                await GlossaryApplicationService.DeleteEntryAndRevertAsync(db, id);
            }

            vm.SelectedEntryIds.Clear();
            LoadEntriesForSet(vm);
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
            entry.PinYin = string.IsNullOrWhiteSpace(EditPinYinBox.Text?.Trim())
                ? _sinoVietnamese.ToPinYin(original)
                : EditPinYinBox.Text!.Trim();
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
            return _sharedSetVms.FirstOrDefault(x => x.Id == setId)
                ?? _privateSetVms.FirstOrDefault(x => x.Id == setId);
        }

        private void OnEditModeClick(object? sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            EditModeButton.Content = _isEditMode ? "Xong" : "Sửa";
            BulkActionsBar.IsVisible = _isEditMode;
            CreateSharedGroupButton.IsVisible = _isEditMode;
            CreatePrivateGroupButton.IsVisible = _isEditMode;

            if (!_isEditMode)
            {
                foreach (var vm in AllRowViewModels())
                    vm.SelectedEntryIds.Clear();
            }

            UpdateSetEditControlsVisibility();
        }

        private IEnumerable<GlossarySetRowViewModel> AllRowViewModels() => _sharedSetVms.Concat(_privateSetVms);

        private void UpdateSetEditControlsVisibility()
        {
            foreach (var list in new ItemsControl[] { SharedList, PrivateList, SharedGroupsList, PrivateGroupsList })
            {
                foreach (var el in list.GetVisualDescendants().OfType<Control>())
                {
                    if (el.Name is "SetDragHandleIcon" or "SetSelectCheckBox" or "EntrySelectCheckBox" or "EntryBulkActionsBar" or "EntryActionsPanel" or "SetActionsPanel" or "GroupActionsPanel" or "GroupDragHandleIcon")
                        el.IsVisible = _isEditMode;

                    if (el is CheckBox cb && el.Name == "EntrySelectCheckBox")
                        cb.IsChecked = false;
                }
            }
        }

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
            _draggedSetGroupId = FindOwningGroup(vm.Id, vm.IsShared)?.Id;

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(vm.Id.ToString()));

            var pressedEvent = _dragPressedEvent;
            _dragPressedEvent = null;

            await DragDrop.DoDragDropAsync(
                pressedEvent,
                data,
                DragDropEffects.Move);
        }

        private GlossaryGroupRowViewModel? FindOwningGroup(Guid setId, bool isShared)
        {
            var groupList = (isShared ? SharedGroupsList : PrivateGroupsList).ItemsSource as List<GlossaryGroupRowViewModel>;
            return groupList?.FirstOrDefault(g => g.Sets.Any(s => s.Id == setId));
        }

        private void OnSetItemDrop(object? sender, DragEventArgs e)
        {
            if (_draggedSet == null) return;
            if (sender is not StyledElement fe || fe.DataContext is not GlossarySetRowViewModel target)
            {
                _draggedSet = null;
                _draggedSetGroupId = null;
                return;
            }

            if (target.Id == _draggedSet.Id || target.IsShared != _draggedSet.IsShared)
            {
                _draggedSet = null;
                _draggedSetGroupId = null;
                return;
            }

            var targetGroupId = FindOwningGroup(target.Id, target.IsShared)?.Id;
            if (targetGroupId != _draggedSetGroupId)
            {
                _draggedSet = null;
                _draggedSetGroupId = null;
                return;
            }

            List<GlossarySetRowViewModel>? items;
            if (_draggedSetGroupId is Guid groupId)
            {
                var groupList = (_draggedSet.IsShared ? SharedGroupsList : PrivateGroupsList).ItemsSource as List<GlossaryGroupRowViewModel>;
                items = groupList?.FirstOrDefault(g => g.Id == groupId)?.Sets;
            }
            else
            {
                var listControl = _draggedSet.IsShared ? SharedList : PrivateList;
                items = listControl.ItemsSource as List<GlossarySetRowViewModel>;
            }

            if (items == null)
            {
                _draggedSet = null;
                _draggedSetGroupId = null;
                return;
            }

            var oldIndex = items.FindIndex(x => x.Id == _draggedSet.Id);
            var newIndex = items.FindIndex(x => x.Id == target.Id);
            if (oldIndex < 0 || newIndex < 0)
            {
                _draggedSet = null;
                _draggedSetGroupId = null;
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
            _draggedSetGroupId = null;
            LoadSets();
        }

        private void OnGroupDragHandleMouseDown(object? sender, PointerPressedEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragPressedEvent = e;
        }

        private async void OnGroupDragHandleMouseMove(object? sender, PointerEventArgs e)
        {
            if (!_isEditMode ||
                _dragPressedEvent == null ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (sender is not Control control || control.DataContext is not GlossaryGroupRowViewModel vm)
                return;

            var pos = e.GetPosition(this);
            var diff = _dragStartPoint - pos;

            if (Math.Abs(diff.X) <= DragThreshold && Math.Abs(diff.Y) <= DragThreshold)
                return;

            _draggedGroup = vm;

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(vm.Id.ToString()));

            var pressedEvent = _dragPressedEvent;
            _dragPressedEvent = null;

            await DragDrop.DoDragDropAsync(pressedEvent, data, DragDropEffects.Move);
        }

        private void OnGroupItemDrop(object? sender, DragEventArgs e)
        {
            if (_draggedGroup == null) return;
            if (sender is not StyledElement fe || fe.DataContext is not GlossaryGroupRowViewModel target)
            {
                _draggedGroup = null;
                return;
            }

            if (target.Id == _draggedGroup.Id || target.IsShared != _draggedGroup.IsShared)
            {
                _draggedGroup = null;
                return;
            }

            var listControl = _draggedGroup.IsShared ? SharedGroupsList : PrivateGroupsList;
            if (listControl.ItemsSource is not List<GlossaryGroupRowViewModel> items)
            {
                _draggedGroup = null;
                return;
            }

            var oldIndex = items.FindIndex(x => x.Id == _draggedGroup.Id);
            var newIndex = items.FindIndex(x => x.Id == target.Id);
            if (oldIndex < 0 || newIndex < 0)
            {
                _draggedGroup = null;
                return;
            }

            var moved = _draggedGroup;
            items.RemoveAt(oldIndex);
            items.Insert(newIndex, moved);

            using (var db = OpenDb())
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var entity = db.GlossaryGroups.Find(items[i].Id);
                    if (entity != null) entity.SortOrder = i;
                }
                db.SaveChanges();
            }

            _draggedGroup = null;
            LoadSets();
        }

        private void OnDeleteSelectedSetsClick(object? sender, RoutedEventArgs e)
        {
            var selectedShared = _sharedSetVms.Where(x => x.IsSelected);
            var selectedPrivate = _privateSetVms.Where(x => x.IsSelected);

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

        private bool _newGroupIsShared;

        private void OnCreateGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            _newGroupIsShared = btn.Tag is "True";

            NewGroupNameBox.Text = "";
            NewGroupCard.IsVisible = true;
            if (NewGroupCard.Parent is Panel panel) panel.Children.Remove(NewGroupCard);
            ModalService.Show(NewGroupCard);
        }

        private void OnNewGroupSaveClick(object? sender, RoutedEventArgs e)
        {
            var name = NewGroupNameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return;

            using var db = OpenDb();
            GlossaryGroupService.CreateGroup(db, name, _newGroupIsShared);
            ModalService.Close();
            LoadSets();
        }

        private void OnNewGroupCancelClick(object? sender, RoutedEventArgs e) => ModalService.Close();

        private List<Guid> _pickerSetIds = new();
        private Dictionary<Guid, bool> _pickerSetScopes = new();
        private List<(Guid GroupId, string Name, bool IsShared)> _pickerAllGroupsFlat = new();
        private HashSet<Guid> _pickerSelectedGroupIds = new();

        private void OnAddSetToGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GlossarySetRowViewModel vm) return;
            OpenGroupPicker(new List<GlossarySetRowViewModel> { vm });
        }

        private void OnAddSelectedSetsToGroupClick(object? sender, RoutedEventArgs e)
        {
            var selected = _sharedSetVms.Concat(_privateSetVms).Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) return;
            OpenGroupPicker(selected);
        }

        private void OpenGroupPicker(List<GlossarySetRowViewModel> targetSets)
        {
            using var db = OpenDb();
            _pickerSetIds = targetSets.Select(s => s.Id).ToList();
            _pickerSetScopes = targetSets.ToDictionary(s => s.Id, s => s.IsShared);

            var scopes = _pickerSetScopes.Values.Distinct().ToList();
            _pickerAllGroupsFlat = scopes
                .SelectMany(isShared => GlossaryGroupService.GetGroups(db, isShared).Select(g => (g.Id, g.Name, isShared)))
                .ToList();

            _pickerSelectedGroupIds = _pickerAllGroupsFlat
                .Where(g => _pickerSetIds.All(sid =>
                    db.GlossaryGroups.Any(gr => gr.Id == g.GroupId && gr.Sets.Any(s => s.Id == sid))))
                .Select(g => g.GroupId)
                .ToHashSet();

            GroupPickerSearchBox.Text = "";
            RenderGroupPickerList("");

            GroupPickerCard.IsVisible = true;
            if (GroupPickerCard.Parent is Panel panel) panel.Children.Remove(GroupPickerCard);
            ModalService.Show(GroupPickerCard);
        }

        private void OnGroupPickerSearchChanged(object? sender, TextChangedEventArgs e) =>
            RenderGroupPickerList(GroupPickerSearchBox.Text?.Trim() ?? "");

        private void RenderGroupPickerList(string keyword)
        {
            GroupPickerList.Children.Clear();

            var groups = string.IsNullOrEmpty(keyword)
                ? _pickerAllGroupsFlat
                : _pickerAllGroupsFlat.Where(g => g.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

            if (groups.Count == 0)
            {
                GroupPickerList.Children.Add(new TextBlock
                {
                    Text = "Chưa có nhóm nào khớp.", FontStyle = FontStyle.Italic, FontSize = 14,
                    Foreground = Application.Current?.FindResource("TextMuted") as IBrush
                });
                return;
            }

            var showScopeLabel = _pickerSetScopes.Values.Distinct().Count() > 1;

            foreach (var group in groups)
            {
                var label = showScopeLabel ? $"{group.Name} ({(group.IsShared ? "chung" : "riêng")})" : group.Name;
                var cb = new CheckBox { Content = label, IsChecked = _pickerSelectedGroupIds.Contains(group.GroupId), Tag = group.GroupId };
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.Tag is not Guid gid) return;
                    if (cb.IsChecked == true) _pickerSelectedGroupIds.Add(gid);
                    else _pickerSelectedGroupIds.Remove(gid);
                };
                GroupPickerList.Children.Add(cb);
            }
        }

        private void OnGroupPickerSaveClick(object? sender, RoutedEventArgs e)
        {
            using var db = OpenDb();

            foreach (var setId in _pickerSetIds)
            {
                var isShared = _pickerSetScopes[setId];
                var relevantGroupIds = _pickerAllGroupsFlat.Where(g => g.IsShared == isShared).Select(g => g.GroupId).ToHashSet();

                var currentGroupIds = db.GlossaryGroups
                    .Where(g => g.IsShared == isShared && g.Sets.Any(s => s.Id == setId))
                    .Select(g => g.Id).ToHashSet();

                var desiredGroupIds = _pickerSelectedGroupIds.Intersect(relevantGroupIds).ToHashSet();

                foreach (var addId in desiredGroupIds.Except(currentGroupIds))
                    GlossaryGroupService.AddSetToGroup(db, addId, setId);

                foreach (var removeId in currentGroupIds.Except(desiredGroupIds))
                    GlossaryGroupService.RemoveSetFromGroup(db, removeId, setId);
            }

            ModalService.Close();
            LoadSets();
        }

        private void OnGroupPickerCancelClick(object? sender, RoutedEventArgs e) => ModalService.Close();
    }

    public class PagerItemVm
    {
        public string Label { get; set; } = "";
        public bool IsCurrent { get; set; }
        public bool Clickable { get; set; } = true;
    }

    public class GlossaryGroupRowViewModel : INotifyPropertyChanged
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsShared { get; set; }
        public int SortOrder { get; set; }

        private bool _isExpanded;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnChanged(nameof(IsExpanded)); } }

        public List<GlossarySetRowViewModel> Sets { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

        private ObservableCollection<PagerItemVm> _pageItems = new();
        public ObservableCollection<PagerItemVm> PageItems
        {
            get => _pageItems;
            set { _pageItems = value; OnChanged(nameof(PageItems)); }
        }

        private bool _canGoPrev;
        public bool CanGoPrev { get => _canGoPrev; set { _canGoPrev = value; OnChanged(nameof(CanGoPrev)); } }

        private bool _canGoNext;
        public bool CanGoNext { get => _canGoNext; set { _canGoNext = value; OnChanged(nameof(CanGoNext)); } }

        public HashSet<Guid> SelectedEntryIds { get; } = new();

        public bool IsEmpty => AllEntries.Count == 0;
        public void RaiseEmptyChanged() => OnChanged(nameof(IsEmpty));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}