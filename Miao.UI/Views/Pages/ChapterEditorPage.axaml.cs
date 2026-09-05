using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        // ===================== Nội dung dạng khối (giống trang đọc) =====================

        private readonly ObservableCollection<ReaderBlockViewModel> _blocks = new();
        private readonly ObservableCollection<ReaderDisplayGroup> _editGroups = new();

        private ReaderDisplayGroup? _lastFocusedGroup;
        private TextBox? _activeTextBox;

        public ChapterEditorPage(Guid novelId, Guid chapterId)
        {
            InitializeComponent();
            _novelId = novelId;
            _chapterId = chapterId;

            ContentBlocksList.ItemsSource = _editGroups;

            EditorScrollHost.SizeChanged += (_, e) => UpdateEditorContentWidth(e.NewSize.Width);

            LoadNovelTitle();
            LoadChapter();
            LoadChapterDropdown();

            _isLoading = false;
        }

        private static MiaoDbContext OpenDb() => new(AppPaths.DbFilePath);

        private void OnPageLoaded(object? sender, RoutedEventArgs e)
        {
            ReaderHost.SetOuterScrollEnabled?.Invoke(false);
        }

        private void OnPageUnloaded(object? sender, RoutedEventArgs e)
        {
            ReaderHost.SetOuterScrollEnabled?.Invoke(true);
        }

        private void UpdateEditorContentWidth(double availableWidth)
        {
            var available = availableWidth - 40;

            // Math.Clamp ép sàn 480px dù màn hình thực tế hẹp hơn -> tràn ngang trên điện thoại.
            // Hẹp hơn mức lý tưởng thì dùng hết chiều rộng đang có thay vì ép cứng.
            var newWidth = available < MinEditorWidth
                ? Math.Max(available, 0)
                : Math.Clamp(available, MinEditorWidth, MaxEditorWidth);
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
            SetContentBlocks(chapter.Content);
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

        private void OnTitleChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
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

        private static readonly System.Text.RegularExpressions.Regex ImageMarkerRegex =
            new(@"\[\[IMG:.+?\]\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        private void UpdateWordCount()
        {
            var text = GetBlocksAsText();
            var withoutImageMarkers = ImageMarkerRegex.Replace(text, "");
            WordCountText.Text = $"{withoutImageMarkers.Length} ký tự";
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
            SyncBlocksFromEditGroups();

            using var db = OpenDb();
            var chapter = db.WrittenChapters.Find(_chapterId);
            if (chapter == null) return;

            chapter.Title = ChapterTitleBox.Text?.Trim() ?? "";
            chapter.Content = GetBlocksAsText();
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

        // ===================== Quản lý khối nội dung (text + ảnh) =====================

        private void SetContentBlocks(string? content)
        {
            var parsed = ReaderBlock.Parse(content);
            _blocks.Clear();
            foreach (var block in parsed)
                _blocks.Add(ReaderBlockViewModel.FromBlock(block, isEditing: true));

            RebuildEditGroups();
        }

        private static void BuildGroups(IList<ReaderBlockViewModel> blocks, ObservableCollection<ReaderDisplayGroup> target)
        {
            target.Clear();

            var i = 0;
            while (i < blocks.Count)
            {
                var block = blocks[i];

                if (block.IsImage)
                {
                    target.Add(new ReaderDisplayGroup
                    {
                        IsImage = true,
                        ImagePath = block.ImagePath,
                        StartBlockIndex = i,
                        EndBlockIndex = i
                    });
                    i++;
                    continue;
                }

                var start = i;
                var lines = new List<string>();
                while (i < blocks.Count && !blocks[i].IsImage)
                {
                    lines.Add(blocks[i].Text ?? "");
                    i++;
                }

                target.Add(new ReaderDisplayGroup
                {
                    IsImage = false,
                    Text = string.Join("\n", lines),
                    StartBlockIndex = start,
                    EndBlockIndex = i - 1
                });
            }
        }

        private void RebuildEditGroups() => BuildGroups(_blocks, _editGroups);

        private void SyncBlocksFromEditGroups()
        {
            var newBlocks = new List<ReaderBlockViewModel>();

            foreach (var group in _editGroups)
            {
                if (group.IsImage)
                {
                    if (group.StartBlockIndex >= 0 && group.StartBlockIndex < _blocks.Count)
                        newBlocks.Add(_blocks[group.StartBlockIndex]);
                    continue;
                }

                foreach (var line in (group.Text ?? "").Split('\n'))
                {
                    newBlocks.Add(new ReaderBlockViewModel
                    {
                        Type = ReaderBlockType.Text,
                        Text = line,
                        IsEditing = true
                    });
                }
            }

            _blocks.Clear();
            foreach (var b in newBlocks)
                _blocks.Add(b);
        }

        private string GetBlocksAsText() => ReaderBlock.Serialize(_blocks.Select(vm => vm.ToBlock()));

        private void OnBlockTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
            UpdateWordCount();
            ScheduleAutoSave();
        }

        private void OnBlockTextBoxFocused(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ReaderDisplayGroup group)
            {
                _lastFocusedGroup = group;
                _activeTextBox = tb;
            }
        }

        private static int GetCaretLineIndex(TextBox tb)
        {
            var text = tb.Text ?? "";
            var caret = Math.Clamp(tb.CaretIndex, 0, text.Length);
            return text[..caret].Count(c => c == '\n');
        }

        private int ComputeInsertIndexForGroup(ReaderDisplayGroup targetGroup, int lineIndexInGroup)
        {
            var index = 0;
            foreach (var group in _editGroups)
            {
                if (ReferenceEquals(group, targetGroup))
                {
                    var lineCount = (group.Text ?? "").Split('\n').Length;
                    var clampedLine = Math.Clamp(lineIndexInGroup, 0, lineCount);
                    return index + clampedLine;
                }

                index += group.IsImage ? 1 : (group.Text ?? "").Split('\n').Length;
            }

            return index;
        }

        // ===================== Nút định dạng cố định trên thanh trên cùng =====================

        private TextBox? GetActiveOrFirstTextBox()
        {
            if (_activeTextBox != null) return _activeTextBox;

            var firstGroup = _editGroups.FirstOrDefault(g => !g.IsImage);
            if (firstGroup == null) return null;

            var container = ContentBlocksList.ContainerFromIndex(_editGroups.IndexOf(firstGroup));
            return container?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        }

        private void OnInsertImageButtonClick(object? sender, RoutedEventArgs e) => _ = InsertImageAsync();

        private void OnBoldButtonClick(object? sender, RoutedEventArgs e)
        {
            var tb = GetActiveOrFirstTextBox();
            if (tb != null) WrapContentSelection(tb, "b");
        }

        private void OnItalicButtonClick(object? sender, RoutedEventArgs e)
        {
            var tb = GetActiveOrFirstTextBox();
            if (tb != null) WrapContentSelection(tb, "i");
        }

        private void OnUnderlineButtonClick(object? sender, RoutedEventArgs e)
        {
            var tb = GetActiveOrFirstTextBox();
            if (tb != null) WrapContentSelection(tb, "u");
        }

        private void OnStrikeButtonClick(object? sender, RoutedEventArgs e)
        {
            var tb = GetActiveOrFirstTextBox();
            if (tb != null) WrapContentSelection(tb, "s");
        }

        // ===================== Toolbar định dạng (hiện khi chuột phải vào 1 khối chữ) =====================

        private DispatcherTimer? _longPressTimer;
        private Point _longPressStartPos;
        private TextBox? _longPressTargetBox;
        private const double LongPressMoveTolerance = 12;
        private const int LongPressDurationMs = 450;

        private void OnBlockTextBoxPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            if (e.GetCurrentPoint(tb).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                _activeTextBox = tb;
                if (tb.DataContext is ReaderDisplayGroup group) _lastFocusedGroup = group;
                ShowFormattingToolbar(tb);
                return;
            }

            // Trên Android không có "chuột phải" — giữ ngón tay đủ lâu (long-press) mở cùng toolbar.
            // Không bật cho desktop để không đổi hành vi click/kéo-chọn chữ bằng chuột trái đang có.
            if (!PlatformServices.IsTouchPlatform) return;

            _longPressTargetBox = tb;
            _longPressStartPos = e.GetPosition(tb);
            _longPressTimer?.Stop();
            _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressDurationMs) };
            _longPressTimer.Tick += (_, _) =>
            {
                _longPressTimer!.Stop();
                _activeTextBox = tb;
                if (tb.DataContext is ReaderDisplayGroup group) _lastFocusedGroup = group;
                ShowFormattingToolbar(tb);
            };
            _longPressTimer.Start();
        }

        private void OnBlockTextBoxPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_longPressTimer == null || _longPressTargetBox == null) return;

            var delta = e.GetPosition(_longPressTargetBox) - _longPressStartPos;
            if (Math.Abs(delta.X) > LongPressMoveTolerance || Math.Abs(delta.Y) > LongPressMoveTolerance)
                _longPressTimer.Stop();
        }

        private void OnBlockTextBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _longPressTimer?.Stop();
        }

        private void ShowFormattingToolbar(TextBox targetBox)
        {
            Button MakeToolButton(string label, Action onClick)
            {
                var btn = new Button { Content = label, Classes = { "EditorToolButton" } };
                btn.Click += (_, _) => onClick();
                return btn;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

            row.Children.Add(MakeToolButton("B", () => WrapContentSelection(targetBox, "b")));
            row.Children.Add(MakeToolButton("I", () => WrapContentSelection(targetBox, "i")));
            row.Children.Add(MakeToolButton("U", () => WrapContentSelection(targetBox, "u")));
            row.Children.Add(MakeToolButton("S", () => WrapContentSelection(targetBox, "s")));
            row.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 6) });
            row.Children.Add(MakeToolButton("Trái", () => targetBox.TextAlignment = TextAlignment.Left));
            row.Children.Add(MakeToolButton("Giữa", () => targetBox.TextAlignment = TextAlignment.Center));
            row.Children.Add(MakeToolButton("Phải", () => targetBox.TextAlignment = TextAlignment.Right));
            row.Children.Add(new Separator { Width = 1, Margin = new Thickness(4, 6) });
            row.Children.Add(MakeToolButton("🖼 Ảnh", () => _ = InsertImageAsync()));

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
            flyout.ShowAt(targetBox);
        }

        private void WrapContentSelection(TextBox targetBox, string tag)
        {
            var text = targetBox.Text ?? "";
            var (newText, newStart, newEnd) = ReaderRichText.WrapSelection(text, targetBox.SelectionStart, targetBox.SelectionEnd, tag);
            targetBox.Text = newText;
            targetBox.SelectionStart = newStart;
            targetBox.SelectionEnd = newEnd;
        }

        // ===================== Chèn ảnh (giống trang đọc) =====================

        private string GetNovelImageDirectory()
        {
            var dbDirectory = AppSettingsService.Instance.Settings.DataFolder;
            var dir = Path.Combine(dbDirectory, "Images", $"Novel_{_novelId}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetRelativeNovelImagePath(string fileName)
            => Path.Combine("Images", $"Novel_{_novelId}", fileName);

        private string ResolveImageFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                path = uri.LocalPath;

            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppSettingsService.Instance.Settings.DataFolder, path);
        }

        private async Task InsertImageAsync()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Chọn ảnh minh họa",
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
            });

            if (result is null || result.Count == 0) return;

            try
            {
                var imageDirectory = GetNovelImageDirectory();

                var extension = Path.GetExtension(result[0].Name);
                if (string.IsNullOrWhiteSpace(extension)) extension = ".png";

                var fileName = $"{_novelId}_{_chapterNumber}_{Guid.NewGuid():N}{extension}";
                var destPath = Path.Combine(imageDirectory, fileName);

                using (var sourceStream = await result[0].OpenReadAsync())
                using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destStream);
                    await destStream.FlushAsync();
                }

                if (!File.Exists(destPath))
                {
                    await DialogService.ShowYesNoAsync("Không thể lưu ảnh vào thư mục dữ liệu của truyện.", "Lỗi chèn ảnh");
                    return;
                }

                InsertImageBlock(GetRelativeNovelImagePath(fileName));
                ScheduleAutoSave();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InsertImageAsync] Lỗi chèn ảnh: {ex}");
                await DialogService.ShowYesNoAsync($"Không thể chèn ảnh: {ex.Message}", "Lỗi chèn ảnh");
            }
        }

        private void InsertImageBlock(string relativeImagePath)
        {
            int? insertIndex = null;
            if (_lastFocusedGroup != null && !_lastFocusedGroup.IsImage && _activeTextBox != null)
            {
                var lineIndex = GetCaretLineIndex(_activeTextBox);
                insertIndex = ComputeInsertIndexForGroup(_lastFocusedGroup, lineIndex);
            }

            SyncBlocksFromEditGroups();

            var newBlock = new ReaderBlockViewModel
            {
                Type = ReaderBlockType.Image,
                ImagePath = relativeImagePath,
                IsEditing = true
            };

            var finalInsertIndex = Math.Clamp(insertIndex ?? _blocks.Count, 0, _blocks.Count);
            _blocks.Insert(finalInsertIndex, newBlock);

            RebuildEditGroups();
        }

        private void OnRemoveImageBlockClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReaderDisplayGroup group || !group.IsImage) return;

            var imagePathToDelete = group.ImagePath;

            SyncBlocksFromEditGroups();

            if (group.StartBlockIndex >= 0 && group.StartBlockIndex < _blocks.Count)
                _blocks.RemoveAt(group.StartBlockIndex);

            RebuildEditGroups();
            UpdateWordCount();
            ScheduleAutoSave();

            TryDeleteImageFileIfUnused(imagePathToDelete);
        }

        private void TryDeleteImageFileIfUnused(string? relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath)) return;

            try
            {
                var fullPath = ResolveImageFullPath(relativeOrAbsolutePath);
                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return;

                var stillUsedInCurrentChapter = _blocks.Any(b =>
                    b.Type == ReaderBlockType.Image &&
                    string.Equals(b.ImagePath, relativeOrAbsolutePath, StringComparison.Ordinal));

                if (stillUsedInCurrentChapter) return;

                using var db = OpenDb();

                var stillUsedInOtherChapters = db.WrittenChapters
                    .Where(c => c.NovelId == _novelId && c.Id != _chapterId)
                    .Any(c => c.Content != null && c.Content.Contains(relativeOrAbsolutePath));

                if (!stillUsedInOtherChapters)
                    File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TryDeleteImageFileIfUnused] Lỗi xóa file ảnh: {ex}");
            }
        }

        // ===================== Dán ảnh từ clipboard (giống trang đọc) =====================

        private async void OnBlockTextBoxPasteCheck(object? sender, KeyEventArgs e)
        {
            var isPaste = e.Key == Key.V &&
                (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
            if (!isPaste) return;
            if (sender is not TextBox tb || tb.DataContext is not ReaderDisplayGroup group) return;

            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return;

            var bytes = await TryGetClipboardImageBytesAsync(clipboard);
            if (bytes == null || bytes.Length == 0) return;

            e.Handled = true;

            try
            {
                var imageDirectory = GetNovelImageDirectory();
                var pasteFileName = $"{_novelId}_{_chapterNumber}_{Guid.NewGuid():N}.png";
                var destPath = Path.Combine(imageDirectory, pasteFileName);
                await File.WriteAllBytesAsync(destPath, bytes);

                var lineIndex = GetCaretLineIndex(tb);
                var insertIndexBeforeSync = ComputeInsertIndexForGroup(group, lineIndex);

                SyncBlocksFromEditGroups();

                var newBlock = new ReaderBlockViewModel
                {
                    Type = ReaderBlockType.Image,
                    ImagePath = GetRelativeNovelImagePath(pasteFileName),
                    IsEditing = true
                };

                var insertIndex = Math.Clamp(insertIndexBeforeSync, 0, _blocks.Count);
                _blocks.Insert(insertIndex, newBlock);
                RebuildEditGroups();
                ScheduleAutoSave();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnBlockTextBoxPasteCheck] Lỗi dán ảnh: {ex}");
            }
        }

        private static async Task<byte[]?> TryGetClipboardImageBytesAsync(IClipboard clipboard)
        {
            try
            {
                var clipboardType = clipboard.GetType();
                var getFormatsMethod = clipboardType.GetMethod("GetFormatsAsync");
                var getDataMethod = clipboardType.GetMethod("GetDataAsync");
                if (getFormatsMethod == null || getDataMethod == null) return null;

                if (getFormatsMethod.Invoke(clipboard, null) is not Task formatsTask) return null;
                await formatsTask.ConfigureAwait(true);

                var formats = formatsTask.GetType().GetProperty("Result")?.GetValue(formatsTask) as string[];
                var imageFormat = formats?.FirstOrDefault(f =>
                    f.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("png", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("bitmap", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("dib", StringComparison.OrdinalIgnoreCase));
                if (imageFormat == null) return null;

                if (getDataMethod.Invoke(clipboard, new object[] { imageFormat }) is not Task dataTask) return null;
                await dataTask.ConfigureAwait(true);

                return dataTask.GetType().GetProperty("Result")?.GetValue(dataTask) as byte[];
            }
            catch
            {
                return null;
            }
        }
    }
}