using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class NovelEditPage : UserControl
    {
        private const int VisibleGroupCount = 6;
        private const int SuggestLimit = 10;
        private const string StatusCategoryName = "Tình trạng";
        private const string DefaultStatus = "Chưa xác minh";

        private readonly Guid _novelId;
        private readonly ObservableCollection<LinkItem> _links = new();
        private List<TagCategoryGroup> _tagGroups = new();

        private bool _groupsExpanded;
        private bool _tagSuggestShowAll;
        private string _tagSuggestKeyword = "";

        private string LayoutFile =>
            Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "search_filter_layout.json");

        private class SearchFilterLayout
        {
            public List<string> GroupOrder { get; set; } = new();
            public Dictionary<string, List<Guid>> TagOrder { get; set; } = new();
        }

        public NovelEditPage(Guid novelId)
        {
            InitializeComponent();
            _novelId = novelId;
            LinksList.ItemsSource = _links;
            LoadNovel();
        }

        // ===================== Tải dữ liệu truyện =====================

        private void LoadNovel()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var novel = db.Novels.FirstOrDefault(n => n.Id == _novelId);
            if (novel == null)
                return;

            CustomTitleBox.Text = novel.CustomTitle;
            OriginalTitleBox.Text = novel.Title;
            TranslatedTitleBox.Text = novel.TranslatedTitle;
            AuthorBox.Text = novel.Author;
            DescriptionBox.Text = novel.Description;
            SourceUrlBox.Text = novel.SourceUrl;
            SourceDescriptionBox.Text = novel.SourceDescription;

            LoadTagCheckboxes(novel.Tags);

            _links.Clear();
            foreach (var link in db.NovelLinks.Where(l => l.NovelId == _novelId).ToList())
                _links.Add(new LinkItem { Description = link.Description, Url = link.Url });

            TryLoadCoverPreview(novel.CoverImagePath);
        }

        // ===================== Tag: nạp, sắp xếp theo layout đã lưu =====================

        private void LoadTagCheckboxes(string tagsText)
        {
            var selectedNames = string.IsNullOrWhiteSpace(tagsText)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var db = new MiaoDbContext(AppPaths.DbFilePath);

            _tagGroups = db.Tags
                .ToList()
                .GroupBy(t => t.Category)
                .Select(g => new TagCategoryGroup
                {
                    Category = g.Key,
                    IsStatusGroup = false,
                    Tags = g.OrderBy(t => t.Name)
                        .Select(t => new TagCheckItem
                        {
                            TagId = t.Id,
                            Name = t.Name,
                            IsSelected = selectedNames.Contains(t.Name)
                        })
                        .ToList()
                })
                .ToList();

            ApplySavedLayout();

            _groupsExpanded = false;
            _tagSuggestKeyword = "";
            _tagSuggestShowAll = false;
            TagSuggestBox.Text = "";

            RefreshTagGroupsDisplay();
            RefreshTagSuggestList();
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

        private void ApplySavedLayout()
        {
            var layout = LoadLayout();

            if (layout.GroupOrder.Count > 0)
            {
                var ordered = new List<TagCategoryGroup>();

                foreach (var category in layout.GroupOrder)
                {
                    var group = _tagGroups.FirstOrDefault(g => g.Category == category);
                    if (group != null)
                        ordered.Add(group);
                }

                foreach (var group in _tagGroups)
                {
                    if (!ordered.Contains(group))
                        ordered.Add(group);
                }

                _tagGroups = ordered;
            }

            foreach (var group in _tagGroups)
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

        // ===================== Tag: hiển thị nhóm & gợi ý tìm kiếm =====================

        private void RefreshTagGroupsDisplay()
        {
            bool isSearching = _tagSuggestKeyword.Length > 0;

            var primary = _tagGroups.Take(VisibleGroupCount).ToList();
            var extra = _tagGroups.Skip(VisibleGroupCount).ToList();

            TagCategoriesList.ItemsSource = null;
            TagCategoriesList.ItemsSource = primary;

            TagCategoriesExtraList.ItemsSource = null;
            TagCategoriesExtraList.ItemsSource = extra;
            TagCategoriesExtraList.IsVisible = !isSearching && _groupsExpanded && extra.Count > 0;

            TagGroupsShowAllButton.IsVisible = !isSearching && extra.Count > 0 && !_groupsExpanded;
        }

        private void OnShowAllGroupsClick(object? sender, RoutedEventArgs e)
        {
            _groupsExpanded = true;
            RefreshTagGroupsDisplay();
        }

        private static string NormalizeForSearch(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            var normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString()
                .Replace('đ', 'd')
                .Replace('Đ', 'D')
                .ToLowerInvariant();
        }

        private void OnTagSuggestChanged(object? sender, TextChangedEventArgs e)
        {
            _tagSuggestKeyword = TagSuggestBox.Text?.Trim() ?? "";
            _tagSuggestShowAll = false;
            RefreshTagSuggestList();
            RefreshTagGroupsDisplay();
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

            var matches = _tagGroups
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

        // ===================== Ảnh bìa =====================

        private void TryLoadCoverPreview(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppSettingsService.Instance.Settings.DataFolder, path);

            if (!File.Exists(fullPath))
                return;

            try
            {
                CoverImage.Source = new Bitmap(fullPath);
            }
            catch
            {
                CoverImage.Source = null;
            }
        }

        private async void OnPickCoverClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Chọn ảnh bìa",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Ảnh") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" } }
                }
            });

            if (files.Count == 0 || files[0].Path.LocalPath is not { } sourcePath)
                return;

            try
            {
                var coverFolder = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "Covers");
                Directory.CreateDirectory(coverFolder);

                var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (ext == ".jpeg")
                    ext = ".jpg";

                var coverPath = Path.Combine(coverFolder, $"{_novelId}{ext}");
                var relativeCoverPath = Path.Combine("Covers", $"{_novelId}{ext}");

                foreach (var oldExt in new[] { ".jpg", ".jpeg", ".png", ".webp" })
                {
                    var oldCover = Path.Combine(coverFolder, $"{_novelId}{oldExt}");

                    if (!string.Equals(oldCover, coverPath, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(oldCover))
                    {
                        File.Delete(oldCover);
                    }
                }

                File.Copy(sourcePath, coverPath, overwrite: true);

                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var novel = db.Novels.Find(_novelId);
                if (novel != null)
                {
                    novel.CoverImagePath = relativeCoverPath;
                    db.SaveChanges();
                }

                TryLoadCoverPreview(relativeCoverPath);
                CoverStatusText.Text = "Đã cập nhật ảnh bìa.";
            }
            catch (Exception ex)
            {
                CoverStatusText.Text = $"Lỗi khi chọn ảnh: {ex.Message}";
            }
        }

        // ===================== Liên kết bổ sung =====================

        private void OnAddLinkClick(object? sender, RoutedEventArgs e)
            => _links.Add(new LinkItem());

        private void OnRemoveLinkClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is LinkItem item)
                _links.Remove(item);
        }

        // ===================== Lưu / Xóa / Hủy =====================

        private void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var novel = db.Novels.Find(_novelId);
                if (novel == null)
                    return;

                novel.CustomTitle = CustomTitleBox.Text?.Trim() ?? "";
                novel.Title = OriginalTitleBox.Text?.Trim() ?? "";
                novel.TranslatedTitle = TranslatedTitleBox.Text?.Trim() ?? "";
                novel.Author = AuthorBox.Text?.Trim() ?? "";
                novel.Description = DescriptionBox.Text?.Trim() ?? "";
                novel.SourceUrl = SourceUrlBox.Text?.Trim() ?? "";
                novel.SourceDescription = SourceDescriptionBox.Text?.Trim() ?? "";

                var selectedTags = _tagGroups
                    .SelectMany(g => g.Tags)
                    .Where(t => t.IsSelected)
                    .GroupBy(t => t.TagId)
                    .Select(g => g.First())
                    .ToList();

                novel.Tags = string.Join(",", selectedTags.Select(t => t.Name));

                var statusTagName = _tagGroups
                    .FirstOrDefault(g => g.Category == StatusCategoryName)?
                    .Tags.FirstOrDefault(t => t.IsSelected)?.Name;

                novel.Status = string.IsNullOrWhiteSpace(statusTagName) ? DefaultStatus : statusTagName;

                var oldNovelTags = db.NovelTags.Where(nt => nt.NovelId == _novelId).ToList();
                db.NovelTags.RemoveRange(oldNovelTags);
                db.SaveChanges();

                foreach (var tag in selectedTags)
                    db.NovelTags.Add(new NovelTag { NovelId = _novelId, TagId = tag.TagId });

                var oldLinks = db.NovelLinks.Where(l => l.NovelId == _novelId).ToList();
                db.NovelLinks.RemoveRange(oldLinks);
                foreach (var link in _links)
                {
                    if (string.IsNullOrWhiteSpace(link.Description) && string.IsNullOrWhiteSpace(link.Url))
                        continue;

                    db.NovelLinks.Add(new NovelLink
                    {
                        NovelId = _novelId,
                        Description = (link.Description ?? "").Trim(),
                        Url = (link.Url ?? "").Trim()
                    });
                }

                db.SaveChanges();

                StatusText.Text = "Đã lưu thay đổi.";
                AppNavigator.NavigateTo(new NovelDetailPage(_novelId));
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Lỗi khi lưu: {ex.GetType().Name} — {ex.Message}";
            }
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
            => AppNavigator.NavigateTo(new NovelDetailPage(_novelId));

        private void OnBackToNovelClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new NovelDetailPage(_novelId));
    }
}