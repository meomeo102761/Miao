using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class ChapterEditorPage : UserControl
    {
        private readonly Guid _novelId;
        private Guid _chapterId;
        private DispatcherTimer? _autoSaveTimer;
        private bool _isLoading = true;

        public ChapterEditorPage(Guid novelId, Guid chapterId)
        {
            InitializeComponent();
            _novelId = novelId;
            _chapterId = chapterId;

            LoadNovelTitle();
            LoadChapter();
            LoadChapterDropdown();

            _isLoading = false;
        }

        private static MiaoDbContext OpenDb() => new(AppPaths.DbFilePath);

        private void LoadNovelTitle()
        {
            using var db = OpenDb();
            var novel = db.WrittenNovels.Find(_novelId);
            NovelDropdownTitleText.Text = novel?.DisplayTitle ?? "Tên truyện";
        }

        private void LoadChapter()
        {
            using var db = OpenDb();
            var chapter = db.WrittenChapters.Find(_chapterId);
            if (chapter == null) return;

            ChapterTitleBox.Text = chapter.Title;
            ContentBox.Text = chapter.Content;
            PublishedCheckBox.IsChecked = chapter.IsPublished;
            UpdateWordCount();
        }

        private void LoadChapterDropdown()
        {
            using var db = OpenDb();
            var chapters = db.WrittenChapters
                .Where(c => c.NovelId == _novelId)
                .OrderBy(c => c.Number)
                .Select(c => new ChapterRowViewModel { Id = c.Id, Number = c.Number, DisplayTitle = c.DisplayTitle })
                .ToList();

            ChapterDropdownList.ItemsSource = chapters;
        }

        private void OnToggleChapterDropdown(object? sender, RoutedEventArgs e)
        {
            LoadChapterDropdown();
            ChapterDropdownPopup.IsOpen = !ChapterDropdownPopup.IsOpen;
        }

        private void OnChapterDropdownItemClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChapterRowViewModel row) return;

            ChapterDropdownPopup.IsOpen = false;
            if (row.Id == _chapterId) return;

            SaveChapter();
            AppNavigator.NavigateTo(new ChapterEditorPage(_novelId, row.Id));
        }

        private void OnAddChapterFromDropdownClick(object? sender, RoutedEventArgs e)
        {
            ChapterDropdownPopup.IsOpen = false;
            SaveChapter();

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

        private void OnContentChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
            UpdateWordCount();
            ScheduleAutoSave();
        }

        private void OnPublishedChanged(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ScheduleAutoSave();
        }

        private void UpdateWordCount()
        {
            var text = ContentBox.Text ?? "";
            var count = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            WordCountText.Text = $"{count} từ";
        }

        private void ScheduleAutoSave()
        {
            SaveStatusText.Text = "Đang lưu...";

            _autoSaveTimer?.Stop();
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _autoSaveTimer.Tick += (_, _) =>
            {
                _autoSaveTimer!.Stop();
                SaveChapter();
            };
            _autoSaveTimer.Start();
        }

        private void SaveChapter()
        {
            using var db = OpenDb();
            var chapter = db.WrittenChapters.Find(_chapterId);
            if (chapter == null) return;

            chapter.Title = ChapterTitleBox.Text?.Trim() ?? "";
            chapter.Content = ContentBox.Text ?? "";
            chapter.IsPublished = PublishedCheckBox.IsChecked == true;
            chapter.UpdatedAt = DateTime.Now;
            db.SaveChanges();

            SaveStatusText.Text = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}";
        }

        private void OnBackClick(object? sender, RoutedEventArgs e)
        {
            SaveChapter();
            AppNavigator.NavigateTo(new WriteNovelDetailPage(_novelId));
        }
    }
}