using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class ChapterRowViewModel : INotifyPropertyChanged
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string DisplayTitle { get; set; } = "";
        public string StatusLabel { get; set; } = "";
        public string WordCountLabel { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class WriteNovelDetailPage : UserControl
    {
        private readonly Guid _novelId;
        private DispatcherTimer? _autoSaveTimer;
        private bool _isLoading = true;
        private string _coverPath = "";

        public WriteNovelDetailPage(Guid novelId)
        {
            InitializeComponent();
            _novelId = novelId;

            LoadNovel();
            LoadChapters();

            _isLoading = false;
        }

        private static MiaoDbContext OpenDb() => new(AppPaths.DbFilePath);

        private void LoadNovel()
        {
            using var db = OpenDb();
            var novel = db.WrittenNovels.Find(_novelId);
            if (novel == null) return;

            TitleBox.Text = novel.Title;
            AuthorBox.Text = novel.Author;
            DescriptionBox.Text = novel.Description;
            NotesBox.Text = novel.Notes;
            _coverPath = novel.CoverImagePath;
            HeaderTitleText.Text = novel.DisplayTitle;

            CoverImage.Source = CoverImageResolver.Load(this, _coverPath);
        }

        private void LoadChapters()
        {
            using var db = OpenDb();
            var chapters = db.WrittenChapters
                .Where(c => c.NovelId == _novelId)
                .OrderBy(c => c.Number)
                .ToList();

            var rows = chapters.Select(c => new ChapterRowViewModel
            {
                Id = c.Id,
                Number = c.Number,
                DisplayTitle = c.DisplayTitle,
                StatusLabel = c.IsPublished
                    ? $"Đã đăng - {c.UpdatedAt:dd/MM/yyyy}"
                    : $"Bản thảo - {c.UpdatedAt:dd/MM/yyyy}",
                WordCountLabel = $"{c.WordCount} từ"
            }).ToList();

            foreach (var row in rows)
                row.PropertyChanged += (_, _) => RefreshChapterBulkBar();

            ChaptersList.ItemsSource = rows;
            ChaptersEmptyText.IsVisible = rows.Count == 0;
            RefreshChapterBulkBar();
        }

        private List<ChapterRowViewModel> ChapterRows =>
            (ChaptersList.ItemsSource as List<ChapterRowViewModel>) ?? new();

        private void RefreshChapterBulkBar()
        {
            var selected = ChapterRows.Where(r => r.IsSelected).ToList();
            ChapterBulkActionsBar.IsVisible = selected.Count > 0;
            ChapterSelectedCountText.Text = $"Đã chọn {selected.Count} chương.";
        }

        private void OnTabClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var tab = btn.Tag as string;

            DetailTabPanel.IsVisible = tab == "Detail";
            ChaptersTabPanel.IsVisible = tab == "Chapters";
            NotesTabPanel.IsVisible = tab == "Notes";

            TabDetailButton.Classes.Set("tabButtonActive", tab == "Detail");
            TabChaptersButton.Classes.Set("tabButtonActive", tab == "Chapters");
            TabNotesButton.Classes.Set("tabButtonActive", tab == "Notes");

            if (tab == "Chapters") LoadChapters();
        }

        private void OnFieldChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
            ScheduleAutoSave();
        }

        private void ScheduleAutoSave()
        {
            SaveStatusButton.Content = "Đang lưu...";

            _autoSaveTimer?.Stop();
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _autoSaveTimer.Tick += (_, _) =>
            {
                _autoSaveTimer!.Stop();
                SaveNovelInfo();
            };
            _autoSaveTimer.Start();
        }

        private void SaveNovelInfo()
        {
            using var db = OpenDb();
            var novel = db.WrittenNovels.Find(_novelId);
            if (novel == null) return;

            novel.Title = TitleBox.Text?.Trim() ?? "";
            novel.Author = AuthorBox.Text?.Trim() ?? "";
            novel.Description = DescriptionBox.Text ?? "";
            novel.Notes = NotesBox.Text ?? "";
            novel.UpdatedAt = DateTime.Now;
            db.SaveChanges();

            HeaderTitleText.Text = novel.DisplayTitle;
            SaveStatusButton.Content = "Đã lưu";
        }

        private async void OnCoverClick(object? sender, PointerPressedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Chọn ảnh bìa",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Ảnh (*.jpg;*.jpeg;*.png;*.webp)")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" }
                    }
                }
            });

            if (result is null || result.Count == 0) return;

            var sourcePath = result[0].Path.LocalPath;
            var coverFolder = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "Covers");
            Directory.CreateDirectory(coverFolder);

            var extension = Path.GetExtension(sourcePath);
            var destPath = Path.Combine(coverFolder, $"{_novelId}{extension}");
            File.Copy(sourcePath, destPath, overwrite: true);

            _coverPath = destPath;
            CoverImage.Source = CoverImageResolver.Load(this, destPath);

            using var db = OpenDb();
            var novel = db.WrittenNovels.Find(_novelId);
            if (novel != null)
            {
                novel.CoverImagePath = destPath;
                novel.UpdatedAt = DateTime.Now;
                db.SaveChanges();
            }
        }

        private void OnPreviewClick(object? sender, RoutedEventArgs e) => SaveNovelInfo();

        private void OnBackClick(object? sender, RoutedEventArgs e)
        {
            SaveNovelInfo();
            AppNavigator.NavigateTo(new WriteNovelPage());
        }

        private async void OnDeleteClick(object? sender, RoutedEventArgs e)
        {
            var choice = await DialogService.ShowYesNoAsync(
                "Xóa truyện này? Toàn bộ chương bên trong sẽ bị xóa vĩnh viễn.", "Xóa truyện");
            if (choice != DialogResult.Yes) return;

            using var db = OpenDb();
            db.WrittenChapters.RemoveRange(db.WrittenChapters.Where(c => c.NovelId == _novelId));
            var novel = db.WrittenNovels.Find(_novelId);
            if (novel != null) db.WrittenNovels.Remove(novel);
            db.SaveChanges();

            AppNavigator.NavigateTo(new WriteNovelPage());
        }

        private void OnAddChapterClick(object? sender, RoutedEventArgs e)
        {
            using var db = OpenDb();

            var existingNumbers = db.WrittenChapters
                .Where(c => c.NovelId == _novelId)
                .Select(c => c.Number)
                .ToList();

            var nextNumber = existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;

            var chapter = new WrittenChapter { NovelId = _novelId, Number = nextNumber };
            db.WrittenChapters.Add(chapter);
            db.SaveChanges();

            AppNavigator.NavigateTo(new ChapterEditorPage(_novelId, chapter.Id));
        }

        private void OnChapterRowClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not ChapterRowViewModel row) return;
            AppNavigator.NavigateTo(new ChapterEditorPage(_novelId, row.Id));
        }

        private void OnSelectAllChaptersChanged(object? sender, RoutedEventArgs e)
        {
            var check = SelectAllChaptersBox.IsChecked == true;
            foreach (var row in ChapterRows)
                row.IsSelected = check;

            RefreshChapterBulkBar();
        }

        private void OnPublishSelectedClick(object? sender, RoutedEventArgs e) => SetPublishedForSelected(true);
        private void OnUnpublishSelectedClick(object? sender, RoutedEventArgs e) => SetPublishedForSelected(false);

        private void SetPublishedForSelected(bool published)
        {
            var ids = ChapterRows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
            if (ids.Count == 0) return;

            using var db = OpenDb();
            var chapters = db.WrittenChapters.Where(c => ids.Contains(c.Id)).ToList();
            foreach (var c in chapters)
            {
                c.IsPublished = published;
                c.UpdatedAt = DateTime.Now;
            }
            db.SaveChanges();

            LoadChapters();
        }

        private async void OnDeleteSelectedChaptersClick(object? sender, RoutedEventArgs e)
        {
            var ids = ChapterRows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
            if (ids.Count == 0) return;

            var choice = await DialogService.ShowYesNoAsync($"Xóa {ids.Count} chương đã chọn?", "Xóa chương");
            if (choice != DialogResult.Yes) return;

            using var db = OpenDb();
            db.WrittenChapters.RemoveRange(db.WrittenChapters.Where(c => ids.Contains(c.Id)));
            db.SaveChanges();

            LoadChapters();
        }

        private async void OnDeleteChapterClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChapterRowViewModel row) return;

            var choice = await DialogService.ShowYesNoAsync($"Xóa chương \"{row.DisplayTitle}\"?", "Xóa chương");
            if (choice != DialogResult.Yes) return;

            using var db = OpenDb();
            var chapter = db.WrittenChapters.Find(row.Id);
            if (chapter != null) db.WrittenChapters.Remove(chapter);
            db.SaveChanges();

            LoadChapters();
        }
    }
}