using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;
using Miao.UI.Views.Pages.Reader;

namespace Miao.UI.Views.Pages
{
    public partial class ChapterEditorPage : UserControl
    {
        private const double MinEditorWidth = 480;
        private const double MaxEditorWidth = 900;

        private readonly Guid _novelId;
        private Guid _chapterId;
        private DispatcherTimer? _autoSaveTimer;
        private bool _isLoading = true;
        private int _chapterNumber;
        private bool _isPublished;

        public ChapterEditorPage(Guid novelId, Guid chapterId)
        {
            InitializeComponent();
            _novelId = novelId;
            _chapterId = chapterId;

            EditorScrollHost.SizeChanged += (_, e) => UpdateEditorContentWidth(e.NewSize.Width);

            LoadNovelTitle();
            LoadChapter();
            LoadChapterDropdown();

            _isLoading = false;
        }

        private static MiaoDbContext OpenDb() => new(AppPaths.DbFilePath);

        private void UpdateEditorContentWidth(double availableWidth)
        {
            var newWidth = Math.Clamp(availableWidth - 40, MinEditorWidth, MaxEditorWidth);
            if (newWidth <= 0) return;
            EditorContentGrid.Width = newWidth;
        }

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
            _chapterNumber = chapter.Number;
            _isPublished = chapter.IsPublished;

            UpdateWordCount();
            UpdateChapterTitleDisplay();
            UpdatePublishButtonVisual();
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

        private void UpdateChapterTitleDisplay()
        {
            var title = ChapterTitleBox.Text?.Trim();
            ChapterTitleDisplayText.Text = string.IsNullOrWhiteSpace(title)
                ? $"Chưa đặt tiêu đề {_chapterNumber}"
                : title;
        }

        private void UpdatePublishButtonVisual()
        {
            PublishToggleButton.Classes.Set("published", _isPublished);
            PublishToggleButton.Content = _isPublished ? "Đã đăng tải" : "Đăng tải";
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
            UpdateChapterTitleDisplay();
            ScheduleAutoSave();
        }

        private void OnTogglePublishClick(object? sender, RoutedEventArgs e)
        {
            _isPublished = !_isPublished;
            UpdatePublishButtonVisual();
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
            chapter.IsPublished = _isPublished;
            chapter.UpdatedAt = DateTime.Now;
            db.SaveChanges();

            SaveStatusText.Text = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}";
        }

        private void OnBackClick(object? sender, PointerPressedEventArgs e)
        {
            SaveChapter();
            AppNavigator.NavigateTo(new WriteNovelDetailPage(_novelId));
        }

        // ===================== Toolbar định dạng (hiện khi chuột phải) =====================

        private void OnContentBoxPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(ContentBox).Properties.IsRightButtonPressed) return;

            e.Handled = true;
            ShowFormattingToolbar();
        }

        private void ShowFormattingToolbar()
        {
            Button MakeToolButton(string label, Action onClick)
            {
                var btn = new Button { Content = label, Classes = { "EditorToolButton" } };
                btn.Click += (_, _) => onClick();
                return btn;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

            row.Children.Add(MakeToolButton("B", () => WrapContentSelection("b")));
            row.Children.Add(MakeToolButton("I", () => WrapContentSelection("i")));
            row.Children.Add(MakeToolButton("U", () => WrapContentSelection("u")));
            row.Children.Add(MakeToolButton("S", () => WrapContentSelection("s")));
            row.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 6) });
            row.Children.Add(MakeToolButton("Trái", () => ContentBox.TextAlignment = TextAlignment.Left));
            row.Children.Add(MakeToolButton("Giữa", () => ContentBox.TextAlignment = TextAlignment.Center));
            row.Children.Add(MakeToolButton("Phải", () => ContentBox.TextAlignment = TextAlignment.Right));

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (IBrush)(Application.Current?.FindResource("BorderSoft") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Child = row
            };

            var flyout = new Flyout { Content = card, Placement = PlacementMode.Pointer };
            flyout.ShowAt(ContentBox);
        }

        private void WrapContentSelection(string tag)
        {
            var text = ContentBox.Text ?? "";
            var (newText, newStart, newEnd) = ReaderRichText.WrapSelection(text, ContentBox.SelectionStart, ContentBox.SelectionEnd, tag);
            ContentBox.Text = newText;
            ContentBox.SelectionStart = newStart;
            ContentBox.SelectionEnd = newEnd;
        }
    }
}