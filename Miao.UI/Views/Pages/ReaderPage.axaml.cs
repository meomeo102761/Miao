using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;
using Miao.UI.Views.Pages.Reader;

namespace Miao.UI.Views.Pages
{
    public static class ReaderHost
    {
        public static Action<bool>? SetOuterScrollEnabled;
    }

    public sealed class ReaderDisplayGroup
    {
        public bool IsImage { get; set; }
        public string Text { get; set; } = "";
        public string? ImagePath { get; set; }

        public int StartBlockIndex { get; set; }
        public int EndBlockIndex { get; set; }
    }

    public partial class ReaderPage : UserControl
    {
        private const double MinReadingWidth = 480;
        private const double MaxReadingWidth = 900;

        private readonly Guid _novelId;
        private int _chapterNumber;

        private Chapter? _currentChapter;
        private Popup? _readerLibraryPopup;

        private bool _loadingChapter;
        private bool _isEditing;
        private bool _updatingReaderSettings;      

        private enum EditTarget { Original = 0, Translated = 1 }
        private EditTarget _editTarget = EditTarget.Original;
        private TextBox? _activeEditTextBox;

        private readonly SinoVietnameseConverter _sinoVietnamese;
        private ObservableCollection<ReaderBlockViewModel> _blocks = new();
        private ObservableCollection<ReaderDisplayGroup> _readGroups = new();
        private ObservableCollection<ReaderDisplayGroup> _editGroups = new();

        private ReaderDisplayGroup? _activeContextGroup;
        private SelectableTextBlock? _activeContextTextBlock;
        private int _activeSelectionStartOffset;

        private Dictionary<string, Character> _characterLookup = new(StringComparer.OrdinalIgnoreCase);

        public ReaderPage(Guid novelId, int chapterNumber, bool startInEditMode = false)
        {
            InitializeComponent();

            SetupReadBlocksTemplate();

            _novelId = novelId;
            _chapterNumber = chapterNumber;

            var handataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "handata");
            var hanVietDictionaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translate", "zh_to_vi", "HanViet.json");
            _sinoVietnamese = new SinoVietnameseConverter(handataPath, hanVietDictionaryPath);

            EditBlocksList.ItemsSource = _editGroups;
            ReadBlocksList.ItemsSource = _readGroups;

            ApplyFontSettings();
            LoadEditTargetPreference();
            LoadChapter();

            if (startInEditMode)
                EnterEditMode(EditTarget.Original);
        }

        private void SetupReadBlocksTemplate()
        {
            ReadBlocksList.ItemTemplate = new FuncDataTemplate<ReaderDisplayGroup>((group, _) =>
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                if (group == null) return grid;

                if (group.IsImage)
                {
                    var img = new Image
                    {
                        Stretch = Stretch.Uniform, MaxWidth = 640,
                        Margin = new Thickness(0, 10, 0, 24),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    if (this.TryFindResource("CoverImageConverter", out var conv) && conv is IValueConverter converter)
                        img.Source = converter.Convert(group.ImagePath, typeof(IImage), null, CultureInfo.CurrentCulture) as IImage;

                    grid.Children.Add(img);
                    return grid;
                }

                var stb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Tag = group };
                stb.Classes.Add("readerParagraph");
                stb.Inlines?.AddRange(ReaderRichText.ToInlines(group.Text ?? ""));
                stb.ContextRequested += OnTextBlockContextRequested;
                stb.DoubleTapped += OnParagraphDoubleTapped;

                var addItem = new MenuItem { Header = "Thêm" };
                addItem.Click += OnContextAddStyledClick;
                var addCharItem = new MenuItem { Header = "Thêm nhân vật" };
                addCharItem.Click += OnContextAddCharacterClick;
                var copyItem = new MenuItem { Header = "Copy" };
                copyItem.Click += OnContextCopyClick;

                var contextMenu = new ContextMenu();
                contextMenu.Items.Add(addItem);
                contextMenu.Items.Add(addCharItem);
                contextMenu.Items.Add(copyItem);
                stb.ContextMenu = contextMenu;

                grid.Children.Add(stb);
                return grid;
            });
        }

        private static MiaoDbContext OpenDb() =>
            new(Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "miao.db"));

        private void OnReaderLoaded(object? sender, RoutedEventArgs e)
        {
            ReaderHost.SetOuterScrollEnabled?.Invoke(false);

            ReadingCard.PropertyChanged += OnReadingCardPropertyChanged;
            UpdateReadingContentWidth(ReadingCard.Bounds.Width);
        }

        private void OnReaderUnloaded(object? sender, RoutedEventArgs e)
        {
            if (_readerLibraryPopup != null)
                _readerLibraryPopup.IsOpen = false;

            FontPanelPopup.IsOpen = false;
            ReadingCard.PropertyChanged -= OnReadingCardPropertyChanged;

            ReaderHost.SetOuterScrollEnabled?.Invoke(true);
        }

        private void OnReadingCardPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty)
                UpdateReadingContentWidth(ReadingCard.Bounds.Width);
        }

        private void UpdateReadingContentWidth(double cardWidth)
        {
            var available = cardWidth - ReadingCard.Padding.Left - ReadingCard.Padding.Right;
            var newWidth = Math.Clamp(available, MinReadingWidth, MaxReadingWidth);
            if (newWidth <= 0) return;

            ReadBlocksList.Width = newWidth;
            EditBlocksList.Width = newWidth;
        }

        private void LoadChapter()
        {
            _loadingChapter = true;

            using var db = OpenDb();

            _currentChapter = db.Chapters.FirstOrDefault(c => c.NovelId == _novelId && c.Number == _chapterNumber);
            var novel = db.Novels.Find(_novelId);

            if (_currentChapter == null)
            {
                NovelTitleText.Text = "";
                ChapterTitleText.Text = "Không tìm thấy chương này";
                ChapterHeadingText.Text = "";
                SetReaderBlocks("");
                _loadingChapter = false;
                return;
            }

            NovelTitleText.Text = novel?.DisplayTitle ?? novel?.Title ?? "";
            ChapterTitleText.Text = _currentChapter.DisplayTitle;
            ChapterHeadingText.Text = _currentChapter.DisplayTitle;

            SetPinIconState(_currentChapter.IsPinned);
            SetFavoriteIconState(novel?.IsFavorite ?? false);

            LoadCharacterLookup();

            SetReaderBlocks(BuildDisplayContentForTarget(EditTarget.Translated));
            TranslatingText.IsVisible = false;

            var chapters = db.Chapters.Where(c => c.NovelId == _novelId).OrderBy(c => c.Number).ToList();
            ChapterComboBox.ItemsSource = chapters;

            var selected = chapters.FirstOrDefault(c => c.Number == _chapterNumber);
            ChapterComboBox.SelectedItem = selected;

            var currentIndex = selected == null ? -1 : chapters.IndexOf(selected);
            PrevButton.IsEnabled = currentIndex > 0;
            NextButton.IsEnabled = currentIndex >= 0 && currentIndex < chapters.Count - 1;

            if (novel != null)
            {
                novel.LastReadChapterNumber = _currentChapter.Number;
                if (_currentChapter.Status == ChapterStatus.Unread)
                    _currentChapter.Status = ChapterStatus.Reading;
                db.SaveChanges();
            }

            ReaderScrollViewer.ScrollToHome();
            _loadingChapter = false;
        }

        private string BuildDisplayContentForTarget(EditTarget target)
        {
            if (_currentChapter == null) return "";

            if (target == EditTarget.Original)
                return _currentChapter.OriginalContent;

            return !string.IsNullOrWhiteSpace(_currentChapter.DisplayContent)
                ? _currentChapter.DisplayContent
                : "Chương này chưa có bản dịch hoàn chỉnh. Hãy dịch chương trước khi đọc.";
        }

        private void OnNovelTitleClick(object? sender, PointerPressedEventArgs e)
            => AppNavigator.NavigateTo(new NovelDetailPage(_novelId));

        private void OnChapterSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_loadingChapter) return;

            if (sender is ComboBox combo && combo.SelectedItem is Chapter selected && selected.Number != _chapterNumber)
            {
                _chapterNumber = selected.Number;
                LoadChapter();
            }
        }

        private void OnPrevClick(object? sender, RoutedEventArgs e) => GoToAdjacentChapter(-1);
        private void OnNextClick(object? sender, RoutedEventArgs e) => GoToAdjacentChapter(1);

        private void GoToAdjacentChapter(int step)
        {
            using var db = OpenDb();
            var chapters = db.Chapters.Where(c => c.NovelId == _novelId).OrderBy(c => c.Number).ToList();
            var currentIndex = chapters.FindIndex(c => c.Number == _chapterNumber);
            var targetIndex = currentIndex + step;

            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= chapters.Count)
                return;

            _chapterNumber = chapters[targetIndex].Number;
            LoadChapter();
        }

        private void SetReaderBlocks(string? content)
        {
            var parsed = ReaderBlock.Parse(content);
            _blocks.Clear();
            foreach (var block in parsed)
                _blocks.Add(ReaderBlockViewModel.FromBlock(block, isEditing: _isEditing));

            RebuildReadGroups();
            RebuildEditGroups();
            UpdateEditModeVisibility();
            ApplyReaderBackground(AppSettingsService.Instance.Settings.ReaderBackground);
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

        private void RebuildReadGroups() => BuildGroups(_blocks, _readGroups);
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

        private void UpdateEditModeVisibility()
        {
            ReadBlocksList.IsVisible = !_isEditing;
            EditBlocksList.IsVisible = _isEditing;
        }

        private string GetBlocksAsText() => ReaderBlock.Serialize(_blocks.Select(vm => vm.ToBlock()));

        private void ApplyReaderBackground(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) color = "#FFFDF8";

            var isDark = string.Equals(color, "#171717", StringComparison.OrdinalIgnoreCase);
            var isWhite = string.Equals(color, "#FFFFFF", StringComparison.OrdinalIgnoreCase);
            IBrush textForeground = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(51, 51, 51));
            IBrush borderSoft = Application.Current?.FindResource("BorderSoft") as IBrush ?? Brushes.LightGray;

            ReaderTopBar.Background = Brushes.White;
            ReaderTopBar.BorderBrush = borderSoft;

            if (isDark)
            {
                ReaderRoot.Background = Brushes.Black;
                ReadingCard.Background = Brushes.Black;
                ReadingCard.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55));
            }
            else if (isWhite)
            {
                ReaderRoot.Background = Brushes.White;
                ReadingCard.Background = Brushes.White;
                ReadingCard.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230));
            }
            else
            {
                ReaderRoot.Background = new SolidColorBrush(Color.FromRgb(247, 245, 240));
                ReadingCard.Background = new SolidColorBrush(Color.FromRgb(255, 253, 248));
                ReadingCard.BorderBrush = new SolidColorBrush(Color.FromRgb(232, 227, 217));
            }

            ReaderRoot.Resources["ReaderForegroundBrush"] = textForeground;
        }

        private ReaderDisplayGroup? _lastFocusedEditGroup;

        private void OnEditGroupTextBoxFocused(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ReaderDisplayGroup group)
            {
                _lastFocusedEditGroup = group;
                _activeEditTextBox = tb;
            }
        }

        private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
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
                var fileName = $"{_novelId}_{_chapterNumber}_{Guid.NewGuid():N}{extension}";
                var destPath = Path.Combine(imageDirectory, fileName);

                await using var sourceStream = await result[0].OpenReadAsync();
                await using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream);

                SyncBlocksFromEditGroups();

                var newBlock = new ReaderBlockViewModel
                {
                    Type = ReaderBlockType.Image,
                    ImagePath = destPath,
                    IsEditing = true
                };

                var insertIndex = _lastFocusedEditGroup != null
                    ? _lastFocusedEditGroup.EndBlockIndex + 1
                    : _blocks.Count;

                _blocks.Insert(Math.Clamp(insertIndex, 0, _blocks.Count), newBlock);
                RebuildReadGroups();
                RebuildEditGroups();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnInsertImageClick] Lỗi chèn ảnh: {ex}");
            }
        }

        private void OnRemoveImageGroupClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReaderDisplayGroup group || !group.IsImage) return;

            SyncBlocksFromEditGroups();

            if (group.StartBlockIndex >= 0 && group.StartBlockIndex < _blocks.Count)
                _blocks.RemoveAt(group.StartBlockIndex);

            RebuildReadGroups();
            RebuildEditGroups();
        }

        private string GetNovelImageDirectory()
        {
            var dbDirectory = AppSettingsService.Instance.Settings.DataFolder;
            var dir = Path.Combine(dbDirectory, "Images", $"Novel_{_novelId}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private async void OnEditTextBoxPasteCheck(object? sender, KeyEventArgs e)
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
                var destPath = Path.Combine(imageDirectory, $"{_novelId}_{_chapterNumber}_{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(destPath, bytes);

                SyncBlocksFromEditGroups();

                var newBlock = new ReaderBlockViewModel
                {
                    Type = ReaderBlockType.Image,
                    ImagePath = destPath,
                    IsEditing = true
                };

                var insertIndex = Math.Clamp(group.EndBlockIndex + 1, 0, _blocks.Count);
                _blocks.Insert(insertIndex, newBlock);
                RebuildReadGroups();
                RebuildEditGroups();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnEditTextBoxPasteCheck] Lỗi dán ảnh: {ex}");
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

        private void LoadEditTargetPreference()
        {
            using var db = OpenDb();
            var novel = db.Novels.Find(_novelId);
            var target = novel != null && novel.PreferredEditTarget == 1 ? EditTarget.Translated : EditTarget.Original;
            _editTarget = target;
            UpdateEditTargetButtons(target);
        }

        private void UpdateEditTargetButtons(EditTarget target)
        {
            EditTargetOriginalButton.Classes.Set("active", target == EditTarget.Original);
            EditTargetTranslatedButton.Classes.Set("active", target == EditTarget.Translated);
        }

        private void OnSetEditTargetOriginal(object? sender, RoutedEventArgs e)
        {
            SavePreferredEditTarget(EditTarget.Original);
            if (_isEditing) EnterEditMode(EditTarget.Original);
        }

        private void OnSetEditTargetTranslated(object? sender, RoutedEventArgs e)
        {
            SavePreferredEditTarget(EditTarget.Translated);
            if (_isEditing) EnterEditMode(EditTarget.Translated);
        }
        private void SavePreferredEditTarget(EditTarget target)
        {
            using var db = OpenDb();
            var novel = db.Novels.Find(_novelId);
            if (novel != null)
            {
                novel.PreferredEditTarget = (int)target;
                db.SaveChanges();
            }
            _editTarget = target;
            UpdateEditTargetButtons(target);
        }

        private void OnToggleEdit(object? sender, RoutedEventArgs e)
        {
            if (_isEditing) { _ = SaveEditAsync(); return; }
            EnterEditMode(_editTarget);
        }

        private void EnterEditMode(EditTarget target)
        {
            if (_currentChapter == null) return;

            _isEditing = true;
            _editTarget = target;

            SetReaderBlocks(target == EditTarget.Original ? _currentChapter.OriginalContent : _currentChapter.DisplayContent);

            EditButton.Classes.Set("active", true);
            ToolTip.SetTip(EditButton, target == EditTarget.Original ? "Lưu bản gốc (sẽ dịch lại)" : "Lưu bản dịch (không dịch lại)");
            InsertImageButton.IsVisible = true;
            BoldButton.IsVisible = ItalicButton.IsVisible = UnderlineButton.IsVisible = StrikeButton.IsVisible = true;

            ChapterTitleEditBox.Text = target == EditTarget.Original
                ? _currentChapter.Title
                : (string.IsNullOrWhiteSpace(_currentChapter.TranslatedTitle) ? _currentChapter.Title : _currentChapter.TranslatedTitle);
            ChapterTitleEditBox.IsVisible = true;
            ChapterHeadingText.IsVisible = false;
        }

        private void ExitEditMode()
        {
            _isEditing = false;
            EditButton.Classes.Set("active", false);
            ToolTip.SetTip(EditButton, "Sửa");
            InsertImageButton.IsVisible = false;
            BoldButton.IsVisible = ItalicButton.IsVisible = UnderlineButton.IsVisible = StrikeButton.IsVisible = false;
            ChapterTitleEditBox.IsVisible = false;
            ChapterHeadingText.IsVisible = true;
        }

        private async Task SaveEditAsync()
        {
            if (_editTarget == EditTarget.Original) await SaveOriginalEditAsync();
            else await SaveTranslatedEditAsync();
        }

        private async Task SaveOriginalEditAsync()
        {
            if (_currentChapter == null) return;

            SyncBlocksFromEditGroups();
            var newOriginalContent = GetBlocksAsText();
            if (string.IsNullOrWhiteSpace(newOriginalContent)) return;

            var newTitle = ChapterTitleEditBox.Text?.Trim() ?? _currentChapter.Title;
            var titleChanged = newTitle != _currentChapter.Title;

            var oldOriginalLines = SplitLines(_currentChapter.OriginalContent);
            var oldDisplayLines = SplitLines(_currentChapter.DisplayContent);
            var newOriginalLines = SplitLines(newOriginalContent);
            var newDisplayLines = new string[newOriginalLines.Length];
            var translationSucceeded = true;

            TranslatingText.Text = "Đang dịch lại chương...";
            TranslatingText.IsVisible = true;
            EditButton.IsEnabled = false;

            try
            {
                var translator = TranslationService.CreateFromSettings();
                var diff = DiffLines(oldOriginalLines, newOriginalLines);

                var linesToTranslate = diff.Count(d =>
                    d.NewIndex != null && d.OldIndex == null &&
                    Regex.IsMatch(newOriginalLines[d.NewIndex.Value], @"\p{IsCJKUnifiedIdeographs}"));
                var translatedSoFar = 0;

                foreach (var (oldIndex, newIndex) in diff)
                {
                    if (newIndex == null) continue;

                    if (oldIndex != null && oldIndex.Value < oldDisplayLines.Length)
                    {
                        newDisplayLines[newIndex.Value] = oldDisplayLines[oldIndex.Value];
                        continue;
                    }

                    var line = newOriginalLines[newIndex.Value];

                    if (!Regex.IsMatch(line, @"\p{IsCJKUnifiedIdeographs}"))
                    {
                        newDisplayLines[newIndex.Value] = line;
                        continue;
                    }

                    translatedSoFar++;
                    TranslatingText.Text = $"Đang dịch lại chương... ({translatedSoFar}/{linesToTranslate})";
                    var translated = await translator.TranslateChapterAsync(line);
                    newDisplayLines[newIndex.Value] = string.IsNullOrWhiteSpace(translated) ? line : translated;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOriginalEditAsync] Lỗi dịch lại chương: {ex}");
                translationSucceeded = false;
            }
            finally
            {
                TranslatingText.IsVisible = false;
                EditButton.IsEnabled = true;
            }

            var newDisplayContent = translationSucceeded
                ? string.Join("\n", newDisplayLines)
                : _currentChapter.DisplayContent;

            string newTranslatedTitle = _currentChapter.TranslatedTitle;
            if (titleChanged)
            {
                try
                {
                    var translator = TranslationService.CreateFromSettings();
                    var t = (await translator.TranslateChapterAsync(newTitle)).Trim();
                    newTranslatedTitle = string.IsNullOrWhiteSpace(t) ? newTitle : t;
                }
                catch { newTranslatedTitle = _currentChapter.TranslatedTitle; }
            }

            using var db = OpenDb();
            var chapterInDb = db.Chapters.First(c => c.Id == _currentChapter.Id);
            chapterInDb.OriginalContent = newOriginalContent;
            chapterInDb.Title = newTitle;
            if (titleChanged) chapterInDb.TranslatedTitle = newTranslatedTitle;
            chapterInDb.Status = ChapterStatus.Edited;
            chapterInDb.LastEditedAt = DateTime.UtcNow;
            chapterInDb.DisplayContent = newDisplayContent;
            db.SaveChanges();

            _currentChapter.OriginalContent = chapterInDb.OriginalContent;
            _currentChapter.DisplayContent = chapterInDb.DisplayContent;
            _currentChapter.Title = chapterInDb.Title;
            _currentChapter.TranslatedTitle = chapterInDb.TranslatedTitle;

            ExitEditMode();
            ChapterTitleText.Text = _currentChapter.DisplayTitle;
            SetReaderBlocks(_currentChapter.DisplayContent);
        }

        private async Task SaveTranslatedEditAsync()
        {
            if (_currentChapter == null) return;

            SyncBlocksFromEditGroups();
            var newDisplayContent = GetBlocksAsText();
            if (string.IsNullOrWhiteSpace(newDisplayContent)) return;

            var newTranslatedTitle = ChapterTitleEditBox.Text?.Trim() ?? _currentChapter.TranslatedTitle;

            using var db = OpenDb();
            var chapterInDb = db.Chapters.First(c => c.Id == _currentChapter.Id);
            chapterInDb.DisplayContent = newDisplayContent;
            chapterInDb.TranslatedTitle = newTranslatedTitle;
            chapterInDb.Status = ChapterStatus.Edited;
            chapterInDb.LastEditedAt = DateTime.UtcNow;
            db.SaveChanges();

            _currentChapter.DisplayContent = chapterInDb.DisplayContent;
            _currentChapter.TranslatedTitle = chapterInDb.TranslatedTitle;

            await Task.CompletedTask;
            ExitEditMode();
            ChapterTitleText.Text = _currentChapter.DisplayTitle;
            SetReaderBlocks(_currentChapter.DisplayContent);
        }

        private void OnBoldClick(object? sender, RoutedEventArgs e) => WrapActiveTextBox("b");
        private void OnItalicClick(object? sender, RoutedEventArgs e) => WrapActiveTextBox("i");
        private void OnUnderlineClick(object? sender, RoutedEventArgs e) => WrapActiveTextBox("u");
        private void OnStrikeClick(object? sender, RoutedEventArgs e) => WrapActiveTextBox("s");

        private void WrapActiveTextBox(string tag)
        {
            if (_activeEditTextBox == null) return;
            var text = _activeEditTextBox.Text ?? "";
            var (newText, newStart, newEnd) = ReaderRichText.WrapSelection(text, _activeEditTextBox.SelectionStart, _activeEditTextBox.SelectionEnd, tag);
            _activeEditTextBox.Text = newText;
            _activeEditTextBox.SelectionStart = newStart;
            _activeEditTextBox.SelectionEnd = newEnd;
        }
      
        private void HandleAddName(string? rawSelectedText, bool isOriginalSelection)
        {
            var selectedText = rawSelectedText?.Trim();
            if (string.IsNullOrWhiteSpace(selectedText) || _activeContextGroup == null || _activeContextGroup.IsImage)
                return;

            using var db = OpenDb();
            var set = db.NovelGlossarySets
                .Where(x => x.NovelId == _novelId)
                .Select(x => x.GlossarySet!)
                .OrderBy(x => x.Name)
                .FirstOrDefault();

            if (set == null)
            {
                _ = DialogService.ShowYesNoAsync(
                    "Truyện chưa có bộ tên nào được áp dụng. Hãy thêm bộ tên cho truyện trước.",
                    "Thêm tên");
                return;
            }

            if (isOriginalSelection)
            {
                var existing = db.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == set.Id && x.OriginalTerm == selectedText);
                ShowGlossaryEntryDialog(set.Id, selectedText, existing?.TranslatedTerm ?? "", existing?.HanViet ?? "",
                    oldTranslatedText: existing?.TranslatedTerm ?? "", blockIndex: -1);
            }
            else
            {
                var blockIndex = GetSelectionBlockIndex();
                var originalGuess = selectedText;

                var existing = db.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == set.Id && x.OriginalTerm == originalGuess);
                ShowGlossaryEntryDialog(set.Id, originalGuess, existing?.TranslatedTerm ?? selectedText, existing?.HanViet ?? "",
                    oldTranslatedText: selectedText, blockIndex: blockIndex ?? -1);
            }
        }

        private void OnEditTextBoxContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            _activeEditTextBox = tb;
            _activeContextGroup = tb.DataContext as ReaderDisplayGroup;
            _activeSelectionStartOffset = Math.Min(tb.SelectionStart, tb.SelectionEnd);
        }

        private void OnEditContextAddNameClick(object? sender, RoutedEventArgs e)
            => HandleAddName(_activeEditTextBox?.SelectedText, isOriginalSelection: _editTarget == EditTarget.Original);

        private void OnTextBlockContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (sender is not SelectableTextBlock stb) return;

            _activeContextTextBlock = stb;
            _activeContextGroup = stb.Tag as ReaderDisplayGroup;

            _activeSelectionStartOffset = Math.Min(stb.SelectionStart, stb.SelectionEnd);
        }

        private async void OnContextCopyClick(object? sender, RoutedEventArgs e)
        {
            var text = _activeContextTextBlock?.SelectedText;
            if (string.IsNullOrEmpty(text)) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
                await topLevel.Clipboard.SetTextAsync(text);
        }

        private void OnContextAddStyledClick(object? sender, RoutedEventArgs e)
            => HandleAddName(_activeContextTextBlock?.SelectedText, isOriginalSelection: false);

        private void OnContextAddCharacterClick(object? sender, RoutedEventArgs e)
        {
            var selectedText = _activeContextTextBlock?.SelectedText?.Trim();
            if (string.IsNullOrWhiteSpace(selectedText)) return;

            ModalService.Show(new ReaderAddCharacterModal(_novelId, selectedText, () => LoadCharacterLookup()));
        }

        private void ReplaceInDisplayContent(string oldText, string newText, bool wholeChapter, int? blockIndex)
        {
            if (_currentChapter == null || string.IsNullOrEmpty(oldText) || oldText == newText) return;

            using var db = OpenDb();
            var chapterInDb = db.Chapters.First(c => c.Id == _currentChapter.Id);
            var content = chapterInDb.DisplayContent ?? "";

            if (wholeChapter)
            {
                content = content.Replace(oldText, newText);
            }
            else if (blockIndex is int idx)
            {
                var lines = SplitLines(content);
                if (idx >= 0 && idx < lines.Length)
                {
                    var pos = lines[idx].IndexOf(oldText, StringComparison.Ordinal);
                    if (pos >= 0)
                        lines[idx] = lines[idx][..pos] + newText + lines[idx][(pos + oldText.Length)..];
                    content = string.Join("\n", lines);
                }
            }

            chapterInDb.DisplayContent = content;
            db.SaveChanges();

            _currentChapter.DisplayContent = content;
            SetReaderBlocks(_currentChapter.DisplayContent);
        }

        private static void ReplaceInNovelDisplayTitle(Novel novel, string oldText, string newText)
        {
            if (!string.IsNullOrWhiteSpace(novel.CustomTitle) &&
                novel.CustomTitle.Contains(oldText, StringComparison.Ordinal))
            {
                novel.CustomTitle = novel.CustomTitle.Replace(oldText, newText);
                return;
            }
            if (!string.IsNullOrWhiteSpace(novel.TranslatedTitle) &&
                novel.TranslatedTitle.Contains(oldText, StringComparison.Ordinal))
            {
                novel.TranslatedTitle = novel.TranslatedTitle.Replace(oldText, newText);
                return;
            }
            if (novel.Title.Contains(oldText, StringComparison.Ordinal))
                novel.Title = novel.Title.Replace(oldText, newText);
        }

        private static void ReplaceInChapterDisplayTitle(Chapter chapter, string oldText, string newText)
        {
            if (!string.IsNullOrWhiteSpace(chapter.TranslatedTitle) &&
                chapter.TranslatedTitle.Contains(oldText, StringComparison.Ordinal))
            {
                chapter.TranslatedTitle = chapter.TranslatedTitle.Replace(oldText, newText);
                return;
            }
            if (chapter.Title.Contains(oldText, StringComparison.Ordinal))
                chapter.Title = chapter.Title.Replace(oldText, newText);
        }

        private void ReplaceAcrossNovel(string oldText, string newText)
        {
            if (_currentChapter == null || string.IsNullOrEmpty(oldText) || oldText == newText) return;

            using var db = OpenDb();

            var novel = db.Novels.Find(_novelId);
            if (novel != null)
                ReplaceInNovelDisplayTitle(novel, oldText, newText);

            var chapters = db.Chapters.Where(c => c.NovelId == _novelId).ToList();

            foreach (var chapter in chapters)
            {
                ReplaceInChapterDisplayTitle(chapter, oldText, newText);

                var content = chapter.DisplayContent ?? "";
                if (content.Contains(oldText, StringComparison.Ordinal))
                {
                    chapter.DisplayContent = content.Replace(oldText, newText);
                }

                if (chapter.Id == _currentChapter.Id)
                {
                    _currentChapter.DisplayContent = chapter.DisplayContent ?? "";
                    _currentChapter.Title = chapter.Title;
                    _currentChapter.TranslatedTitle = chapter.TranslatedTitle;
                }
            }

            db.SaveChanges();

            NovelTitleText.Text = novel?.DisplayTitle ?? novel?.Title ?? NovelTitleText.Text;
            ChapterTitleText.Text = _currentChapter.DisplayTitle;
            ChapterHeadingText.Text = _currentChapter.DisplayTitle;

            var refreshedChapters = db.Chapters.Where(c => c.NovelId == _novelId).OrderBy(c => c.Number).ToList();
            ChapterComboBox.ItemsSource = refreshedChapters;
            ChapterComboBox.SelectedItem = refreshedChapters.FirstOrDefault(c => c.Number == _chapterNumber);

            SetReaderBlocks(_currentChapter.DisplayContent);
        }

        private const int OriginalGuessWindow = 6;

        private static bool IsBoundaryChar(char c)
        {
            if (char.IsWhiteSpace(c)) return true;
            if (char.IsLetterOrDigit(c)) return false;
            if (c >= 0x4E00 && c <= 0x9FFF) return false;
            if (c >= 0x3400 && c <= 0x4DBF) return false;
            if (c >= 0xF900 && c <= 0xFAFF) return false;
            return true;
        }

        private int? GetSelectionBlockIndex()
        {
            if (_activeContextGroup == null || _activeContextGroup.IsImage) return null;

            var fullText = _activeContextGroup.Text;
            var offset = Math.Clamp(_activeSelectionStartOffset, 0, fullText.Length);

            var lineOffset = fullText[..offset].Count(c => c == '\n');
            return Math.Clamp(
                _activeContextGroup.StartBlockIndex + lineOffset,
                _activeContextGroup.StartBlockIndex,
                _activeContextGroup.EndBlockIndex);
        }

        private static string? TryFindKnownTermNearSelection(MiaoDbContext db, Guid glossarySetId, string originalLine, int centerIndex)
        {
            var knownTerms = db.GlossarySetEntries
                .Where(x => x.GlossarySetId == glossarySetId)
                .Select(x => x.OriginalTerm)
                .ToList()
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .OrderByDescending(t => t.Length); 

            foreach (var term in knownTerms)
            {
                var searchFrom = 0;
                while (true)
                {
                    var idx = originalLine.IndexOf(term, searchFrom, StringComparison.Ordinal);
                    if (idx < 0) break;

                    if (centerIndex >= idx && centerIndex < idx + term.Length)
                        return term;

                    searchFrom = idx + 1;
                }
            }

            return null;
        }

        private string GetOriginalLineForSelection(int blockIndex, string groupText, string selectedText,
            Guid? glossarySetId = null, MiaoDbContext? db = null)
        {
            if (_currentChapter == null) return "";

            var originalLines = SplitLines(_currentChapter.OriginalContent);
            if (blockIndex < 0 || blockIndex >= originalLines.Length) return "";

            var originalLine = originalLines[blockIndex].Trim();
            if (originalLine.Length == 0) return "";

            var groupLines = groupText.Split('\n');
            var lineIndexInGroup = blockIndex - (_activeContextGroup?.StartBlockIndex ?? blockIndex);
            if (lineIndexInGroup < 0 || lineIndexInGroup >= groupLines.Length) return originalLine;

            var lineText = groupLines[lineIndexInGroup];
            if (lineText.Length == 0 || string.IsNullOrEmpty(selectedText)) return originalLine;

            var precedingLength = 0;
            for (var i = 0; i < lineIndexInGroup; i++)
                precedingLength += groupLines[i].Length + 1;

            var selectionStartOffset = Math.Clamp(_activeSelectionStartOffset - precedingLength, 0, Math.Max(0, lineText.Length - 1));

            var ratio = Math.Clamp((double)selectionStartOffset / lineText.Length, 0, 1);
            var centerIndex = Math.Clamp((int)(ratio * originalLine.Length), 0, originalLine.Length - 1);

            if (db != null && glossarySetId != null)
            {
                var knownMatch = TryFindKnownTermNearSelection(db, glossarySetId.Value, originalLine, centerIndex);
                if (!string.IsNullOrWhiteSpace(knownMatch))
                    return knownMatch;
            }

            var estimatedOriginalLength = Math.Clamp(
                (double)selectedText.Length / lineText.Length * originalLine.Length,
                1, originalLine.Length);
            var halfSpan = Math.Clamp((int)Math.Round(estimatedOriginalLength / 2.0), 2, OriginalGuessWindow);

            if (IsBoundaryChar(originalLine[centerIndex]))
            {
                var found = false;
                for (int d = 1; d <= halfSpan && !found; d++)
                {
                    if (centerIndex - d >= 0 && !IsBoundaryChar(originalLine[centerIndex - d]))
                    { centerIndex -= d; found = true; }
                    else if (centerIndex + d < originalLine.Length && !IsBoundaryChar(originalLine[centerIndex + d]))
                    { centerIndex += d; found = true; }
                }
                if (!found) return "";
            }

            var start = centerIndex;
            while (start > 0 && centerIndex - (start - 1) <= halfSpan && !IsBoundaryChar(originalLine[start - 1]))
                start--;

            var end = centerIndex;
            while (end < originalLine.Length - 1 && (end + 1) - centerIndex <= halfSpan && !IsBoundaryChar(originalLine[end + 1]))
                end++;

            return originalLine.Substring(start, end - start + 1).Trim();
        }

        private static string[] SplitLines(string? text)
            => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static List<(int? OldIndex, int? NewIndex)> DiffLines(string[] oldLines, string[] newLines)
        {
            int n = oldLines.Length, m = newLines.Length;
            var dp = new int[n + 1, m + 1];

            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = oldLines[i] == newLines[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var result = new List<(int?, int?)>();
            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (oldLines[a] == newLines[b]) { result.Add((a, b)); a++; b++; }
                else if (dp[a + 1, b] >= dp[a, b + 1]) { result.Add((a, null)); a++; }
                else { result.Add((null, b)); b++; }
            }
            while (a < n) { result.Add((a, null)); a++; }
            while (b < m) { result.Add((null, b)); b++; }

            return result;
        }

        private void ApplyFontSettings()
        {
            var s = AppSettingsService.Instance.Settings;
            if (s.ReaderFontSize <= 0) s.ReaderFontSize = 15;
            if (s.ReaderLineHeight <= 0) s.ReaderLineHeight = 1.5;
            if (string.IsNullOrWhiteSpace(s.ReaderFontFamily)) s.ReaderFontFamily = "Arial";
            if (string.IsNullOrWhiteSpace(s.ReaderBackground)) s.ReaderBackground = "#FFFDF8";

            _updatingReaderSettings = true;
            try
            {
                foreach (var obj in FontFamilyBox.Items)
                {
                    if (obj is ComboBoxItem item &&
                        string.Equals((string)item.Content!, s.ReaderFontFamily, StringComparison.OrdinalIgnoreCase))
                    {
                        FontFamilyBox.SelectedItem = item;
                        break;
                    }
                }

                FontSizeSlider.Value = Math.Clamp(s.ReaderFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
                LineHeightSlider.Value = Math.Clamp(s.ReaderLineHeight, LineHeightSlider.Minimum, LineHeightSlider.Maximum);

                foreach (var obj in ReaderBackgroundBox.Items)
                {
                    if (obj is ComboBoxItem item &&
                        string.Equals(item.Tag?.ToString(), s.ReaderBackground, StringComparison.OrdinalIgnoreCase))
                    {
                        ReaderBackgroundBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _updatingReaderSettings = false;
            }

            FontSizeValueText.Text = $"{s.ReaderFontSize:0} px";
            LineHeightValueText.Text = $"{s.ReaderLineHeight:0.0}×";

            ReadingCard.SetValue(TextElement.FontFamilyProperty, new FontFamily(s.ReaderFontFamily));
            ReadingCard.SetValue(TextElement.FontSizeProperty, s.ReaderFontSize);

            ReaderRoot.Resources["ReaderFontFamilyValue"] = new FontFamily(s.ReaderFontFamily);
            ReaderRoot.Resources["ReaderFontSizeValue"] = s.ReaderFontSize;

            _lineHeightMultiplier = s.ReaderLineHeight;
            UpdateComputedLineHeight();

            ApplyReaderBackground(s.ReaderBackground);
        }

        private double _lineHeightMultiplier = 1.5;

        private void UpdateComputedLineHeight()
        {
            var fontSize = AppSettingsService.Instance.Settings.ReaderFontSize;
            if (fontSize <= 0) fontSize = 15;

            ReaderRoot.Resources["ReaderLineHeightValue"] = fontSize * _lineHeightMultiplier;
        }

        private void OnToggleFontPanel(object? sender, RoutedEventArgs e)
        {
            FontPanelPopup.IsOpen = !FontPanelPopup.IsOpen;
        }

        private void OnFontChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingReaderSettings || FontFamilyBox.SelectedItem is not ComboBoxItem item) return;

            var family = (string)item.Content!;
            AppSettingsService.Instance.Settings.ReaderFontFamily = family;
            AppSettingsService.Instance.Save();

            ReadingCard.SetValue(TextElement.FontFamilyProperty, new FontFamily(family));
            ReaderRoot.Resources["ReaderFontFamilyValue"] = new FontFamily(family);
        }

        private void OnFontSizeChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (FontSizeValueText == null) return;

            FontSizeValueText.Text = $"{e.NewValue:0} px";
            if (_updatingReaderSettings) return;

            AppSettingsService.Instance.Settings.ReaderFontSize = e.NewValue;
            AppSettingsService.Instance.Save();

            ReadingCard.SetValue(TextElement.FontSizeProperty, e.NewValue);
            ReaderRoot.Resources["ReaderFontSizeValue"] = e.NewValue;
            UpdateComputedLineHeight();
        }

        private void OnLineHeightChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (LineHeightValueText == null) return;

            LineHeightValueText.Text = $"{e.NewValue:0.0}×";
            if (_updatingReaderSettings) return;

            AppSettingsService.Instance.Settings.ReaderLineHeight = e.NewValue;
            AppSettingsService.Instance.Save();

            _lineHeightMultiplier = e.NewValue;
            UpdateComputedLineHeight();
        }

        private void OnReaderBackgroundChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingReaderSettings || ReaderBackgroundBox.SelectedItem is not ComboBoxItem item) return;

            var color = item.Tag?.ToString() ?? "#FFFDF8";
            AppSettingsService.Instance.Settings.ReaderBackground = color;
            AppSettingsService.Instance.Save();
            ApplyReaderBackground(color);
        }

        private static IBrush GetAccentJadeBrush() =>
            (IBrush)(Application.Current?.FindResource("AccentJade") ?? Brushes.Teal);

        private void SetFavoriteIconState(bool isFavorite) =>
            FavoriteIconPath.Fill = isFavorite ? GetAccentJadeBrush() : Brushes.Transparent;

        private void SetPinIconState(bool isPinned) =>
            PinIconPath.Fill = isPinned ? GetAccentJadeBrush() : Brushes.Transparent;

        private void OnToggleFavorite(object? sender, RoutedEventArgs e)
        {
            using var db = OpenDb();
            var novel = db.Novels.Find(_novelId);
            if (novel == null) return;

            novel.IsFavorite = !novel.IsFavorite;
            db.SaveChanges();
            SetFavoriteIconState(novel.IsFavorite);
        }

        private void OnTogglePin(object? sender, RoutedEventArgs e)
        {
            if (_currentChapter == null) return;

            using var db = OpenDb();
            var chapterInDb = db.Chapters.First(c => c.Id == _currentChapter.Id);

            chapterInDb.IsPinned = !chapterInDb.IsPinned;
            db.SaveChanges();

            _currentChapter.IsPinned = chapterInDb.IsPinned;
            SetPinIconState(chapterInDb.IsPinned);
        }

        private void OnAddToLibraryReaderClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button addButton) return;
            if (_readerLibraryPopup != null)
                _readerLibraryPopup.IsOpen = false;

            var panel = new StackPanel();
            var borderSoft = (IBrush)(Application.Current?.FindResource("BorderSoft") ?? Brushes.LightGray);
            var accentJade = (IBrush)(Application.Current?.FindResource("AccentJade") ?? Brushes.Teal);

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = borderSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Width = 240,
                Child = panel
            };

            using (var db = OpenDb())
            {
                var addedLibraryIds = db.CustomLibraryNovels
                    .Where(x => x.NovelId == _novelId)
                    .Select(x => x.CustomLibraryId)
                    .ToHashSet();

                foreach (var library in db.CustomLibraries.OrderBy(x => x.Name).ToList())
                {
                    var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                    var nameText = new TextBlock { Text = library.Name, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(nameText, 0);
                    row.Children.Add(nameText);

                    if (addedLibraryIds.Contains(library.Id))
                    {
                        var check = new TextBlock
                        {
                            Text = "✓",
                            FontWeight = FontWeight.Bold,
                            Foreground = accentJade,
                            Margin = new Thickness(8, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(check, 1);
                        row.Children.Add(check);
                    }

                    var libraryButton = new Button
                    {
                        Content = row,
                        Tag = library.Id,
                        Classes = { "ReaderMenuItemButton" },
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    libraryButton.Click += OnReaderLibraryItemClick;
                    panel.Children.Add(libraryButton);
                }
            }

            panel.Children.Add(new Separator
            {
                Background = borderSoft,
                Height = 1,
                Margin = new Thickness(4, 3, 4, 3)
            });

            var createButton = new Button { Content = "+ Thêm danh sách mới", Classes = { "ReaderMenuItemButton" } };
            createButton.Click += OnReaderCreateLibraryClick;
            panel.Children.Add(createButton);

            _readerLibraryPopup = new Popup
            {
                PlacementTarget = addButton,
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true,
                Child = card,
                IsOpen = true
            };

            ((ISetLogicalParent)_readerLibraryPopup).SetParent(this);
        }

        private void OnReaderLibraryItemClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not Guid libraryId) return;

            using var db = OpenDb();
            var existing = db.CustomLibraryNovels
                .FirstOrDefault(x => x.CustomLibraryId == libraryId && x.NovelId == _novelId);

            if (existing != null)
                db.CustomLibraryNovels.Remove(existing);
            else
                db.CustomLibraryNovels.Add(new CustomLibraryNovel { CustomLibraryId = libraryId, NovelId = _novelId });

            db.SaveChanges();

            if (_readerLibraryPopup != null) _readerLibraryPopup.IsOpen = false;
        }

        private void OnReaderCreateLibraryClick(object? sender, RoutedEventArgs e)
        {
            if (_readerLibraryPopup != null) _readerLibraryPopup.IsOpen = false;

            var nameBox = new TextBox { FontFamily = "Arial", FontSize = 15, Margin = new Thickness(0, 0, 0, 18) };

            var addButton = new Button { Content = "Thêm", Width = 90, Classes = { "JadeButton" } };
            var cancelButton = new Button { Content = "Hủy", Width = 90, Margin = new Thickness(8, 0, 0, 0), Classes = { "SecondaryButton" } };
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            buttonRow.Children.Add(addButton);
            buttonRow.Children.Add(cancelButton);

            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = "Tên danh sách:", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(nameBox);
            panel.Children.Add(buttonRow);

            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                Width = 320,
                Child = panel
            };

            cancelButton.Click += (_, _) => ModalService.Close();
            addButton.Click += (_, _) =>
            {
                var name = nameBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                using var db = OpenDb();
                var library = db.CustomLibraries.FirstOrDefault(x => x.Name == name);
                if (library == null)
                {
                    library = new CustomLibrary { Name = name };
                    db.CustomLibraries.Add(library);
                    db.SaveChanges();
                }

                if (!db.CustomLibraryNovels.Any(x => x.CustomLibraryId == library.Id && x.NovelId == _novelId))
                {
                    db.CustomLibraryNovels.Add(new CustomLibraryNovel { CustomLibraryId = library.Id, NovelId = _novelId });
                    db.SaveChanges();
                }

                ModalService.Close();
            };

            ModalService.Show(card);
        }

        private static async Task RefineHanVietAsync(TextBox originalBox, TextBox hanVietBox)
        {
            var original = originalBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(original)) return;

            var accurate = await NameHanVietLookup.ToHanVietAsync(original);

            if (!string.IsNullOrWhiteSpace(accurate) && originalBox.Text == original)
                hanVietBox.Text = accurate;
        }

        private void ShowGlossaryEntryDialog(Guid glossarySetId, string selectedOriginal, string currentTranslation,
            string currentHanViet, string oldTranslatedText, int blockIndex)
        {
            var originalBox = new TextBox { Text = selectedOriginal, Margin = new Thickness(0, 0, 0, 14) };
            var hanVietText = string.IsNullOrWhiteSpace(currentHanViet) ? _sinoVietnamese.ToHanViet(selectedOriginal) : currentHanViet;
            var hanVietBox = new TextBox { Text = hanVietText, Margin = new Thickness(0, 0, 0, 14) };

            if (string.IsNullOrWhiteSpace(currentHanViet))
            {
                _ = RefineHanVietAsync(originalBox, hanVietBox);
            }

            var isFirstOriginalTextEvent = true;

            async void OnOriginalTextChanged(string? text)
            {
                if (isFirstOriginalTextEvent) { isFirstOriginalTextEvent = false; return; }

                var current = text ?? "";
                var quickGuess = _sinoVietnamese.ToHanViet(current);
                hanVietBox.Text = string.IsNullOrWhiteSpace(quickGuess) ? current : quickGuess;

                await RefineHanVietAsync(originalBox, hanVietBox);
            }

            originalBox.GetObservable(TextBox.TextProperty).Subscribe(
                new Avalonia.Reactive.AnonymousObserver<string?>(OnOriginalTextChanged));
            var translatedBox = new TextBox { Text = currentTranslation, Margin = new Thickness(0, 0, 0, 18) };

            var root = new StackPanel { Margin = new Thickness(24), Width = 360 };
            root.Children.Add(new TextBlock { Text = "Gốc:", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(originalBox);
            root.Children.Add(new TextBlock { Text = "Hán Việt:", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(hanVietBox);
            root.Children.Add(new TextBlock { Text = "Dịch:", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(translatedBox);

            Button MakeButton(string content, string styleClass) => new()
            {
                Content = content,
                Width = 100,
                Classes = { styleClass },
                Margin = new Thickness(5, 0, 5, 0)
            };

            var wholeNovelButton = MakeButton("Tất cả", "JadeButton");
            var chapterButton = MakeButton("1 chương", "SecondaryButton");
            var onceButton = MakeButton("1 lần", "SecondaryButton");

            var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
            scopeRow.Children.Add(wholeNovelButton);
            scopeRow.Children.Add(chapterButton);
            scopeRow.Children.Add(onceButton);

            var deleteButton = MakeButton("Xóa", "DangerButton");
            var cancelButton = MakeButton("Hủy", "SecondaryButton");
            var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            bottomRow.Children.Add(deleteButton);
            bottomRow.Children.Add(cancelButton);

            root.Children.Add(scopeRow);
            root.Children.Add(bottomRow);

            var card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Child = root };

            cancelButton.Click += (_, _) => ModalService.Close();

            void SaveGlossaryEntry(string newOriginal, string translated)
            {
                using var saveDb = OpenDb();
                var entry = saveDb.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == glossarySetId && x.OriginalTerm == selectedOriginal);
                if (entry == null)
                {
                    saveDb.GlossarySetEntries.Add(new GlossarySetEntry
                    {
                        GlossarySetId = glossarySetId,
                        OriginalTerm = newOriginal,
                        HanViet = hanVietBox.Text?.Trim() ?? "",
                        PinYin = _sinoVietnamese.ToPinYin(newOriginal),
                        TranslatedTerm = translated
                    });
                }
                else
                {
                    entry.OriginalTerm = newOriginal;
                    entry.TranslatedTerm = translated;
                    entry.HanViet = hanVietBox.Text?.Trim() ?? "";
                    entry.PinYin = _sinoVietnamese.ToPinYin(newOriginal);
                }
                saveDb.SaveChanges();
            }

            wholeNovelButton.Click += (_, _) =>
            {
                var newOriginal = originalBox.Text?.Trim() ?? "";
                var translated = translatedBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(newOriginal) || string.IsNullOrWhiteSpace(translated)) return;

                SaveGlossaryEntry(newOriginal, translated);
                ReplaceAcrossNovel(oldTranslatedText, translated);
                ModalService.Close();
            };

            chapterButton.Click += (_, _) =>
            {
                var translated = translatedBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(translated)) return;

                ReplaceInDisplayContent(oldTranslatedText, translated, wholeChapter: true, blockIndex: null);
                ModalService.Close();
            };

            onceButton.Click += (_, _) =>
            {
                var translated = translatedBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(translated)) return;

                ReplaceInDisplayContent(oldTranslatedText, translated, wholeChapter: false, blockIndex: blockIndex);
                ModalService.Close();
            };

            deleteButton.Click += async (_, _) =>
            {
                var result = await DialogService.ShowYesNoAsync(
                    "Xóa tên này sẽ dịch lại cụm từ gốc bằng máy dịch và thay thế trong TOÀN BỘ truyện đang dùng bộ tên này (không chỉ truyện đang đọc). Tiếp tục?",
                    "Xóa tên");
                if (result != DialogResult.Yes) return;

                using var deleteDb = OpenDb();
                var entry = deleteDb.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == glossarySetId && x.OriginalTerm == selectedOriginal);
                if (entry == null) { ModalService.Close(); return; }

                ModalService.Close();

                await GlossaryApplicationService.DeleteEntryAndRevertAsync(deleteDb, entry.Id);

                LoadChapter();
            };

            ModalService.Show(card);
        }

        private void LoadCharacterLookup()
        {
            _characterLookup = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var db = OpenDb();
                var scopeIds = CharacterService.GetEffectiveScanScopeCharacterIdsAsync(db, _novelId).Result;

                var characters = db.Characters
                    .Include(c => c.Aliases)
                    .Where(c => scopeIds.Contains(c.Id))
                    .ToList();

                foreach (var c in characters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && !_characterLookup.ContainsKey(c.Name))
                        _characterLookup[c.Name] = c;

                    foreach (var alias in c.Aliases.Where(a => a.IsEnabledForScan))
                    {
                        if (!string.IsNullOrWhiteSpace(alias.AliasText) && !_characterLookup.ContainsKey(alias.AliasText))
                            _characterLookup[alias.AliasText] = c;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadCharacterLookup] {ex}");
            }
        }

        private void OnParagraphDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not SelectableTextBlock stb) return;

            var word = stb.SelectedText?.Trim();
            if (string.IsNullOrWhiteSpace(word)) return;

            if (_characterLookup.TryGetValue(word, out var character))
                ShowCharacterFlyout(character, stb);
        }

        private void ShowCharacterFlyout(Character character, Control anchor)
        {
            var panel = new StackPanel { Margin = new Thickness(14), Width = 180, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

            if (!string.IsNullOrWhiteSpace(character.ImagePath) && File.Exists(character.ImagePath))
            {
                try
                {
                    panel.Children.Add(new Border
                    {
                        Width = 140, Height = 140, CornerRadius = new CornerRadius(10), ClipToBounds = true,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new Image { Source = new Avalonia.Media.Imaging.Bitmap(character.ImagePath), Stretch = Stretch.Uniform }
                    });
                }
                catch { /* ảnh lỗi/không đọc được -> bỏ qua, vẫn hiện tên */ }
            }

            panel.Children.Add(new TextBlock
            {
                Text = character.Name,
                FontWeight = FontWeight.Bold,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (IBrush)(Application.Current?.FindResource("BorderSoft") ?? Brushes.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = panel
            };

            var flyout = new Flyout { Content = card, Placement = PlacementMode.Pointer };
            flyout.ShowAt(anchor);
        }
    }
}