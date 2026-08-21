using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class BlockTagSuggestItem
    {
        public Tag Tag { get; set; } = null!;
        public string Name => Tag.Name;
        public bool IsBlocked { get; set; }
    }
    
    public partial class SearchPage : UserControl
    {
        private const int VisibleGroupCount = 6;
        private const int SuggestLimit = 10;

        private List<TagCategoryGroup> _allGroups = new();
        private bool _groupsExpanded;
        private readonly ObservableCollection<Tag> _blockedTags = new();

        private bool _isReady;
        private bool _isEditMode;

        private bool _tagSuggestShowAll;
        private bool _blockSuggestShowAll;
        private string _tagSuggestKeyword = "";
        private string _blockSuggestKeyword = "";

        private Point _dragStartPoint;
        private PointerPressedEventArgs? _dragPressedEventArgs;
        private TagCategoryGroup? _dragGroup;
        private TagCheckItem? _dragTag;

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value) return;
                _isEditMode = value;

                EditButton.Content = value ? "Xong" : "Sửa";
                EditPanel.IsVisible = value;
                UpdateEditButtons();
            }
        }

        private string LayoutFile =>
            Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "search_filter_layout.json");

        public SearchPage()
        {
            InitializeComponent();

            LoadTagFilters();
            SetupTimeDropdowns();

            BlockedTagsList.ItemsSource = _blockedTags;

            _isReady = true;
            DoSearch();
        }

        // ================= NẠP DỮ LIỆU / BỐ CỤC BỘ LỌC =================

        private void LoadTagFilters()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            _allGroups = db.Tags
                .ToList()
                .GroupBy(t => t.Category)
                .Select(g => new TagCategoryGroup
                {
                    Category = g.Key,
                    IsStatusGroup = false,
                    Tags = g.OrderBy(t => t.Name)
                        .Select(t => new TagCheckItem { TagId = t.Id, Name = t.Name, IsSelected = false })
                        .ToList()
                })
                .ToList();

            ApplySavedLayout();

            _groupsExpanded = false;
            RefreshGroupListsDisplay();

            CategoryComboBox.ItemsSource = _allGroups.Select(g => g.Category).Distinct().OrderBy(x => x).ToList();
        }

        private void RefreshGroupListsDisplay()
        {
            bool isSearching = _tagSuggestKeyword.Length > 0;

            var primary = _allGroups.Take(VisibleGroupCount).ToList();
            var extra = _allGroups.Skip(VisibleGroupCount).ToList();

            TagCategoriesList.ItemsSource = null;
            TagCategoriesList.ItemsSource = primary;

            TagCategoriesExtraList.ItemsSource = null;
            TagCategoriesExtraList.ItemsSource = extra;
            TagCategoriesExtraList.IsVisible = !isSearching && _groupsExpanded && extra.Count > 0;

            TagGroupsShowAllButton.IsVisible = !isSearching && extra.Count > 0 && !_groupsExpanded;

            UpdateEditButtons();
        }

        private void UpdateEditButtons()
        {
            foreach (var list in new ItemsControl[] { TagCategoriesList, TagCategoriesExtraList })
            {
                foreach (var button in FindVisualChildren<Button>(list))
                {
                    if (button.Classes.Contains("editOnly"))
                        button.IsVisible = IsEditMode;
                }
            }
        }

        private void OnShowAllGroupsClick(object? sender, RoutedEventArgs e)
        {
            _groupsExpanded = true;
            RefreshGroupListsDisplay();
        }

        private void OnEditClick(object? sender, RoutedEventArgs e) => IsEditMode = !IsEditMode;

        // ================= LƯU / NẠP BỐ CỤC =================

        private class SearchFilterLayout
        {
            public List<string> GroupOrder { get; set; } = new();
            public Dictionary<string, List<Guid>> TagOrder { get; set; } = new();
        }

        private SearchFilterLayout LoadLayout()
        {
            try
            {
                if (!File.Exists(LayoutFile))
                    return new SearchFilterLayout();

                var json = File.ReadAllText(LayoutFile);
                return JsonSerializer.Deserialize<SearchFilterLayout>(json) ?? new SearchFilterLayout();
            }
            catch
            {
                return new SearchFilterLayout();
            }
        }

        private void SaveLayout()
        {
            try
            {
                var layout = new SearchFilterLayout
                {
                    GroupOrder = _allGroups.Select(g => g.Category).ToList()
                };

                foreach (var group in _allGroups)
                    layout.TagOrder[group.Category] = group.Tags.Select(t => t.TagId).ToList();

                Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile)!);

                var json = JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LayoutFile, json);
            }
            catch
            {
                // Không để lỗi lưu layout làm hỏng SearchPage
            }
        }

        private void ApplySavedLayout()
        {
            var layout = LoadLayout();

            if (layout.GroupOrder.Count > 0)
            {
                var ordered = new List<TagCategoryGroup>();

                foreach (var category in layout.GroupOrder)
                {
                    var group = _allGroups.FirstOrDefault(g => g.Category == category);
                    if (group != null)
                        ordered.Add(group);
                }

                foreach (var group in _allGroups)
                {
                    if (!ordered.Contains(group))
                        ordered.Add(group);
                }

                _allGroups = ordered;
            }

            foreach (var group in _allGroups)
            {
                if (!layout.TagOrder.TryGetValue(group.Category, out var order))
                    continue;

                var orderedTags = new List<TagCheckItem>();

                foreach (var tagId in order)
                {
                    var tag = group.Tags.FirstOrDefault(t => t.TagId == tagId);
                    if (tag != null && !orderedTags.Contains(tag))
                        orderedTags.Add(tag);
                }

                foreach (var tag in group.Tags)
                {
                    if (!orderedTags.Contains(tag))
                        orderedTags.Add(tag);
                }

                group.Tags = orderedTags;
            }
        }

        // ================= KÉO-THẢ SẮP XẾP NHÓM / TAG =================

        private void GroupPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsEditMode) return;

            if (sender is Control fe && fe.DataContext is TagCategoryGroup group)
            {
                _dragStartPoint = e.GetPosition(null);
                _dragPressedEventArgs = e;
                _dragGroup = group;
            }
        }

        private async void GroupPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!IsEditMode || _dragGroup == null || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
                return;

            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) < 6 && Math.Abs(diff.Y) < 6)
                return;

            var group = _dragGroup;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText($"group:{group.Category}"));

            _dragGroup = null;
            _dragTag = null;

            if (_dragPressedEventArgs == null)
                return;

            await DragDrop.DoDragDropAsync(
                _dragPressedEventArgs,
                data,
                DragDropEffects.Move);
        }

        private void GroupDrop(object? sender, DragEventArgs e)
        {
            try
            {
                var text = e.DataTransfer.TryGetText();
                if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("group:"))
                    return;

                var category = text["group:".Length..];
                var source = _allGroups.FirstOrDefault(g => g.Category == category);
                if (source == null) return;
                if (sender is not Control fe || fe.DataContext is not TagCategoryGroup target) return;
                if (ReferenceEquals(source, target)) return;

                var from = _allGroups.IndexOf(source);
                var to = _allGroups.IndexOf(target);
                if (from < 0 || to < 0) return;

                _allGroups.RemoveAt(from);
                _allGroups.Insert(to, source);

                RefreshGroupListsDisplay();
                SaveLayout();
            }
            finally
            {
                _dragPressedEventArgs = null;
                _dragGroup = null;
                _dragTag = null;
            }
        }

        private void TagPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsEditMode) return;

            if (sender is Control fe && fe.DataContext is TagCheckItem tag)
            {
                var itemsControl = FindParent<ItemsControl>(fe);
                if (itemsControl?.DataContext is TagCategoryGroup group)
                {
                    _dragStartPoint = e.GetPosition(null);
                    _dragPressedEventArgs = e;
                    _dragTag = tag;
                    _dragGroup = group;
                }
            }
        }

        private async void TagPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!IsEditMode || _dragTag == null || _dragGroup == null || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
                return;

            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) < 6 && Math.Abs(diff.Y) < 6)
                return;

            var tag = _dragTag;
            var group = _dragGroup;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText($"tag:{group.Category}:{tag.TagId}"));

            _dragTag = null;
            _dragGroup = null;

            if (_dragPressedEventArgs == null)
                return;

            await DragDrop.DoDragDropAsync(
                _dragPressedEventArgs,
                data,
                DragDropEffects.Move);
        }

        private void TagDrop(object? sender, DragEventArgs e)
        {
            try
            {
                var text = e.DataTransfer.TryGetText();
                if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("tag:"))
                    return;

                var parts = text.Split(':', 3);
                if (parts.Length != 3 || !Guid.TryParse(parts[2], out var tagId))
                    return;

                var sourceGroup = _allGroups.FirstOrDefault(g => g.Category == parts[1]);
                var sourceTag = sourceGroup?.Tags.FirstOrDefault(t => t.TagId == tagId);

                if (sourceGroup == null || sourceTag == null)
                    return;
                if (sender is not Control fe || fe.DataContext is not TagCheckItem targetTag) return;

                var targetItemsControl = FindParent<ItemsControl>(fe);
                if (targetItemsControl?.DataContext is not TagCategoryGroup targetGroup) return;

                if (!ReferenceEquals(sourceGroup, targetGroup)) return;
                if (ReferenceEquals(sourceTag, targetTag)) return;

                var from = sourceGroup.Tags.IndexOf(sourceTag);
                var to = sourceGroup.Tags.IndexOf(targetTag);
                if (from < 0 || to < 0) return;

                sourceGroup.Tags.RemoveAt(from);
                sourceGroup.Tags.Insert(to, sourceTag);

                RefreshGroupListsDisplay();
                SaveLayout();
            }
            finally
            {
                _dragPressedEventArgs = null;
                _dragTag = null;
                _dragGroup = null;
            }
        }

        private static T? FindParent<T>(Visual child) where T : class
        {
            var parent = child.GetVisualParent();

            while (parent != null)
            {
                if (parent is T result) return result;
                parent = parent.GetVisualParent();
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(Visual root) where T : Visual
        {
            foreach (var child in root.GetVisualChildren())
            {
                if (child is T match)
                    yield return match;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        // ================= SỬA / XOÁ NHÓM & TAG =================

        private void OnEditCategoryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not TagCategoryGroup group) return;

            ShowInputDialog("Sửa tên nhóm", "Tên nhóm mới:", group.Category, newName =>
            {
                if (string.IsNullOrWhiteSpace(newName) || newName == group.Category) return;

                using var db = new MiaoDbContext(AppPaths.DbFilePath);

                var tagsInGroup = db.Tags.Where(t => t.Category == group.Category).ToList();
                foreach (var tag in tagsInGroup)
                    tag.Category = newName;

                db.SaveChanges();
                LoadTagFilters();

                TagManageStatusText.Text = $"Đã đổi tên nhóm \"{group.Category}\" thành \"{newName}\".";
            });
        }

        private void OnEditTagClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not TagCheckItem item) return;

            ShowInputDialog("Sửa tên tag", "Tên tag mới:", item.Name, newName =>
            {
                if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

                using var db = new MiaoDbContext(AppPaths.DbFilePath);

                var tag = db.Tags.FirstOrDefault(t => t.Id == item.TagId);
                if (tag == null) return;

                var duplicate = db.Tags.Any(t => t.Id != tag.Id && t.Name == newName && t.Category == tag.Category);
                if (duplicate)
                {
                    ShowMessageDialog("Không thể đổi tên", $"Tag \"{newName}\" đã tồn tại trong nhóm này.");
                    return;
                }

                tag.Name = newName;
                db.SaveChanges();
                LoadTagFilters();

                TagManageStatusText.Text = $"Đã đổi tên tag thành \"{newName}\".";
            });
        }

        private void OnAddTagClick(object? sender, RoutedEventArgs e)
        {
            var category = CategoryComboBox.Text?.Trim() ?? "";
            var tagName = NewTagNameBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(category))
            {
                TagManageStatusText.Text = "Chưa nhập nhóm tag.";
                return;
            }

            if (string.IsNullOrWhiteSpace(tagName))
            {
                TagManageStatusText.Text = "Chưa nhập tên tag.";
                return;
            }

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var existing = db.Tags.FirstOrDefault(t => t.Name == tagName && t.Category == category);
            if (existing != null)
            {
                TagManageStatusText.Text = $"Tag \"{tagName}\" đã tồn tại trong nhóm \"{category}\".";
                return;
            }

            db.Tags.Add(new Tag { Name = tagName, Category = category });
            db.SaveChanges();

            NewTagNameBox.Text = "";
            LoadTagFilters();

            TagManageStatusText.Text = $"Đã thêm tag \"{tagName}\" vào nhóm \"{category}\".";
        }

        private void OnDeleteTagClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not Guid tagId) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var tag = db.Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag == null) return;

            ShowConfirmDialog("Xóa tag", $"Xóa tag \"{tag.Name}\"?", confirmed =>
            {
                if (!confirmed) return;

                using var deleteDb = new MiaoDbContext(AppPaths.DbFilePath);
                var novelTags = deleteDb.NovelTags.Where(nt => nt.TagId == tagId).ToList();
                deleteDb.NovelTags.RemoveRange(novelTags);
                var t = deleteDb.Tags.Find(tagId);
                if (t != null) deleteDb.Tags.Remove(t);
                deleteDb.SaveChanges();

                var removed = _blockedTags.FirstOrDefault(x => x.Id == tagId);
                if (removed != null) _blockedTags.Remove(removed);

                LoadTagFilters();
                TagManageStatusText.Text = $"Đã xóa tag \"{tag.Name}\".";
                DoSearch();
            });
        }

        // ================= GỢI Ý TAG =================

        private static string NormalizeForSearch(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            var normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
        }

        private void OnTagSuggestChanged(object? sender, TextChangedEventArgs e)
        {
            _tagSuggestKeyword = TagSuggestBox.Text?.Trim() ?? "";
            _tagSuggestShowAll = false;
            RefreshTagSuggestList();
            RefreshGroupListsDisplay();
        }

        private void RefreshTagSuggestList()
        {
            if (_tagSuggestKeyword.Length == 0)
            {
                TagSuggestList.ItemsSource = null;
                TagSuggestShowAllButton.IsVisible = false;
                return;
            }

            var keyword = NormalizeForSearch(_tagSuggestKeyword);

            var matches = _allGroups
                .SelectMany(g => g.Tags)
                .Where(t => NormalizeForSearch(t.Name).Contains(keyword))
                .OrderBy(t => t.Name)
                .ToList();

            TagSuggestList.ItemsSource = _tagSuggestShowAll ? matches : matches.Take(SuggestLimit).ToList();
            TagSuggestShowAllButton.IsVisible = !_tagSuggestShowAll && matches.Count > SuggestLimit;
        }

        private void OnShowAllTagSuggestClick(object? sender, RoutedEventArgs e)
        {
            _tagSuggestShowAll = true;
            RefreshTagSuggestList();
        }

        // ================= CHẶN TAG =================

        private void OnBlockTagSuggestChanged(object? sender, TextChangedEventArgs e)
        {
            _blockSuggestKeyword = BlockTagSuggestBox.Text?.Trim() ?? "";
            _blockSuggestShowAll = false;
            RefreshBlockSuggestList();
        }

        private void RefreshBlockSuggestList()
        {
            if (_blockSuggestKeyword.Length == 0)
            {
                BlockSuggestList.ItemsSource = null;
                BlockTagSuggestShowAllButton.IsVisible = false;
                return;
            }

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var keyword = NormalizeForSearch(_blockSuggestKeyword);

            var matches = db.Tags
                .ToList()
                .Where(t => NormalizeForSearch(t.Name).Contains(keyword))
                .OrderBy(t => t.Name)
                .Select(t => new BlockTagSuggestItem { Tag = t, IsBlocked = _blockedTags.Any(b => b.Id == t.Id) })
                .ToList();

            BlockSuggestList.ItemsSource = _blockSuggestShowAll ? matches : matches.Take(SuggestLimit).ToList();
            BlockTagSuggestShowAllButton.IsVisible = !_blockSuggestShowAll && matches.Count > SuggestLimit;
        }

        private void OnShowAllBlockTagSuggestClick(object? sender, RoutedEventArgs e)
        {
            _blockSuggestShowAll = true;
            RefreshBlockSuggestList();
        }

        private void OnBlockSuggestChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not Tag tag) return;

            if (cb.IsChecked == true)
            {
                if (!_blockedTags.Any(t => t.Id == tag.Id))
                    _blockedTags.Add(tag);
            }
            else
            {
                var existing = _blockedTags.FirstOrDefault(t => t.Id == tag.Id);
                if (existing != null)
                    _blockedTags.Remove(existing);
            }

            DoSearch();
        }

        private void OnRemoveBlockedTag(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is Tag tag)
            {
                _blockedTags.Remove(tag);
                DoSearch();
            }
        }

        // ================= THỜI GIAN =================

        private void SetupTimeDropdowns()
        {
            for (int m = 1; m <= 12; m++)
                TimeMonthBox.Items.Add(new ComboBoxItem { Content = m.ToString() });

            TimeMonthBox.SelectedIndex = DateTime.Now.Month - 1;

            for (int y = 2020; y <= DateTime.Now.Year; y++)
                TimeYearBox.Items.Add(new ComboBoxItem { Content = y.ToString() });

            TimeYearBox.SelectedIndex = TimeYearBox.Items.Count - 1;
        }

        // ================= TÌM KIẾM =================

        private void OnFilterChanged(object? sender, RoutedEventArgs e)
        {
            if (_isReady) DoSearch();
        }

        private void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isReady) DoSearch();
        }

        private void DoSearch()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            var selectedTagIds = _allGroups
                .Where(g => !g.IsStatusGroup)
                .SelectMany(g => g.Tags)
                .Where(t => t.IsSelected)
                .Select(t => t.TagId)
                .ToList();

            var blockedTagIds = _blockedTags.Select(t => t.Id).ToList();

            var hasTimeFilter =
                TimeMonthRadio.IsChecked == true && TimeMonthBox.SelectedItem != null && TimeYearBox.SelectedItem != null;

            var hasLengthFilter =
                LengthComboBox.SelectedItem is ComboBoxItem lengthItem &&
                lengthItem.Content?.ToString() != "Không giới hạn";

            var hasFilter = selectedTagIds.Count > 0 || blockedTagIds.Count > 0 || hasTimeFilter || hasLengthFilter;

            if (!hasFilter)
            {
                ResultsList.ItemsSource = null;
                ResultCountText.Text = "Hãy chọn ít nhất một bộ lọc để tìm truyện.";
                return;
            }

            var query = db.Novels.AsQueryable();

            if (TimeMonthRadio.IsChecked == true &&
                TimeMonthBox.SelectedItem is ComboBoxItem mi &&
                TimeYearBox.SelectedItem is ComboBoxItem yi &&
                int.TryParse(mi.Content?.ToString(), out int month) &&
                int.TryParse(yi.Content?.ToString(), out int year))
            {
                query = query.Where(n => n.AddedAt.Month == month && n.AddedAt.Year == year);
            }

            var results = query.ToList();

            if (selectedTagIds.Count > 0)
            {
                var novelIdsWithAllTags = db.NovelTags
                    .Where(nt => selectedTagIds.Contains(nt.TagId))
                    .GroupBy(nt => nt.NovelId)
                    .Where(g => selectedTagIds.All(id => g.Select(x => x.TagId).Contains(id)))
                    .Select(g => g.Key)
                    .ToHashSet();

                results = results.Where(n => novelIdsWithAllTags.Contains(n.Id)).ToList();
            }

            if (blockedTagIds.Count > 0)
            {
                var novelIdsWithBlockedTags = db.NovelTags
                    .Where(nt => blockedTagIds.Contains(nt.TagId))
                    .Select(nt => nt.NovelId)
                    .ToHashSet();

                results = results.Where(n => !novelIdsWithBlockedTags.Contains(n.Id)).ToList();
            }

            if (LengthComboBox.SelectedItem is ComboBoxItem item)
            {
                var (min, max) = ParseLengthRange(item.Content?.ToString() ?? "");

                if (min >= 0)
                {
                    var chapterCounts = db.Chapters
                        .GroupBy(c => c.NovelId)
                        .Select(g => new { NovelId = g.Key, Count = g.Count() })
                        .ToDictionary(x => x.NovelId, x => x.Count);

                    results = results.Where(n =>
                    {
                        var count = chapterCounts.TryGetValue(n.Id, out var c) ? c : 0;
                        return count >= min && (max == -1 || count <= max);
                    }).ToList();
                }
            }

            ResultsList.ItemsSource = results;
            ResultCountText.Text = results.Count == 0
                ? "Không tìm thấy truyện nào khớp bộ lọc."
                : $"Tìm thấy {results.Count} kết quả.";
        }

        private (int Min, int Max) ParseLengthRange(string text) => text switch
        {
            "0" => (0, 0),
            "1 - 20" => (1, 20),
            "21 - 50" => (21, 50),
            "51 - 100" => (51, 100),
            "101 - 200" => (101, 200),
            "201 - 300" => (201, 300),
            "301 - 500" => (301, 500),
            "501 - 1000" => (501, 1000),
            "1000+" => (1000, -1),
            _ => (-1, -1)
        };

        private void OnNovelClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is Novel novel)
                AppNavigator.NavigateTo(new NovelDetailPage(novel.Id));
        }

        // ================= DIALOG DÙNG CHUNG (qua ModalService, thay cho Window.ShowDialog) =================

        private void ShowInputDialog(string title, string label, string defaultValue, Action<string?> onResult)
        {
            var textBox = new TextBox
            {
                Text = defaultValue,
                Height = 32,
                Margin = new Thickness(0, 8, 0, 16)
            };

            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = (IBrush)this.FindResource("TextMuted")! });
            body.Children.Add(textBox);

            var shell = BuildDialogShell(title, body, out var closeAction, confirmed => onResult(confirmed ? textBox.Text?.Trim() : null),
                confirmText: "Lưu");

            ModalService.Show(shell);
            textBox.Focus();
            textBox.SelectAll();
        }

        private void ShowConfirmDialog(string title, string message, Action<bool> onResult)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });

            var shell = BuildDialogShell(title, body, out _, onResult, confirmText: "Xóa", isDanger: true);
            ModalService.Show(shell);
        }

        private void ShowMessageDialog(string title, string message)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });

            var shell = BuildDialogShell(title, body, out _, _ => { }, confirmText: "Đã hiểu", showCancel: false);
            ModalService.Show(shell);
        }

        private Border BuildDialogShell(string title, StackPanel body, out Action closeAction, Action<bool> onResult,
            string confirmText, bool isDanger = false, bool showCancel = true)
        {
            var outer = new StackPanel();

            var header = new Border
            {
                Background = (IBrush)this.FindResource("BgMain")!,
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Padding = new Thickness(16, 12)
            };
            header.Child = new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 14 };
            outer.Children.Add(header);

            var bodyContainer = new StackPanel { Margin = new Thickness(16) };
            bodyContainer.Children.Add(body);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };

            var confirmButton = new Button
            {
                Content = confirmText,
                Height = 32,
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0),
                Background = isDanger ? new SolidColorBrush(Color.FromRgb(0xB9, 0x4A, 0x48)) : new SolidColorBrush(Color.FromRgb(0x2F, 0xBF, 0x9F)),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(6)
            };

            Border shell = new()
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(10),
                Width = 360
            };

            void Close() => ModalService.Close();
            closeAction = Close;

            confirmButton.Click += (_, _) => { onResult(true); Close(); };
            buttonPanel.Children.Add(confirmButton);

            if (showCancel)
            {
                var cancelButton = new Button
                {
                    Content = "Hủy",
                    Height = 32,
                    MinWidth = 80,
                    Background = Brushes.White,
                    BorderBrush = (IBrush)this.FindResource("BorderSoft")!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6)
                };
                cancelButton.Click += (_, _) => { onResult(false); Close(); };
                buttonPanel.Children.Insert(0, cancelButton);
            }

            bodyContainer.Children.Add(buttonPanel);
            outer.Children.Add(bodyContainer);
            shell.Child = outer;

            return shell;
        }
    }

    public static class ObservableCollectionExtensions
    {
        public static void RemoveWhere<T>(this ObservableCollection<T> collection, Func<T, bool> predicate)
        {
            var items = collection.Where(predicate).ToList();
            foreach (var item in items)
                collection.Remove(item);
        }
    }
}