using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
using Avalonia.VisualTree;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;
using Miao.UI.Views.Pages.Reader;

namespace Miao.UI.Views.Pages
{
    // Hook nhẹ để MainView (chưa dựng) có thể tắt/bật thanh cuộn ngoài khi vào/ra
    // trang đọc — thay cho việc ReaderPage tham chiếu thẳng tới MainWindow.Current
    // như bản WPF cũ (Core/UI không nên biết trực tiếp về khung ngoài).
    public static class ReaderHost
    {
        public static Action<bool>? SetOuterScrollEnabled;
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

        private readonly SinoVietnameseConverter _sinoVietnamese;
        private ObservableCollection<ReaderBlockViewModel> _blocks = new();

        // Khối text đang được thao tác chuột phải (Copy/Thêm) — thay cho việc
        // ContentContextMenuOpening của WPF gắn liền 1 RichTextBox duy nhất.
        private ReaderBlockViewModel? _activeContextBlock;
        private SelectableTextBlock? _activeContextTextBlock;

        public ReaderPage(Guid novelId, int chapterNumber, bool startInEditMode = false)
        {
            InitializeComponent();
            _novelId = novelId;
            _chapterNumber = chapterNumber;

            var handataPath = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "handata");
            _sinoVietnamese = new SinoVietnameseConverter(handataPath);

            BlocksList.ItemsSource = _blocks;

            ApplyFontSettings();
            LoadChapter();

            if (startInEditMode)
                EnterEditMode();
        }

        private static MiaoDbContext OpenDb() =>
            new(Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "miao.db"));

        // ================= VÒNG ĐỜI TRANG =================

        private void OnReaderLoaded(object? sender, RoutedEventArgs e)
        {
            ReaderHost.SetOuterScrollEnabled?.Invoke(false);

            // Avalonia: dùng Tunnel để bắt sự kiện trước khi con xử lý, tương đương
            // PreviewMouseDown của WPF — dùng để tự đóng FontPanel khi bấm ra ngoài.
            ReaderRoot.AddHandler(InputElement.PointerPressedEvent, OnReaderRootPointerDown,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);

            ReadingCard.PropertyChanged += OnReadingCardPropertyChanged;
            UpdateReadingContentWidth(ReadingCard.Bounds.Width);
        }

        private void OnReaderUnloaded(object? sender, RoutedEventArgs e)
        {
            if (_readerLibraryPopup != null)
                _readerLibraryPopup.IsOpen = false;

            ReaderRoot.RemoveHandler(InputElement.PointerPressedEvent, OnReaderRootPointerDown);
            ReadingCard.PropertyChanged -= OnReadingCardPropertyChanged;

            ReaderHost.SetOuterScrollEnabled?.Invoke(true);
        }

        private void OnReadingCardPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty)
                UpdateReadingContentWidth(ReadingCard.Bounds.Width);
        }

        private void UpdateReadingContentWidth(double cardWidth)
        {
            var available = cardWidth - ReadingCard.Padding.Left - ReadingCard.Padding.Right;
            var newWidth = Math.Clamp(available, MinReadingWidth, MaxReadingWidth);
            if (newWidth <= 0) return;

            BlocksList.Width = newWidth;
        }

        private void OnReaderRootPointerDown(object? sender, PointerPressedEventArgs e)
        {
            if (!FontPanel.IsVisible) return;

            if (e.Source is Visual sourceVisual &&
                (sourceVisual == FontPanel || sourceVisual.GetVisualAncestors().Contains(FontPanel)))
                return;

            FontPanel.IsVisible = false;
        }

        // ================= TẢI / ĐIỀU HƯỚNG CHƯƠNG =================

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

            PinButton.Content = _currentChapter.IsPinned ? "📌✓" : "📌";
            FavoriteButton.Content = (novel?.IsFavorite ?? false) ? "★" : "☆";

            var hasTranslation = !string.IsNullOrWhiteSpace(_currentChapter.DisplayContent);
            SetReaderBlocks(hasTranslation
                ? _currentChapter.DisplayContent
                : "Chương này chưa có bản dịch hoàn chỉnh. Hãy dịch chương trước khi đọc.");

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

        // ================= HIỂN THỊ NỘI DUNG (block-based, thay FlowDocument) =================

        private void SetReaderBlocks(string? content)
        {
            var parsed = ReaderBlock.Parse(content);
            _blocks.Clear();
            foreach (var block in parsed)
                _blocks.Add(ReaderBlockViewModel.FromBlock(block, isEditing: _isEditing));

            ApplyReaderBackground(AppSettingsService.Instance.Settings.ReaderBackground);
        }

        private string GetBlocksAsText() => ReaderBlock.Serialize(_blocks.Select(vm => vm.ToBlock()));

        private IBrush GetReaderForeground()
        {
            var background = AppSettingsService.Instance.Settings.ReaderBackground;
            return string.Equals(background, "#171717", StringComparison.OrdinalIgnoreCase)
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(51, 51, 51));
        }

        private void ApplyReaderBackground(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) color = "#FFFDF8";

            var isDark = string.Equals(color, "#171717", StringComparison.OrdinalIgnoreCase);
            var isWhite = string.Equals(color, "#FFFFFF", StringComparison.OrdinalIgnoreCase);
            IBrush headingForeground = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(51, 51, 51));
            IBrush borderSoft = Application.Current?.FindResource("BorderSoft") as IBrush ?? Brushes.LightGray;

            ReaderTopBar.Background = Brushes.White;
            ReaderBottomBar.Background = Brushes.White;
            ReaderTopBar.BorderBrush = borderSoft;
            ReaderBottomBar.BorderBrush = borderSoft;

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

            ChapterHeadingText.Foreground = headingForeground;
        }

        // ================= CHÈN / XOÁ ẢNH TRONG CHẾ ĐỘ SỬA =================

        private ReaderBlockViewModel? _lastFocusedTextBlock;

        private void OnBlockTextBoxFocused(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ReaderBlockViewModel vm)
                _lastFocusedTextBlock = vm;
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
                Directory.CreateDirectory(imageDirectory);

                var extension = Path.GetExtension(result[0].Name);
                var fileName = $"{_novelId}_{_chapterNumber}_{Guid.NewGuid():N}{extension}";
                var destPath = Path.Combine(imageDirectory, fileName);

                await using var sourceStream = await result[0].OpenReadAsync();
                await using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream);

                var newBlock = new ReaderBlockViewModel
                {
                    Type = ReaderBlockType.Image,
                    ImagePath = destPath,
                    IsEditing = true
                };

                var insertIndex = _lastFocusedTextBlock != null
                    ? _blocks.IndexOf(_lastFocusedTextBlock) + 1
                    : _blocks.Count;

                _blocks.Insert(Math.Clamp(insertIndex, 0, _blocks.Count), newBlock);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnInsertImageClick] Lỗi chèn ảnh: {ex}");
            }
        }

        private void OnRemoveImageBlockClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReaderBlockViewModel vm) return;
            _blocks.Remove(vm);
        }

        private string GetNovelImageDirectory()
        {
            var dbDirectory = AppSettingsService.Instance.Settings.DataFolder;
            return Path.Combine(dbDirectory, "Images", $"Novel_{_novelId}");
        }

        // ================= SỬA BẢN GỐC (dịch lại khi lưu) =================

        private async void OnToggleEdit(object? sender, RoutedEventArgs e)
        {
            if (_isEditing)
                await SaveEditAsync();
            else
                EnterEditMode();
        }

        private void EnterEditMode()
        {
            if (_currentChapter == null) return;

            _isEditing = true;
            SetReaderBlocks(_currentChapter.OriginalContent);
            EditButton.Content = "💾";
            ToolTip.SetTip(EditButton, "Lưu bản gốc");
            InsertImageButton.IsVisible = true;
        }

        private async Task SaveEditAsync()
        {
            if (_currentChapter == null) return;

            var newOriginalContent = GetBlocksAsText();
            if (string.IsNullOrWhiteSpace(newOriginalContent)) return;

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
                System.Diagnostics.Debug.WriteLine($"[SaveEditAsync] Lỗi dịch lại chương: {ex}");
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

            using var db = OpenDb();
            var chapterInDb = db.Chapters.First(c => c.Id == _currentChapter.Id);

            chapterInDb.OriginalContent = newOriginalContent;
            chapterInDb.Status = ChapterStatus.Edited;
            chapterInDb.LastEditedAt = DateTime.UtcNow;
            chapterInDb.DisplayContent = newDisplayContent;

            db.SaveChanges();

            _currentChapter.OriginalContent = chapterInDb.OriginalContent;
            _currentChapter.DisplayContent = chapterInDb.DisplayContent;

            _isEditing = false;
            EditButton.Content = "✏";
            ToolTip.SetTip(EditButton, "Sửa bản gốc");
            InsertImageButton.IsVisible = false;

            SetReaderBlocks(_currentChapter.DisplayContent);
        }

        private static string[] SplitLines(string? text)
            => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static System.Collections.Generic.List<(int? OldIndex, int? NewIndex)> DiffLines(string[] oldLines, string[] newLines)
        {
            int n = oldLines.Length, m = newLines.Length;
            var dp = new int[n + 1, m + 1];

            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = oldLines[i] == newLines[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var result = new System.Collections.Generic.List<(int?, int?)>();
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

        // ================= BẢNG TUỲ CHỈNH FONT / NỀN ĐỌC =================

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
            ApplyReaderBackground(s.ReaderBackground);
        }

        private void OnToggleFontPanel(object? sender, RoutedEventArgs e)
        {
            FontPanel.IsVisible = !FontPanel.IsVisible;
        }

        private void OnFontChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingReaderSettings || FontFamilyBox.SelectedItem is not ComboBoxItem item) return;

            AppSettingsService.Instance.Settings.ReaderFontFamily = (string)item.Content!;
            AppSettingsService.Instance.Save();
        }

        private void OnFontSizeChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (FontSizeValueText == null) return;

            FontSizeValueText.Text = $"{e.NewValue:0} px";
            if (_updatingReaderSettings) return;

            AppSettingsService.Instance.Settings.ReaderFontSize = e.NewValue;
            AppSettingsService.Instance.Save();
        }

        private void OnLineHeightChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (LineHeightValueText == null) return;

            LineHeightValueText.Text = $"{e.NewValue:0.0}×";
            if (_updatingReaderSettings) return;

            AppSettingsService.Instance.Settings.ReaderLineHeight = e.NewValue;
            AppSettingsService.Instance.Save();
        }

        private void OnReaderBackgroundChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingReaderSettings || ReaderBackgroundBox.SelectedItem is not ComboBoxItem item) return;

            var color = item.Tag?.ToString() ?? "#FFFDF8";
            AppSettingsService.Instance.Settings.ReaderBackground = color;
            AppSettingsService.Instance.Save();
            ApplyReaderBackground(color);
        }

        // ================= YÊU THÍCH / GHIM =================

        private void OnToggleFavorite(object? sender, RoutedEventArgs e)
        {
            using var db = OpenDb();
            var novel = db.Novels.Find(_novelId);
            if (novel == null) return;

            novel.IsFavorite = !novel.IsFavorite;
            db.SaveChanges();
            FavoriteButton.Content = novel.IsFavorite ? "★" : "☆";
        }

        private void OnTogglePin(object? sender, RoutedEventArgs e)
        {
            if (_currentChapter == null) return;

            using var db = OpenDb();
            var chapterInDb = db.Chapters.First(c => c.Id == _currentChapter.Id);

            chapterInDb.IsPinned = !chapterInDb.IsPinned;
            db.SaveChanges();

            _currentChapter.IsPinned = chapterInDb.IsPinned;
            PinButton.Content = chapterInDb.IsPinned ? "📌✓" : "📌";
        }

        // ================= THÊM VÀO THƯ VIỆN (Avalonia Popup) =================

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
                CornerRadius = new CornerRadius(8),
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

            panel.Children.Add(new Separator { Margin = new Thickness(4, 3, 4, 3) });

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
            if (!db.CustomLibraryNovels.Any(x => x.CustomLibraryId == libraryId && x.NovelId == _novelId))
            {
                db.CustomLibraryNovels.Add(new CustomLibraryNovel { CustomLibraryId = libraryId, NovelId = _novelId });
                db.SaveChanges();
            }

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

        // ================= VÙNG ĐỌC: MENU CHUỘT PHẢI (Copy / Thêm tên) =================

        private void OnTextBlockContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (sender is not SelectableTextBlock stb) return;

            _activeContextTextBlock = stb;
            _activeContextBlock = stb.Tag as ReaderBlockViewModel;
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
        {
            var selectedText = _activeContextTextBlock?.SelectedText?.Trim();
            if (string.IsNullOrWhiteSpace(selectedText) || _activeContextBlock == null) return;

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

            var originalGuess = _isEditing ? selectedText : GetOriginalLineForSelection();
            if (string.IsNullOrWhiteSpace(originalGuess))
                originalGuess = selectedText;

            var translatedGuess = _isEditing ? "" : selectedText;
            var blockIndex = _blocks.IndexOf(_activeContextBlock);

            var existing = db.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == set.Id && x.OriginalTerm == originalGuess);
            ShowGlossaryEntryDialog(
                set.Id, originalGuess, existing?.TranslatedTerm ?? translatedGuess, existing?.HanViet ?? "",
                applyToCurrentChapter: !_isEditing, oldTranslatedText: selectedText, blockIndex: blockIndex);
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

        private string GetOriginalLineForSelection()
        {
            if (_currentChapter == null || _activeContextBlock == null) return "";

            var index = _blocks.IndexOf(_activeContextBlock);
            if (index < 0) return "";

            var originalLines = SplitLines(_currentChapter.OriginalContent);
            if (index >= originalLines.Length) return "";

            var originalLine = originalLines[index].Trim();
            if (originalLine.Length == 0) return "";

            var paragraphText = _activeContextBlock.Text ?? "";
            var selectedText = _activeContextTextBlock?.SelectedText ?? "";
            if (paragraphText.Length == 0 || string.IsNullOrEmpty(selectedText)) return originalLine;

            var selectionStartOffset = Math.Max(0, paragraphText.IndexOf(selectedText, StringComparison.Ordinal));
            var ratio = Math.Clamp((double)selectionStartOffset / paragraphText.Length, 0, 1);
            var centerIndex = Math.Clamp((int)(ratio * originalLine.Length), 0, originalLine.Length - 1);

            if (IsBoundaryChar(originalLine[centerIndex]))
            {
                var found = false;
                for (int d = 1; d <= OriginalGuessWindow && !found; d++)
                {
                    if (centerIndex - d >= 0 && !IsBoundaryChar(originalLine[centerIndex - d]))
                    { centerIndex -= d; found = true; }
                    else if (centerIndex + d < originalLine.Length && !IsBoundaryChar(originalLine[centerIndex + d]))
                    { centerIndex += d; found = true; }
                }
                if (!found) return "";
            }

            var start = centerIndex;
            while (start > 0 && centerIndex - (start - 1) <= OriginalGuessWindow && !IsBoundaryChar(originalLine[start - 1]))
                start--;

            var end = centerIndex;
            while (end < originalLine.Length - 1 && (end + 1) - centerIndex <= OriginalGuessWindow && !IsBoundaryChar(originalLine[end + 1]))
                end++;

            return originalLine.Substring(start, end - start + 1).Trim();
        }

        // Dialog thêm/sửa tên qua ModalService thay cho Window riêng của WPF
        private void ShowGlossaryEntryDialog(Guid glossarySetId, string selectedOriginal, string currentTranslation,
            string currentHanViet, bool applyToCurrentChapter, string oldTranslatedText, int? blockIndex)
        {
            var originalBox = new TextBox { Text = selectedOriginal, Margin = new Thickness(0, 0, 0, 14) };
            var hanVietText = string.IsNullOrWhiteSpace(currentHanViet) ? _sinoVietnamese.ToHanViet(selectedOriginal) : currentHanViet;
            var hanVietBox = new TextBox { Text = hanVietText, Margin = new Thickness(0, 0, 0, 14) };
            originalBox.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(text =>
            {
                var converted = _sinoVietnamese.ToHanViet(text ?? "");
                hanVietBox.Text = string.IsNullOrWhiteSpace(converted) ? text : converted;
            }));
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

            chapterButton.IsVisible = applyToCurrentChapter;
            onceButton.IsVisible = applyToCurrentChapter;

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

                if (applyToCurrentChapter)
                    ReplaceInDisplayContent(oldTranslatedText, translated, wholeChapter: true, blockIndex: null);

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

            deleteButton.Click += (_, _) =>
            {
                using var deleteDb = OpenDb();
                var entry = deleteDb.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == glossarySetId && x.OriginalTerm == selectedOriginal);
                if (entry != null)
                {
                    deleteDb.GlossarySetEntries.Remove(entry);
                    deleteDb.SaveChanges();
                }
                ModalService.Close();
            };

            ModalService.Show(card);
        }
    }
}
