using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        private const double CropFrameW = 240;
        private const double CropFrameH = 336;
        private InlineImageCropper? _coverCropper;

        private static readonly DataFormat<ChapterRowViewModel> ChapterDragFormat =
            DataFormat.CreateInProcessFormat<ChapterRowViewModel>("Miao.WrittenChapterRow");

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

            CoverImage.Source = IsDefaultCover(_coverPath) ? null : CoverImageResolver.Load(this, _coverPath);
        }

        private static bool IsDefaultCover(string path) =>
            string.IsNullOrWhiteSpace(path) || path.Contains("default-cover", StringComparison.OrdinalIgnoreCase);

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
                WordCountLabel = $"{c.CharacterCount} ký tự"
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
            SaveStatusText.Text = "Đang lưu...";

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
            SaveStatusText.Text = "Đã lưu";
        }

        private void OnBackClick(object? sender, PointerPressedEventArgs e)
        {
            SaveNovelInfo();
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

        private async void OnChapterDragHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not ChapterRowViewModel row) return;

            var item = new DataTransferItem();
            item.Set(ChapterDragFormat, row);

            var data = new DataTransfer();
            data.Add(item);

            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }

        private void OnChapterRowDrop(object? sender, DragEventArgs e)
        {
            if (sender is not Control targetControl || targetControl.Tag is not ChapterRowViewModel targetRow) return;

            var sourceRow = e.DataTransfer.TryGetValue(ChapterDragFormat);
            if (sourceRow == null || sourceRow.Id == targetRow.Id) return;

            var rows = ChapterRows;
            var sourceIndex = rows.FindIndex(r => r.Id == sourceRow.Id);
            var targetIndex = rows.FindIndex(r => r.Id == targetRow.Id);
            if (sourceIndex < 0 || targetIndex < 0) return;

            rows.RemoveAt(sourceIndex);
            rows.Insert(targetIndex, sourceRow);

            using var db = OpenDb();
            for (int i = 0; i < rows.Count; i++)
            {
                var chapter = db.WrittenChapters.Find(rows[i].Id);
                if (chapter != null) chapter.Number = i + 1;
            }
            db.SaveChanges();

            LoadChapters();
        }

        // ===================== Cắt ảnh bìa =====================

        private void OnCoverClick(object? sender, PointerPressedEventArgs e)
        {
            OpenCropDialog();
        }

        private void OpenCropDialog()
        {
            _coverCropper = new InlineImageCropper(CropFrameW, CropFrameH);
            CropperHost.Content = _coverCropper;

            var hasExistingCover = !IsDefaultCover(_coverPath) && File.Exists(_coverPath);
            if (hasExistingCover)
            {
                try
                {
                    _coverCropper.SetSource(_coverPath);
                    PickCoverImageButton.Content = "Chọn ảnh khác";
                }
                catch
                {
                    // Ảnh cũ lỗi/không đọc được -> coi như chưa có ảnh, để trống chờ chọn mới.
                    _coverCropper = new InlineImageCropper(CropFrameW, CropFrameH);
                    CropperHost.Content = _coverCropper;
                    PickCoverImageButton.Content = "Chọn ảnh";
                }
            }
            else
            {
                PickCoverImageButton.Content = "Chọn ảnh";
            }

            CropCard.IsVisible = true;
            if (CropCard.Parent is Panel panel) panel.Children.Remove(CropCard);
            ModalService.Show(CropCard);
        }

        private async void OnPickCoverImageClick(object? sender, RoutedEventArgs e)
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

            await using var stream = await result[0].OpenReadAsync();
            var sourceBitmap = new Bitmap(stream);

            _coverCropper = new InlineImageCropper(CropFrameW, CropFrameH);
            _coverCropper.SetSource(sourceBitmap);
            CropperHost.Content = _coverCropper;
            PickCoverImageButton.Content = "Chọn ảnh khác";
        }

        private void OpenCropDialog(Bitmap sourceBitmap)
        {
            _coverCropper = new InlineImageCropper(CropFrameW, CropFrameH);
            _coverCropper.SetSource(sourceBitmap);
            CropperHost.Content = _coverCropper;

            CropCard.IsVisible = true;
            if (CropCard.Parent is Panel panel) panel.Children.Remove(CropCard);
            ModalService.Show(CropCard);
        }

        private void OnCropCancelClick(object? sender, RoutedEventArgs e)
        {
            ModalService.Close();
            _coverCropper = null;
            CropperHost.Content = null;
            PickCoverImageButton.Content = "Chọn ảnh";
        }

        private void OnCropSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_coverCropper == null || !_coverCropper.HasImage) return;

            var pngBytes = _coverCropper.GetCroppedPngBytes();
            if (pngBytes.Length == 0) return;

            var coverFolder = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "Covers");
            Directory.CreateDirectory(coverFolder);
            var destPath = Path.Combine(coverFolder, $"{_novelId}.png");

            File.WriteAllBytes(destPath, pngBytes);

            _coverPath = destPath;
            CoverImage.Source = CoverImageResolver.Load(this, destPath);

            using (var db = OpenDb())
            {
                var novel = db.WrittenNovels.Find(_novelId);
                if (novel != null)
                {
                    novel.CoverImagePath = destPath;
                    novel.UpdatedAt = DateTime.Now;
                    db.SaveChanges();
                }
            }

            ModalService.Close();
            _coverCropper = null;
            CropperHost.Content = null;
        }
    }
}