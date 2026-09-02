using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class WrittenNovelRowViewModel
    {
        public Guid Id { get; set; }
        public string DisplayTitle { get; set; } = "";
        public string CoverImagePath { get; set; } = "";
        public IImage? CoverImageSource { get; set; }
        public string ChapterCountLabel { get; set; } = "";
        public string UpdatedLabel { get; set; } = "";
    }

    public partial class WriteNovelPage : UserControl
    {
        public WriteNovelPage()
        {
            InitializeComponent();
            LoadNovels();
        }

        private static MiaoDbContext OpenDb() => new(AppPaths.DbFilePath);

        private void LoadNovels()
        {
            using var db = OpenDb();

            var novels = db.WrittenNovels
                .OrderByDescending(n => n.UpdatedAt)
                .ToList();

            var chapterCounts = db.WrittenChapters
                .GroupBy(c => c.NovelId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count());

            var rows = novels
                .Select(n => new WrittenNovelRowViewModel
                {
                    Id = n.Id,
                    DisplayTitle = n.DisplayTitle,
                    CoverImagePath = n.CoverImagePath,
                    CoverImageSource = CoverImageResolver.Load(
                        this,
                        n.CoverImagePath),

                    ChapterCountLabel = chapterCounts.TryGetValue(n.Id, out var count)
                            ? $"{count} chương"
                            : "Chưa có chương nào",

                    UpdatedLabel = $"Đã cập nhật {n.UpdatedAt:dd/MM/yyyy HH:mm}"
                })
                .ToList();

            NovelsList.ItemsSource = rows;
            EmptyStateText.IsVisible = rows.Count == 0;
        }

        private void OnCreateNovelClick(object? sender, RoutedEventArgs e)
        {
            using var db = OpenDb();
            var novel = new WrittenNovel();

            db.WrittenNovels.Add(novel);
            db.SaveChanges();

            AppNavigator.NavigateTo(new WriteNovelDetailPage(novel.Id));
        }

        private void OnEditNovelInfoClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not WrittenNovelRowViewModel row)
            {
                return;
            }

            AppNavigator.NavigateTo(new WriteNovelDetailPage(row.Id));
        }

        private void OnContinueWritingClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not WrittenNovelRowViewModel row)
            {
                return;
            }

            using var db = OpenDb();
            var lastChapter = db.WrittenChapters
                .Where(c => c.NovelId == row.Id)
                .OrderByDescending(c => c.Number)
                .FirstOrDefault();

            if (lastChapter != null)
            {
                AppNavigator.NavigateTo(new ChapterEditorPage(row.Id, lastChapter.Id));
            }
            else
            {
                AppNavigator.NavigateTo(new WriteNovelDetailPage(row.Id));
            }
        }

        private async void OnDeleteNovelClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not WrittenNovelRowViewModel row)
            {
                return;
            }

            var choice = await DialogService.ShowYesNoAsync(
                $"Xóa truyện \"{row.DisplayTitle}\"? " +
                "Toàn bộ chương bên trong sẽ bị xóa vĩnh viễn.",
                "Xóa truyện");

            if (choice != DialogResult.Yes)
                return;

            using var db = OpenDb();

            var chapters = db.WrittenChapters
                .Where(c => c.NovelId == row.Id)
                .ToList();

            db.WrittenChapters.RemoveRange(chapters);

            var novel = db.WrittenNovels.Find(row.Id);

            if (novel != null)
                db.WrittenNovels.Remove(novel);

            db.SaveChanges();
            LoadNovels();
        }
    }
}