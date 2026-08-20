using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.Core.Models;
using Miao.UI.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class TagPickerView : UserControl
    {
        private readonly Guid _novelId;
        private List<TagCategoryGroup> _allGroups = new();

        /// <summary>
        /// Thay cho ShowDialog() + đọc property Confirmed đồng bộ của WPF (không dùng được
        /// với ModalService vì Show() không block). Gọi callback này khi người dùng bấm
        /// Xong/Huỷ, tham số là true nếu đã lưu (bấm Xong), false nếu huỷ.
        /// Cách dùng ở nơi gọi:
        ///   var picker = new TagPickerView(novelId);
        ///   picker.OnClosed = confirmed => { if (confirmed) { ...reload... } };
        ///   ModalService.Show(picker);
        /// </summary>
        public Action<bool>? OnClosed { get; set; }

        public TagPickerView(Guid novelId)
        {
            InitializeComponent();
            _novelId = novelId;
            LoadTags();
        }

        private void LoadTags()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var selectedIds = db.NovelTags.Where(nt => nt.NovelId == _novelId).Select(nt => nt.TagId).ToHashSet();
            _allGroups = db.Tags
                .ToList()
                .GroupBy(t => t.Category)
                .Select(g => new TagCategoryGroup
                {
                    Category = g.Key,
                    Tags = g.Select(t => new TagCheckItem
                    {
                        TagId = t.Id,
                        Name = t.Name,
                        IsSelected = selectedIds.Contains(t.Id)
                    }).ToList()
                })
                .OrderBy(g => g.Category)
                .ToList();
            CategoriesList.ItemsSource = _allGroups;
        }

        private void OnSearchChanged(object? sender, TextChangedEventArgs e)
        {
            var keyword = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(keyword))
            {
                CategoriesList.ItemsSource = _allGroups;
                return;
            }
            var filtered = _allGroups
                .Select(g => new TagCategoryGroup
                {
                    Category = g.Category,
                    Tags = g.Tags.Where(t => t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList()
                })
                .Where(g => g.Tags.Count > 0)
                .ToList();
            CategoriesList.ItemsSource = filtered;
        }

        private void OnAddNewTagClick(object? sender, RoutedEventArgs e)
        {
            var name = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var exists = db.Tags.Any(t => t.Name == name);
            if (!exists)
            {
                db.Tags.Add(new Tag { Name = name, Category = "Tự thêm" });
                db.SaveChanges();
            }
            SearchBox.Text = "";
            LoadTags();
        }

        private void OnDoneClick(object? sender, RoutedEventArgs e)
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var oldMappings = db.NovelTags.Where(nt => nt.NovelId == _novelId).ToList();
            db.NovelTags.RemoveRange(oldMappings);

            var selectedTags = _allGroups.SelectMany(g => g.Tags).Where(t => t.IsSelected).ToList();
            foreach (var tag in selectedTags)
                db.NovelTags.Add(new NovelTag { NovelId = _novelId, TagId = tag.TagId });

            var novel = db.Novels.Find(_novelId);
            if (novel != null)
                novel.Tags = string.Join(", ", selectedTags.Select(t => t.Name));

            db.SaveChanges();

            ModalService.Close();
            OnClosed?.Invoke(true);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            ModalService.Close();
            OnClosed?.Invoke(false);
        }
    }
}