using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

using TranslationService = Miao.Core.Services.TranslationService;

namespace Miao.UI.Views.Pages
{
    public partial class DownloadPage : UserControl
    {
        private readonly IPageFetcher _browser = PlatformServices.PageFetcher;
        private readonly IScreenshotFetcher _screenshotFetcher = PlatformServices.ScreenshotFetcher;
        private readonly List<IDownloadSource> _sources;
        private IDownloadSource? _activeSource;
        private readonly TranslationService _titleTranslator = new(new CTranslate2Provider());
        private readonly TranslationService _contentTranslator = new(new CTranslate2Provider());

        private readonly ObservableCollection<ChapterCheckItem> _chapterItems = new();
        private string _novelTitle = "";
        private string _novelTitleDisplay = "";
        private string _novelAuthor = "";
        private string _coverImageUrl = "";
        private string _novelDescription = "";
        private string? _lastErrorLogPath;

        public DownloadPage()
        {
            InitializeComponent();

            var tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            var ocr = new OcrService(tessdataPath);

            _sources = new List<IDownloadSource>
            {
                new Sixty9ShubaDownloadSource(_browser),
                new FanqieDownloadSource(_browser, _screenshotFetcher, ocr),
                new BiqugeDownloadSource(_browser),
                new JinjiangDownloadSource(_browser),
                new LofterDownloadSource(),
                new WikidichDownloadSource(_browser),
                //new Novel543DownloadSource(_browser)
            };

            ChaptersList.ItemsSource = _chapterItems;
        }

        // ================== Fetch danh sách chương ==================

        private async void OnFetchClick(object? sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                StatusText.Text = "Chưa nhập link.";
                return;
            }

            _activeSource = _sources.FirstOrDefault(s => s.CanHandle(url));
            if (_activeSource == null)
            {
                var supported = string.Join(", ", _sources.Select(s => s.SourceName));
                StatusText.Text = $"Link này chưa được hỗ trợ (hiện hỗ trợ: {supported}).";
                return;
            }

            StatusText.Text = "Đang tải danh sách chương...";
            _chapterItems.Clear();
            LofterOptionsPanel.IsVisible = false;
            LofterNewTitleBox.Text = "";
            FetchButton.IsEnabled = false;

            try
            {
                var info = await _activeSource.GetNovelInfoAsync(url);
                _novelTitle = string.IsNullOrWhiteSpace(info.Title) ? "(Không rõ tên)" : info.Title;
                _novelAuthor = info.Author;
                _coverImageUrl = info.CoverImageUrl;
                _novelDescription = info.Description;

                _novelTitleDisplay = _novelTitle;
                if (!_activeSource.ProvidesTranslatedContent &&
                    System.Text.RegularExpressions.Regex.IsMatch(_novelTitle, @"\p{IsCJKUnifiedIdeographs}"))
                {
                    try
                    {
                        var t = (await _titleTranslator.TranslateChapterAsync(_novelTitle)).Trim();
                        if (!string.IsNullOrWhiteSpace(t))
                            _novelTitleDisplay = t;
                    }
                    catch
                    {
                        // Dịch lỗi thì tạm hiện tên gốc, không chặn việc tải danh sách chương.
                    }
                }

                _novelDescription = info.Description;

                var list = await _activeSource.GetChapterListAsync(url);
                var index = 0;

                foreach (var (number, title, chapterUrl) in list)
                {
                    _chapterItems.Add(new ChapterCheckItem
                    {
                        Index = ++index,
                        Number = number,
                        Title = title,
                        ChapterUrl = chapterUrl,
                        TranslatedTitle = "Đang dịch..."
                    });
                }

                StatusText.Text = _chapterItems.Count == 0
                    ? "Không tìm thấy chương nào."
                    : $"{_novelTitleDisplay} — tìm thấy {_chapterItems.Count} chương.";

                MarkAlreadyDownloadedChapters();
                SetupLofterOptionsIfNeeded(url);

                _ = TranslateTitlesInBackgroundAsync(_chapterItems.ToList());
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Lỗi: {ex.Message}";
            }
            finally
            {
                FetchButton.IsEnabled = true;
            }
        }

        private async Task TranslateTitlesInBackgroundAsync(List<ChapterCheckItem> items)
        {
            if (_activeSource == null) return;

            if (_activeSource.ProvidesTranslatedContent)
            {
                foreach (var item in items)
                    item.TranslatedTitle = item.Title;
                return;
            }

            foreach (var item in items)
            {
                try
                {
                    var translated = await _titleTranslator.TranslateChapterAsync(item.Title);
                    // Dispatcher.UIThread.InvokeAsync thay cho Dispatcher.InvokeAsync của WPF
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        item.TranslatedTitle = string.IsNullOrWhiteSpace(translated) ? item.Title : translated.Trim());
                }
                catch
                {
                    await Dispatcher.UIThread.InvokeAsync(() => item.TranslatedTitle = item.Title);
                }
            }
        }

        private void MarkAlreadyDownloadedChapters()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var urls = _chapterItems.Select(c => c.ChapterUrl).ToList();

            var downloaded = db.Chapters
                .Where(c => urls.Contains(c.SourceUrl))
                .Select(c => new { c.SourceUrl, c.NovelId })
                .ToList();

            if (downloaded.Count == 0) return;

            var novelIds = downloaded.Select(d => d.NovelId).Distinct().ToList();
            var novelTitles = db.Novels
                .Where(n => novelIds.Contains(n.Id))
                .ToDictionary(n => n.Id, n => n.DisplayTitle);

            foreach (var item in _chapterItems)
            {
                var match = downloaded.FirstOrDefault(d => d.SourceUrl == item.ChapterUrl);
                if (match == null) continue;

                item.IsAlreadyDownloaded = true;
                item.IsSelected = false;
                item.AlreadyDownloadedLabel = novelTitles.TryGetValue(match.NovelId, out var t)
                    ? $"Đã có trong: {t}"
                    : "Đã tải trước đó";
            }
        }

        private void SetupLofterOptionsIfNeeded(string blogUrl)
        {
            var isLofter = _activeSource is LofterDownloadSource;
            LofterOptionsPanel.IsVisible = isLofter;
            if (!isLofter) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var existingNovels = db.Novels
                .Where(n => db.NovelSources.Any(s => s.NovelId == n.Id && s.Url == blogUrl))
                .ToList();

            LofterExistingNovelsCombo.ItemsSource = existingNovels;
            LofterExistingNovelsCombo.SelectedIndex = existingNovels.Count > 0 ? 0 : -1;
            LofterExistingNovelRadio.IsEnabled = existingNovels.Count > 0;

            if (existingNovels.Count == 0)
                LofterNewNovelRadio.IsChecked = true;
        }

        // ================== Chọn / lọc chương ==================

        private void OnApplyRangeClick(object? sender, RoutedEventArgs e)
        {
            if (!int.TryParse(FromChapterBox.Text?.Trim(), out var from)) from = int.MinValue;
            if (!int.TryParse(ToChapterBox.Text?.Trim(), out var to)) to = int.MaxValue;

            foreach (var item in _chapterItems)
                item.IsSelected = !item.IsAlreadyDownloaded && item.Number >= from && item.Number <= to;
        }

        private void OnSelectAllClick(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _chapterItems.Where(i => !i.IsAlreadyDownloaded))
                item.IsSelected = true;
        }

        private void OnDeselectAllClick(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _chapterItems)
                item.IsSelected = false;
        }

        private void OnLofterTargetChanged(object? sender, RoutedEventArgs e)
        {
            if (LofterNewNovelRadio == null || LofterExistingNovelRadio == null ||
                LofterNewTitleBox == null || LofterExistingNovelsCombo == null)
                return;

            var createNew = LofterNewNovelRadio.IsChecked == true;
            var hasExisting = LofterExistingNovelRadio.IsEnabled;

            LofterNewTitleBox.IsEnabled = createNew;
            LofterExistingNovelsCombo.IsEnabled = !createNew && hasExisting;
        }

        private void ChaptersScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            var delta = e.Delta.Y;
            var canScrollUp = delta > 0 && scrollViewer.Offset.Y > 0;
            var canScrollDown = delta < 0 && scrollViewer.Offset.Y < scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
            if (!canScrollUp && !canScrollDown) return;

            scrollViewer.Offset = scrollViewer.Offset.WithY(scrollViewer.Offset.Y - delta * 40);
            e.Handled = true;
        }

        // ================== Tải chương ==================

        private async void OnDownloadClick(object? sender, RoutedEventArgs e)
        {
            if (_activeSource == null || _chapterItems.Count == 0) return;

            var selected = _chapterItems.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Chưa chọn chương nào.";
                return;
            }

            DownloadButton.IsEnabled = false;

            try
            {
                var sourceUrlTrimmed = UrlTextBox.Text?.Trim() ?? "";
                var isLofter = _activeSource is LofterDownloadSource;
                Novel novel;
                bool isNewNovel;

                if (isLofter)
                {
                    if (LofterExistingNovelRadio.IsChecked == true &&
                        LofterExistingNovelsCombo.SelectedItem is Novel selectedExisting)
                    {
                        novel = selectedExisting;
                        isNewNovel = false;

                        using var db = new MiaoDbContext(AppPaths.DbFilePath);
                        if (!db.NovelSources.Any(s => s.NovelId == novel.Id && s.Url == sourceUrlTrimmed))
                        {
                            db.NovelSources.Add(new NovelSource
                            {
                                NovelId = novel.Id,
                                SourceName = _activeSource.SourceName,
                                Url = sourceUrlTrimmed,
                                IsPrimary = false
                            });
                            db.SaveChanges();
                        }
                    }
                    else
                    {
                        var newTitle = LofterNewTitleBox.Text?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(newTitle))
                        {
                            StatusText.Text = "Nhập tên truyện trước khi tải (Lofter không tự xác định được tên tác phẩm).";
                            return;
                        }

                        novel = CreateNovelEntity(sourceUrlTrimmed, newTitle);
                        isNewNovel = true;
                    }
                }
                else
                {
                    // Kiểm tra trùng lặp TRƯỚC, không mở DbContext trong lúc chờ dialog xác nhận
                    // (khác code WPF gốc — MessageBox.Show cũ chặn cả luồng UI nên giữ DB mở không sao,
                    // còn dialog bất đồng bộ ở đây có thể chờ lâu, giữ DB mở lâu dễ gây lỗi "database locked").
                    var normalizedNewTitle = NormalizeTitle(_novelTitle);
                    Novel? exactUrlMatch;
                    Novel? possibleDuplicate;

                    using (var checkDb = new MiaoDbContext(AppPaths.DbFilePath))
                    {
                        exactUrlMatch = checkDb.Novels.FirstOrDefault(n => n.SourceUrl == sourceUrlTrimmed);
                        possibleDuplicate = exactUrlMatch == null
                            ? checkDb.Novels.ToList().FirstOrDefault(n =>
                                NormalizeTitle(n.Title) == normalizedNewTitle ||
                                NormalizeTitle(n.DisplayTitle) == normalizedNewTitle)
                            : null;
                    }

                    if (exactUrlMatch != null)
                    {
                        StatusText.Text = $"Truyện này đã có trong thư viện: \"{exactUrlMatch.DisplayTitle}\" (đúng link cũ). Hãy dùng chức năng cập nhật trong trang chi tiết truyện thay vì tải lại từ đầu.";
                        return;
                    }

                    if (possibleDuplicate != null)
                    {
                        var choice = await DialogService.ShowYesNoCancelAsync(
                            $"Đã có truyện trùng tên trong thư viện: \"{possibleDuplicate.DisplayTitle}\".\n\n" +
                            "Chọn CÓ để gộp link này làm NGUỒN PHỤ cho truyện đã có.\n" +
                            "Chọn KHÔNG nếu đây thực ra là 2 truyện khác nhau trùng tên.\n" +
                            "Chọn HỦY để dừng lại.",
                            "Phát hiện truyện trùng tên");

                        if (choice == DialogResult.Cancel)
                        {
                            StatusText.Text = "Đã hủy.";
                            return;
                        }

                        if (choice == DialogResult.Yes)
                        {
                            novel = possibleDuplicate;
                            isNewNovel = false;

                            using var db = new MiaoDbContext(AppPaths.DbFilePath);
                            if (!db.NovelSources.Any(s => s.NovelId == novel.Id && s.Url == sourceUrlTrimmed))
                            {
                                var setPrimaryChoice = await DialogService.ShowYesNoAsync(
                                    "Đặt nguồn này làm NGUỒN CHÍNH (ưu tiên dùng khi cập nhật chương sau này)?",
                                    "Nguồn chính");
                                var setPrimary = setPrimaryChoice == DialogResult.Yes;

                                if (setPrimary)
                                    foreach (var s in db.NovelSources.Where(s => s.NovelId == novel.Id))
                                        s.IsPrimary = false;

                                db.NovelSources.Add(new NovelSource
                                {
                                    NovelId = novel.Id,
                                    SourceName = _activeSource.SourceName,
                                    Url = sourceUrlTrimmed,
                                    IsPrimary = setPrimary
                                });
                                db.SaveChanges();
                            }
                        }
                        else
                        {
                            novel = CreateNovelEntity(sourceUrlTrimmed);
                            isNewNovel = true;
                        }
                    }
                    else
                    {
                        novel = CreateNovelEntity(sourceUrlTrimmed);
                        isNewNovel = true;
                    }
                }

                if (isNewNovel)
                {
                    using var db = new MiaoDbContext(AppPaths.DbFilePath);
                    db.Novels.Add(novel);
                    db.SaveChanges();

                    GlossarySetService.CreateDefaultForNovel(db, novel);

                    db.NovelSources.Add(new NovelSource
                    {
                        NovelId = novel.Id,
                        SourceName = _activeSource.SourceName,
                        Url = sourceUrlTrimmed,
                        IsPrimary = true
                    });
                    db.SaveChanges();

                    novel.CoverImagePath = await SaveNovelCoverAsync(novel.Id, _coverImageUrl);
                    db.SaveChanges();
                }

                if (isNewNovel && !_activeSource.ProvidesTranslatedContent)
                    await TranslateNovelTitleAndAuthorAsync(novel);

                var (added, updated, translated, translationFailed, failed) =
                    await DownloadChaptersAsync(novel, selected);

                var summary = isNewNovel
                    ? $"Hoàn tất — đã lưu \"{novel.DisplayTitle}\": {added} chương vào Thư viện."
                    : $"Hoàn tất — đã gộp vào \"{novel.DisplayTitle}\": {added} chương mới, {updated} chương được cập nhật nội dung.";

                var detail = failed == 0 && translationFailed == 0
                    ? $"{summary} Đã dịch {translated} chương."
                    : $"{summary} Đã dịch {translated} chương; {translationFailed} chương dịch lỗi, {failed} chương tải lỗi/nội dung rỗng.";

                StatusText.Text = _lastErrorLogPath == null
                    ? detail
                    : $"{detail}\nChi tiết lỗi từng chương: {_lastErrorLogPath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Lỗi khi tải/dịch: {ex.Message}";
            }
            finally
            {
                DownloadButton.IsEnabled = true;
            }
        }

        private async Task TranslateNovelTitleAndAuthorAsync(Novel novel)
        {
            string? translatedNovelTitle = null;
            string? translatedNovelAuthor = null;

            try
            {
                translatedNovelTitle = (await _titleTranslator.TranslateChapterAsync(novel.Title)).Trim();
            }
            catch
            {
                // Không để lỗi dịch tiêu đề làm hỏng việc tải truyện.
            }

            if (!string.IsNullOrWhiteSpace(novel.Author))
            {
                try
                {
                    translatedNovelAuthor = (await _titleTranslator.TranslateChapterAsync(novel.Author)).Trim();
                }
                catch
                {
                    // Không để lỗi dịch tác giả làm hỏng việc tải truyện.
                }
            }

            if (string.IsNullOrWhiteSpace(translatedNovelTitle) && string.IsNullOrWhiteSpace(translatedNovelAuthor))
                return;

            using var titleDb = new MiaoDbContext(AppPaths.DbFilePath);
            var savedNovel = titleDb.Novels.FirstOrDefault(n => n.Id == novel.Id);
            if (savedNovel == null) return;

            if (!string.IsNullOrWhiteSpace(translatedNovelTitle))
            {
                savedNovel.TranslatedTitle = translatedNovelTitle;
                novel.TranslatedTitle = translatedNovelTitle;
                _novelTitleDisplay = translatedNovelTitle;
            }

            if (!string.IsNullOrWhiteSpace(translatedNovelAuthor))
            {
                savedNovel.TranslatedAuthor = translatedNovelAuthor;
                novel.TranslatedAuthor = translatedNovelAuthor;
            }

            titleDb.SaveChanges();
        }

        private async Task<(int added, int updated, int translated, int translationFailed, int failed)> DownloadChaptersAsync(
            Novel novel, List<ChapterCheckItem> selected)
        {
            int done = 0, failed = 0, translated = 0, translationFailed = 0, updated = 0, added = 0;
            var errorLog = new List<string>();

            foreach (var item in selected)
            {
                StatusText.Text = $"Đang tải chương {item.Number} ({++done}/{selected.Count})...";

                string content;
                try
                {
                    content = await _activeSource!.GetChapterContentAsync(item.ChapterUrl);
                }
                catch (Exception ex)
                {
                    failed++;
                    errorLog.Add($"Chương {item.Number} ({item.Title}): {ex.Message}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    failed++;
                    errorLog.Add($"Chương {item.Number} ({item.Title}): nội dung rỗng không rõ lý do.");
                    continue;
                }

                var translatedTitle = item.Title;
                if (!_activeSource!.ProvidesTranslatedContent)
                {
                    try
                    {
                        var title = (await _titleTranslator.TranslateChapterAsync(item.Title)).Trim();
                        if (!string.IsNullOrWhiteSpace(title))
                            translatedTitle = title;
                    }
                    catch
                    {
                        // Giữ nguyên tiêu đề gốc nếu dịch lỗi.
                    }
                }

                var displayContent = content;
                var needsTranslation = !_activeSource.ProvidesTranslatedContent &&
                    System.Text.RegularExpressions.Regex.IsMatch(content, @"\p{IsCJKUnifiedIdeographs}");

                if (needsTranslation)
                {
                    StatusText.Text = $"Đang dịch chương {item.Number} ({done}/{selected.Count})...";
                    try
                    {
                        var imagePlaceholders = new List<string>();
                        var contentForTranslation = System.Text.RegularExpressions.Regex.Replace(
                            content,
                            @"^\[\[IMG:.+?\]\]$",
                            m =>
                            {
                                imagePlaceholders.Add(m.Value);
                                return $"IMGPLACEHOLDER{imagePlaceholders.Count - 1}";
                            },
                            System.Text.RegularExpressions.RegexOptions.Multiline);

                        var result = await _contentTranslator.TranslateChapterAsync(contentForTranslation);
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            for (var i = 0; i < imagePlaceholders.Count; i++)
                                result = result.Replace($"IMGPLACEHOLDER{i}", imagePlaceholders[i]);

                            displayContent = result;
                            translated++;
                        }
                        else
                        {
                            translationFailed++;
                            errorLog.Add($"Chương {item.Number} ({item.Title}): dịch trả về rỗng.");
                        }
                    }
                    catch (Exception ex)
                    {
                        translationFailed++;
                        errorLog.Add($"Chương {item.Number} ({item.Title}): lỗi dịch — {ex.Message}");
                    }
                }

                using var chapterDb = new MiaoDbContext(AppPaths.DbFilePath);
                var existingChapter = chapterDb.Chapters.FirstOrDefault(c => c.NovelId == novel.Id && c.Number == item.Number);

                if (existingChapter != null)
                {
                    existingChapter.Title = item.Title;
                    existingChapter.TranslatedTitle = translatedTitle;
                    existingChapter.SourceUrl = item.ChapterUrl;
                    existingChapter.OriginalContent = content;
                    existingChapter.DisplayContent = displayContent;
                    existingChapter.LastEditedAt = DateTime.Now;
                    updated++;
                }
                else
                {
                    chapterDb.Chapters.Add(new Chapter
                    {
                        NovelId = novel.Id,
                        Number = item.Number,
                        Title = item.Title,
                        TranslatedTitle = translatedTitle,
                        SourceUrl = item.ChapterUrl,
                        OriginalContent = content,
                        DisplayContent = displayContent
                    });
                    added++;
                }

                chapterDb.SaveChanges();
            }

            if (errorLog.Count > 0)
            {
                var logPath = Path.Combine(
                    AppSettingsService.Instance.Settings.DataFolder,
                    $"download-errors-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                try
                {
                    File.WriteAllLines(logPath, errorLog);
                    _lastErrorLogPath = logPath;
                }
                catch
                {
                    _lastErrorLogPath = null;
                }
            }
            else
            {
                _lastErrorLogPath = null;
            }

            return (added, updated, translated, translationFailed, failed);
        }

        // ================== Bìa truyện ==================

        private async Task<string> SaveNovelCoverAsync(Guid novelId, string? coverUrl)
        {
            var coverFolder = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "Covers");
            Directory.CreateDirectory(coverFolder);

            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    var response = await client.GetAsync(coverUrl);
                    response.EnsureSuccessStatusCode();

                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var extension = Path.GetExtension(new Uri(coverUrl).AbsolutePath);

                    if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5 ||
                        !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        extension = ".jpg";
                    }

                    var coverPath = Path.Combine(coverFolder, $"{novelId}{extension}");
                    await File.WriteAllBytesAsync(coverPath, bytes);
                    return coverPath;
                }
                catch
                {
                    // Tải bìa thất bại → dùng bìa mặc định.
                }
            }

            return Path.Combine(AppContext.BaseDirectory, "Assets", "default-cover.jpg");
        }

        // ================== Helpers ==================

        private Novel CreateNovelEntity(string sourceUrl, string? title = null) => new()
        {
            Title = string.IsNullOrWhiteSpace(title) ? _novelTitle : title,
            Author = _novelAuthor,
            SourceUrl = sourceUrl,
            CoverImagePath = "",
            Tags = "",
            Description = _novelDescription,
            IsDownloaded = true
        };

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";

            var normalized = title.Trim().ToLowerInvariant()
                .Replace("：", ":").Replace("（", "(").Replace("）", ")")
                .Replace("，", ",").Replace("！", "!").Replace("？", "?");

            return System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s\-_:,.!?()\[\]""'']+", "");
        }
    }

    public class ChapterCheckItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnChanged(nameof(IsSelected)); }
        }

        private string _translatedTitle = "";
        public string TranslatedTitle
        {
            get => _translatedTitle;
            set { _translatedTitle = value; OnChanged(nameof(TranslatedTitle)); }
        }

        private bool _isAlreadyDownloaded;
        public bool IsAlreadyDownloaded
        {
            get => _isAlreadyDownloaded;
            set { _isAlreadyDownloaded = value; OnChanged(nameof(IsAlreadyDownloaded)); }
        }

        private string _alreadyDownloadedLabel = "";
        public string AlreadyDownloadedLabel
        {
            get => _alreadyDownloadedLabel;
            set { _alreadyDownloadedLabel = value; OnChanged(nameof(AlreadyDownloadedLabel)); }
        }

        public int Index { get; set; }
        public int Number { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ChapterUrl { get; set; } = string.Empty;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));
    }
}