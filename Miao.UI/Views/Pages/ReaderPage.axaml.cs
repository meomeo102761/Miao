using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
using Microsoft.EntityFrameworkCore;
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

    // Một "khối hiển thị" ở chế độ đọc: hoặc là 1 đoạn văn bản đã GỘP nhiều dòng
    // (block) liên tiếp lại để có thể bôi đen/chọn liền mạch như Word, hoặc là 1 ảnh.
    // Đây thuần là dữ liệu hiển thị — dữ liệu gốc theo từng dòng vẫn nằm ở _blocks
    // để phục vụ Sửa bản gốc / gán glossary theo đúng dòng.
    public sealed class ReaderDisplayGroup
    {
        public bool IsImage { get; set; }
        public string Text { get; set; } = "";
        public string? ImagePath { get; set; }

        // Chỉ áp dụng khi IsImage == false: khoảng chỉ số dòng trong _blocks mà group này gộp lại
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

        private readonly SinoVietnameseConverter _sinoVietnamese;
        private ObservableCollection<ReaderBlockViewModel> _blocks = new();
        private ObservableCollection<ReaderDisplayGroup> _readGroups = new();

        // Khối text đang được thao tác chuột phải (Copy/Thêm/Thêm nhân vật) — mỗi
        // SelectableTextBlock ở chế độ đọc có ContextMenu RIÊNG (không dùng chung 1
        // StaticResource nữa) nên sự kiện Click luôn khớp đúng đoạn đang thao tác.
        private ReaderDisplayGroup? _activeContextGroup;
        private SelectableTextBlock? _activeContextTextBlock;

        // Tra cứu nhanh: tên/biệt danh nhân vật (không phân biệt hoa thường) -> Character,
        // dùng khi double-click vào 1 từ trong đoạn văn để hiện ảnh + tên nhân vật.
        private Dictionary<string, Character> _characterLookup = new(StringComparer.OrdinalIgnoreCase);

        public ReaderPage(Guid novelId, int chapterNumber, bool startInEditMode = false)
        {
            InitializeComponent();
            _novelId = novelId;
            _chapterNumber = chapterNumber;

            var handataPath = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "handata");
            var hanVietDictionaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translate", "zh_to_vi", "HanViet.json");
            _sinoVietnamese = new SinoVietnameseConverter(handataPath, hanVietDictionaryPath);

            EditBlocksList.ItemsSource = _blocks;
            ReadBlocksList.ItemsSource = _readGroups;

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

            ReadBlocksList.Width = newWidth;
            EditBlocksList.Width = newWidth;
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

            LoadCharacterLookup();

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

            RebuildReadGroups();
            UpdateEditModeVisibility();
            ApplyReaderBackground(AppSettingsService.Instance.Settings.ReaderBackground);
        }

        // Gộp các dòng văn bản liên tiếp (không phải ảnh) thành 1 khối hiển thị duy nhất
        // để người đọc bôi đen/chọn văn bản liền mạch qua nhiều dòng, giống trình soạn
        // thảo văn bản thông thường, thay vì bị chặn cứng trong từng dòng riêng lẻ.
        private void RebuildReadGroups()
        {
            _readGroups.Clear();

            var i = 0;
            while (i < _blocks.Count)
            {
                var block = _blocks[i];

                if (block.IsImage)
                {
                    _readGroups.Add(new ReaderDisplayGroup
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
                while (i < _blocks.Count && !_blocks[i].IsImage)
                {
                    lines.Add(_blocks[i].Text ?? "");
                    i++;
                }

                _readGroups.Add(new ReaderDisplayGroup
                {
                    IsImage = false,
                    Text = string.Join("\n", lines),
                    StartBlockIndex = start,
                    EndBlockIndex = i - 1
                });
            }
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

            // Cập nhật màu chữ qua DynamicResource -> mọi SelectableTextBlock/TextBox trong
            // vùng đọc (kể cả ở chế độ Sửa) tự đổi màu theo nền, không bị "chìm" khi chọn nền tối.
            ReaderRoot.Resources["ReaderForegroundBrush"] = textForeground;
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
                RebuildReadGroups();
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
            RebuildReadGroups();
        }

        // Thư mục ảnh dùng chung cho MỌI cách thêm ảnh (chọn file, dán clipboard...) —
        // tự tạo nếu chưa có, mỗi truyện 1 thư mục riêng "Images/Novel_{novelId}".
        private string GetNovelImageDirectory()
        {
            var dbDirectory = AppSettingsService.Instance.Settings.DataFolder;
            var dir = Path.Combine(dbDirectory, "Images", $"Novel_{_novelId}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Dán ảnh (Ctrl+V) trực tiếp vào ô đang sửa: nếu clipboard đang chứa ảnh (copy từ
        // trình duyệt/ứng dụng khác) thì lưu vào cùng thư mục ảnh của truyện và chèn thành
        // 1 block ảnh ngay sau dòng đang focus, thay vì dán chữ rác hoặc không làm gì.
        // Lưu ý: định dạng clipboard ảnh phụ thuộc hệ điều hành/phiên bản Avalonia — nếu
        // ở máy bạn danh sách format khác "image/png"/"PNG"/"Bitmap", báo mình để chỉnh lại.
        // Dán ảnh (Ctrl+V) trực tiếp vào ô đang sửa: nếu clipboard đang chứa ảnh (copy từ
        // trình duyệt/ứng dụng khác) thì lưu vào cùng thư mục ảnh của truyện và chèn thành
        // 1 block ảnh ngay sau dòng đang focus, thay vì dán chữ rác hoặc không làm gì.
        //
        // GetFormatsAsync/GetDataAsync chỉ có trên IClipboard từ Avalonia 11.1 trở lên, nên
        // gọi qua reflection để build được ở MỌI phiên bản Avalonia — nếu bản bạn đang dùng
        // không hỗ trợ, TryGetClipboardImageBytesAsync trả về null và hành vi dán chữ mặc
        // định vẫn hoạt động bình thường (không crash, chỉ là tính năng dán ảnh không chạy).
        private async void OnEditTextBoxPasteCheck(object? sender, KeyEventArgs e)
        {
            var isPaste = e.Key == Key.V &&
                (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
            if (!isPaste) return;
            if (sender is not TextBox tb || tb.DataContext is not ReaderBlockViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return;

            var bytes = await TryGetClipboardImageBytesAsync(clipboard);
            if (bytes == null || bytes.Length == 0) return; // không có ảnh -> để dán chữ mặc định

            e.Handled = true; // có ảnh: chặn hành vi dán chữ mặc định của TextBox

            try
            {
                var imageDirectory = GetNovelImageDirectory();
                var destPath = Path.Combine(imageDirectory, $"{_novelId}_{_chapterNumber}_{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(destPath, bytes);

                var newBlock = new ReaderBlockViewModel
                {
                    Type = ReaderBlockType.Image,
                    ImagePath = destPath,
                    IsEditing = true
                };

                var insertIndex = Math.Clamp(_blocks.IndexOf(vm) + 1, 0, _blocks.Count);
                _blocks.Insert(insertIndex, newBlock);
                RebuildReadGroups();
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

            // Áp trực tiếp font/cỡ chữ lên vùng đọc (thuộc tính TextElement được kế thừa
            // xuống mọi TextBlock/SelectableTextBlock/TextBox con bên trong ReadingCard).
            ReadingCard.SetValue(TextElement.FontFamilyProperty, new FontFamily(s.ReaderFontFamily));
            ReadingCard.SetValue(TextElement.FontSizeProperty, s.ReaderFontSize);

            _lineHeightMultiplier = s.ReaderLineHeight;
            UpdateComputedLineHeight();

            ApplyReaderBackground(s.ReaderBackground);
        }

        // LineHeight của Avalonia là giá trị PIXEL TUYỆT ĐỐI, không phải hệ số nhân — trước
        // đây gán thẳng "1.5" (hệ số) vào LineHeight khiến mỗi dòng cao đúng 1.5px và các
        // dòng chữ chồng khít lên nhau (chữ bị "rối"/đè nhau). Giờ tính LineHeight thực tế
        // = cỡ chữ hiện tại × hệ số giãn dòng, và tính lại mỗi khi 1 trong 2 giá trị đổi.
        private double _lineHeightMultiplier = 1.5;

        private void UpdateComputedLineHeight()
        {
            var fontSize = AppSettingsService.Instance.Settings.ReaderFontSize;
            if (fontSize <= 0) fontSize = 15;

            ReaderRoot.Resources["ReaderLineHeightValue"] = fontSize * _lineHeightMultiplier;
        }

        private void OnToggleFontPanel(object? sender, RoutedEventArgs e)
        {
            FontPanel.IsVisible = !FontPanel.IsVisible;
        }

        private void OnFontChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingReaderSettings || FontFamilyBox.SelectedItem is not ComboBoxItem item) return;

            var family = (string)item.Content!;
            AppSettingsService.Instance.Settings.ReaderFontFamily = family;
            AppSettingsService.Instance.Save();

            ReadingCard.SetValue(TextElement.FontFamilyProperty, new FontFamily(family));
        }

        private void OnFontSizeChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (FontSizeValueText == null) return;

            FontSizeValueText.Text = $"{e.NewValue:0} px";
            if (_updatingReaderSettings) return;

            AppSettingsService.Instance.Settings.ReaderFontSize = e.NewValue;
            AppSettingsService.Instance.Save();

            ReadingCard.SetValue(TextElement.FontSizeProperty, e.NewValue);
            UpdateComputedLineHeight(); // cỡ chữ đổi -> line height (px) cũng phải tính lại
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

                    // Nhấn lại vào 1 thư viện đã thêm sẽ BỎ CHỌN (xoá khỏi thư viện đó),
                    // thay vì trước đây bấm hoài không có phản ứng gì nếu đã tồn tại.
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
            var existing = db.CustomLibraryNovels
                .FirstOrDefault(x => x.CustomLibraryId == libraryId && x.NovelId == _novelId);

            if (existing != null)
                db.CustomLibraryNovels.Remove(existing); // bấm lại -> bỏ khỏi thư viện
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

        // ================= VÙNG ĐỌC: MENU CHUỘT PHẢI (Thêm / Thêm nhân vật / Copy) =================

        private void OnTextBlockContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (sender is not SelectableTextBlock stb) return;

            _activeContextTextBlock = stb;
            _activeContextGroup = stb.Tag as ReaderDisplayGroup;
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
            if (string.IsNullOrWhiteSpace(selectedText) || _activeContextGroup == null || _activeContextGroup.IsImage) return;

            var blockIndex = GetSelectionBlockIndex();
            if (blockIndex == null) return;

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

            var originalGuess = GetOriginalLineForSelection(blockIndex.Value, _activeContextGroup.Text, selectedText);
            if (string.IsNullOrWhiteSpace(originalGuess))
                originalGuess = selectedText;

            var existing = db.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == set.Id && x.OriginalTerm == originalGuess);
            ShowGlossaryEntryDialog(
                set.Id, originalGuess, existing?.TranslatedTerm ?? selectedText, existing?.HanViet ?? "",
                oldTranslatedText: selectedText, blockIndex: blockIndex.Value);
        }

        // Nhấn "Thêm nhân vật" trong menu chuột phải: mở hộp thoại tạo/gán nhân vật
        // cho đúng từ/cụm từ vừa bôi đen.
        private void OnContextAddCharacterClick(object? sender, RoutedEventArgs e)
        {
            var selectedText = _activeContextTextBlock?.SelectedText?.Trim();
            if (string.IsNullOrWhiteSpace(selectedText)) return;

            ShowAddCharacterDialog(selectedText);
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

        // Với chế độ đọc gộp nhiều dòng vào 1 khối, cần tính lại đúng "dòng" (block index
        // trong _blocks) mà đoạn đang được chọn thuộc về, dựa vào số ký tự xuống dòng '\n'
        // đứng trước vị trí bắt đầu của phần được bôi đen trong văn bản đã gộp.
        private int? GetSelectionBlockIndex()
        {
            if (_activeContextGroup == null || _activeContextGroup.IsImage) return null;

            var selected = _activeContextTextBlock?.SelectedText ?? "";
            var fullText = _activeContextGroup.Text;

            if (string.IsNullOrEmpty(selected))
                return _activeContextGroup.StartBlockIndex;

            var offset = fullText.IndexOf(selected, StringComparison.Ordinal);
            if (offset < 0)
                return _activeContextGroup.StartBlockIndex;

            var lineOffset = fullText[..offset].Count(c => c == '\n');
            return Math.Clamp(
                _activeContextGroup.StartBlockIndex + lineOffset,
                _activeContextGroup.StartBlockIndex,
                _activeContextGroup.EndBlockIndex);
        }

        private string GetOriginalLineForSelection(int blockIndex, string groupText, string selectedText)
        {
            if (_currentChapter == null) return "";

            var originalLines = SplitLines(_currentChapter.OriginalContent);
            if (blockIndex < 0 || blockIndex >= originalLines.Length) return "";

            var originalLine = originalLines[blockIndex].Trim();
            if (originalLine.Length == 0) return "";

            // Lấy đúng dòng con (trong nhóm đã gộp) tương ứng với blockIndex
            var groupLines = groupText.Split('\n');
            var lineIndexInGroup = blockIndex - (_activeContextGroup?.StartBlockIndex ?? blockIndex);
            var lineText = lineIndexInGroup >= 0 && lineIndexInGroup < groupLines.Length
                ? groupLines[lineIndexInGroup]
                : groupText;

            if (lineText.Length == 0 || string.IsNullOrEmpty(selectedText)) return originalLine;

            var selectionStartOffset = Math.Max(0, lineText.IndexOf(selectedText, StringComparison.Ordinal));
            var ratio = Math.Clamp((double)selectionStartOffset / lineText.Length, 0, 1);
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

        // Tra Hán Việt chính xác hơn (khớp cụm tên riêng qua Name.json) trong nền
        // rồi mới cập nhật ô Hán Việt — chỉ áp kết quả nếu người dùng chưa gõ
        // tiếp sang nội dung khác trong lúc chờ (tránh ghi đè nhầm).
        private static async Task RefineHanVietAsync(TextBox originalBox, TextBox hanVietBox)
        {
            var original = originalBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(original)) return;

            var accurate = await NameHanVietLookup.ToHanVietAsync(original);

            if (!string.IsNullOrWhiteSpace(accurate) && originalBox.Text == original)
                hanVietBox.Text = accurate;
        }

        // Dialog thêm/sửa tên qua ModalService thay cho Window riêng của WPF
        private void ShowGlossaryEntryDialog(Guid glossarySetId, string selectedOriginal, string currentTranslation,
            string currentHanViet, string oldTranslatedText, int blockIndex)
        {
            var originalBox = new TextBox { Text = selectedOriginal, Margin = new Thickness(0, 0, 0, 14) };
            var hanVietText = string.IsNullOrWhiteSpace(currentHanViet) ? _sinoVietnamese.ToHanViet(selectedOriginal) : currentHanViet;
            var hanVietBox = new TextBox { Text = hanVietText, Margin = new Thickness(0, 0, 0, 14) };
            var pinYinBox = new TextBox { Text = _sinoVietnamese.ToPinYin(selectedOriginal), Margin = new Thickness(0, 0, 0, 14) };

            if (string.IsNullOrWhiteSpace(currentHanViet))
            {
                _ = RefineHanVietAsync(originalBox, hanVietBox);
            }

            async void OnOriginalTextChanged(string? text)
            {
                var current = text ?? "";
                var quickGuess = _sinoVietnamese.ToHanViet(current);
                hanVietBox.Text = string.IsNullOrWhiteSpace(quickGuess) ? current : quickGuess;
                pinYinBox.Text = _sinoVietnamese.ToPinYin(current);

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
            root.Children.Add(new TextBlock { Text = "Bính âm:", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(pinYinBox);
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
                var pinYin = string.IsNullOrWhiteSpace(pinYinBox.Text?.Trim())
                    ? _sinoVietnamese.ToPinYin(newOriginal)
                    : pinYinBox.Text!.Trim();

                using var saveDb = OpenDb();
                var entry = saveDb.GlossarySetEntries.FirstOrDefault(x => x.GlossarySetId == glossarySetId && x.OriginalTerm == selectedOriginal);
                if (entry == null)
                {
                    saveDb.GlossarySetEntries.Add(new GlossarySetEntry
                    {
                        GlossarySetId = glossarySetId,
                        OriginalTerm = newOriginal,
                        HanViet = hanVietBox.Text?.Trim() ?? "",
                        PinYin = pinYin,
                        TranslatedTerm = translated
                    });
                }
                else
                {
                    entry.OriginalTerm = newOriginal;
                    entry.TranslatedTerm = translated;
                    entry.HanViet = hanVietBox.Text?.Trim() ?? "";
                    entry.PinYin = pinYin;
                }
                saveDb.SaveChanges();
            }

            wholeNovelButton.Click += (_, _) =>
            {
                var newOriginal = originalBox.Text?.Trim() ?? "";
                var translated = translatedBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(newOriginal) || string.IsNullOrWhiteSpace(translated)) return;

                SaveGlossaryEntry(newOriginal, translated);
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

        // ================= NHÂN VẬT: NHẬN DIỆN TÊN + BẤM VÀO ĐỂ XEM ẢNH =================
        //
        // GIẢ ĐỊNH schema (dựa trên 3 model Character / CharacterAlias / CharacterGroup bạn gửi):
        //   - MiaoDbContext có DbSet<CharacterGroup> CharacterGroups, DbSet<Character> Characters,
        //     DbSet<CharacterAlias> CharacterAliases. Nếu tên DbSet trong project khác, đổi lại
        //     3 chỗ dùng "db.CharacterGroups / db.Characters / db.CharacterAliases" bên dưới.
        //   - Nhân vật thuộc về truyện hiện tại nếu: CharacterGroup.OwnerNovelId == _novelId
        //     (bộ riêng của truyện) HOẶC CharacterGroup.IsShared == true (bộ dùng chung, tương
        //     tự cách GlossarySet dùng NovelGlossarySets). Nếu thực tế có bảng nối kiểu
        //     "NovelCharacterGroups" riêng, thay điều kiện Where bên dưới cho khớp.

        private void LoadCharacterLookup()
        {
            _characterLookup = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var db = OpenDb();
                var characters = db.Characters
                    .Include(c => c.Aliases)
                    .Include(c => c.CharacterGroup)
                    .Where(c => c.CharacterGroup != null &&
                                (c.CharacterGroup.OwnerNovelId == _novelId || c.CharacterGroup.IsShared))
                    .ToList();

                foreach (var c in characters)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && !_characterLookup.ContainsKey(c.Name))
                        _characterLookup[c.Name] = c;

                    foreach (var alias in c.Aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(alias.AliasText) && !_characterLookup.ContainsKey(alias.AliasText))
                            _characterLookup[alias.AliasText] = c;
                    }
                }
            }
            catch (Exception ex)
            {
                // Nếu DbSet Characters/CharacterAliases/CharacterGroups chưa tồn tại trong
                // MiaoDbContext, tính năng nhận diện nhân vật sẽ tự tắt thay vì làm crash trang đọc.
                System.Diagnostics.Debug.WriteLine($"[LoadCharacterLookup] {ex}");
            }
        }

        // Double-click vào 1 từ trong đoạn văn: SelectableTextBlock tự chọn từ dưới con trỏ,
        // nếu từ đó khớp tên/biệt danh nhân vật đã biết -> hiện ảnh + tên trong 1 flyout nhỏ.
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
            var panel = new StackPanel { Margin = new Thickness(14), Width = 200 };

            if (!string.IsNullOrWhiteSpace(character.ImagePath) && File.Exists(character.ImagePath))
            {
                try
                {
                    panel.Children.Add(new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(character.ImagePath),
                        Height = 160,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 10)
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

            if (!string.IsNullOrWhiteSpace(character.Description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = character.Description,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = (IBrush)(Application.Current?.FindResource("TextMuted") ?? Brushes.Gray)
                });
            }

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

        // Hộp thoại "Thêm nhân vật" — tạo nhân vật mới (hoặc gán alias cho nhân vật đã có)
        // gắn với đúng cụm từ vừa bôi đen trong văn bản.
        private void ShowAddCharacterDialog(string suggestedName)
        {
            var nameBox = new TextBox { Text = suggestedName, Margin = new Thickness(0, 0, 0, 14) };
            var descBox = new TextBox { AcceptsReturn = true, Height = 70, Margin = new Thickness(0, 0, 0, 14) };

            string? pickedImagePath = null;
            var previewImage = new Image { Height = 120, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 10), IsVisible = false };
            var pickImageButton = new Button { Content = "Chọn ảnh nhân vật", Classes = { "SecondaryButton" }, Margin = new Thickness(0, 0, 0, 14) };

            pickImageButton.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is null) return;

                var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Chọn ảnh nhân vật",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
                });
                if (result is null || result.Count == 0) return;

                var imageDirectory = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "Images", "Characters");
                Directory.CreateDirectory(imageDirectory);
                var destPath = Path.Combine(imageDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(result[0].Name)}");

                await using var sourceStream = await result[0].OpenReadAsync();
                await using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream);

                pickedImagePath = destPath;
                previewImage.Source = new Avalonia.Media.Imaging.Bitmap(destPath);
                previewImage.IsVisible = true;
            };

            var saveButton = new Button { Content = "Lưu", Width = 100, Classes = { "JadeButton" }, Margin = new Thickness(5, 0, 5, 0) };
            var cancelButton = new Button { Content = "Hủy", Width = 100, Classes = { "SecondaryButton" }, Margin = new Thickness(5, 0, 5, 0) };
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            buttonRow.Children.Add(saveButton);
            buttonRow.Children.Add(cancelButton);

            var root = new StackPanel { Margin = new Thickness(24), Width = 320 };
            root.Children.Add(new TextBlock { Text = "Tên nhân vật:", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(nameBox);
            root.Children.Add(new TextBlock { Text = "Mô tả (không bắt buộc):", FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(descBox);
            root.Children.Add(previewImage);
            root.Children.Add(pickImageButton);
            root.Children.Add(buttonRow);

            var card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Child = root };

            cancelButton.Click += (_, _) => ModalService.Close();
            saveButton.Click += (_, _) =>
            {
                var name = nameBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                try
                {
                    using var db = OpenDb();

                    // Dùng (hoặc tạo) 1 CharacterGroup riêng của truyện này để lưu nhân vật mới
                    var group = db.CharacterGroups.FirstOrDefault(g => g.OwnerNovelId == _novelId);
                    if (group == null)
                    {
                        group = new CharacterGroup { OwnerNovelId = _novelId, Name = "Nhân vật truyện" };
                        db.CharacterGroups.Add(group);
                        db.SaveChanges();
                    }

                    var character = db.Characters.FirstOrDefault(c => c.CharacterGroupId == group.Id && c.Name == name);
                    var isNewCharacter = character == null;
                    character ??= new Character { CharacterGroupId = group.Id, Name = name };

                    character.Description = descBox.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(pickedImagePath))
                        character.ImagePath = pickedImagePath;

                    if (isNewCharacter)
                        db.Characters.Add(character);

                    db.SaveChanges();

                    // Nếu cụm từ bôi đen khác tên chính -> lưu thành 1 cách gọi khác (alias)
                    if (!string.Equals(character.Name, suggestedName, StringComparison.OrdinalIgnoreCase) &&
                        !db.CharacterAliases.Any(a => a.CharacterId == character.Id && a.AliasText == suggestedName))
                    {
                        db.CharacterAliases.Add(new CharacterAlias { CharacterId = character.Id, AliasText = suggestedName });
                        db.SaveChanges();
                    }

                    LoadCharacterLookup();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ShowAddCharacterDialog] {ex}");
                }

                ModalService.Close();
            };

            ModalService.Show(card);
        }
    }
}