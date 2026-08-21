using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class NovelDetailPage : UserControl
    {
        private const double NarrowThreshold = 760;
        private const int ChaptersPerPage = 120;

        private readonly Guid _novelId;
        private readonly IPageFetcher _browser = PlatformServices.PageFetcher;
        private readonly List<IDownloadSource> _sources;
        private readonly TranslationService _titleTranslator = new(new CTranslate2Provider());
        private readonly FileImportService _fileImportService = new();
        private readonly TranslationService _fileContentTranslator = new(new CTranslate2Provider());

        private string _authorName = "";
        private string _sourceUrl = "";
        private Control? _relatedContent;
        private ItemsControl RelatedList = null!;

        private List<ChapterListItem> _allChapters = new();
        private Dictionary<Guid, string> _volumeNames = new();
        private Dictionary<Guid, int> _volumeChapterCounts = new();
        private int _chapterPage = 1;

        private List<GlossarySetEntry> _nameEntries = new();
        private GlossarySetEntry? _editingEntry;
        private bool _showDuplicateNamesOnly;
        private Guid? _viewingSetId;
        private string _viewingSetName = "";

        private Action? _pendingConfirmAction;

        private Popup? _libraryPopup;
        private Popup? _newLibraryPopup;
        private TextBox? _newLibraryNameBox;
        private Control? _libraryButtonTarget;

        private readonly ObservableCollection<LofterUpdateItem> _lofterUpdateItems = new();
        private IDownloadSource? _lofterUpdateSource;

        internal class ChapterListItem
        {
            public int Number { get; set; }
            public string DisplayTitle { get; set; } = "";
            public Guid? VolumeId { get; set; }
        }

        internal class ChapterSection
        {
            public Guid? VolumeId { get; set; }
            public string? Header { get; set; }
            public bool HasHeader => !string.IsNullOrEmpty(Header);
            public List<ChapterListItem> Chapters { get; set; } = new();
        }

        internal class SetOptionItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsApplied { get; set; }
        }

        internal class RelatedNovelItem
        {
            public Guid Id { get; set; }
            public string DisplayTitle { get; set; } = "";
            public string Author { get; set; } = "";
            public string DirectionTag { get; set; } = "";
            public string Status { get; set; } = "";
            public string ReadProgress { get; set; } = "";
            public Bitmap? CoverBitmap { get; set; }
        }

        internal class LofterUpdateItem : INotifyPropertyChanged
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

            public string Title { get; set; } = "";
            public string ChapterUrl { get; set; } = "";

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public NovelDetailPage(Guid novelId)
        {
            InitializeComponent();
            _novelId = novelId;

            var tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            var ocr = new OcrService(tessdataPath);

            _sources = new List<IDownloadSource>
            {
                new Sixty9ShubaDownloadSource(_browser),
                new FanqieDownloadSource(_browser, PlatformServices.ScreenshotFetcher, ocr),
                new BiqugeDownloadSource(_browser),
                new JinjiangDownloadSource(_browser),
                new LofterDownloadSource(),
                new WikidichDownloadSource(_browser)
            };

            _relatedContent = BuildRelatedPanel();
            SizeChanged += OnPageSizeChanged;
            UpdateRelatedLayout(Bounds.Width);
            LoadNovel();

            AddDeleteNovelMenuItem();
        }

        // ===================== Bố cục co giãn =====================

        private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateRelatedLayout(e.NewSize.Width);

        private void UpdateRelatedLayout(double width)
        {
            if (_relatedContent == null)
                return;

            bool isNarrow = width < NarrowThreshold;

            if (isNarrow)
            {
                RelatedSlotWide.Content = null;
                RelatedSlotNarrow.Content = _relatedContent;
            }
            else
            {
                RelatedSlotNarrow.Content = null;
                RelatedSlotWide.Content = _relatedContent;
            }
        }

        private Control BuildRelatedPanel()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Cùng thể loại", FontSize = 18, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 8) });

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = (IBrush)this.FindResource("BorderSoft")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Height = 500
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            RelatedList = new ItemsControl { ItemTemplate = BuildLibraryLikeRelatedTemplate() };
            scroll.Content = RelatedList;
            border.Child = scroll;
            stack.Children.Add(border);

            return stack;
        }

        // ===================== Tải dữ liệu truyện, chương, truyện liên quan =====================

        private void LoadNovel()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var novel = db.Novels.FirstOrDefault(n => n.Id == _novelId);
            if (novel == null)
                return;

            TitleText.Text = novel.DisplayTitle;
            _authorName = novel.Author;
            _sourceUrl = novel.SourceUrl;
            AuthorText.Text = novel.DisplayAuthor;

            if (!string.IsNullOrWhiteSpace(novel.TranslatedTitle) && novel.TranslatedTitle != novel.Title)
            {
                OriginalTitleText.Text = $"Tên gốc: {novel.Title}";
                OriginalTitleText.IsVisible = true;
            }
            else
            {
                OriginalTitleText.IsVisible = false;
            }

            StatusBadgeText.Text = novel.Status;
            var (badgeBg, badgeFg) = novel.Status switch
            {
                "Hoàn thành" => ("#E3F7E8", "#2E9E4F"),
                "Còn tiếp" => ("#E3F1FB", "#1E7FD1"),
                "Tạm ngưng" => ("#FDECE3", "#D1621E"),
                _ => ("#F0F0F0", "#777777")
            };
            StatusBadge.Background = SolidColorBrush.Parse(badgeBg);
            StatusBadgeText.Foreground = SolidColorBrush.Parse(badgeFg);

            var latestChapter = db.Chapters.Where(c => c.NovelId == _novelId).OrderByDescending(c => c.Number).FirstOrDefault();
            if (latestChapter != null)
            {
                MetaLatestText.Text = $"Mới nhất: {latestChapter.DisplayTitle}";
                MetaLatestText.IsVisible = true;
                MetaUpdateText.Margin = new Thickness(0, 8, 0, 0);
            }
            else
            {
                MetaLatestText.IsVisible = false;
                MetaUpdateText.Margin = new Thickness(0);
            }

            var updateTime = novel.LastUpdatedAt ?? novel.AddedAt;
            MetaUpdateText.Text = $"Cập nhật: {updateTime:dd-MM-yyyy HH:mm}";

            var tagList = string.IsNullOrWhiteSpace(novel.Tags)
                ? new List<string> { "(chưa có tag)" }
                : novel.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            TagsList.ItemsSource = tagList;

            DescriptionText.Text = string.IsNullOrWhiteSpace(novel.Description) ? "Chưa có giới thiệu." : novel.Description;
            TryLoadCoverPreview(novel.CoverImagePath);

            _volumeNames = db.Volumes
                .Where(v => v.NovelId == _novelId)
                .ToDictionary(v => v.Id, v => v.Name);

            _volumeChapterCounts = db.Chapters
                .Where(c => c.NovelId == _novelId && c.VolumeId != null)
                .GroupBy(c => c.VolumeId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            _allChapters = db.Chapters
                .Where(c => c.NovelId == _novelId)
                .OrderBy(c => c.Number)
                .Select(c => new ChapterListItem { Number = c.Number, DisplayTitle = c.DisplayTitle, VolumeId = c.VolumeId })
                .ToList();
            ChaptersTitleText.Text = $"Mục lục · {_allChapters.Count} Chương";
            _chapterPage = 1;
            LoadChapterPage();

            LoadRelated(db, novel);
        }

        private void LoadRelated(MiaoDbContext db, Novel novel)
        {
            const int maxItems = 15;

            var myTags = (novel.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var others = db.Novels.Where(n => n.Id != novel.Id).ToList();

            var sameAuthor = others
                .Where(n => !string.IsNullOrWhiteSpace(novel.Author) && string.Equals(n.Author, novel.Author, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var remaining = others.Except(sameAuthor)
                .Select(n => new
                {
                    Novel = n,
                    TagOverlap = myTags.Count == 0
                        ? 0
                        : (n.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Count(t => myTags.Contains(t))
                })
                .Where(x => x.TagOverlap > 0)
                .OrderByDescending(x => x.TagOverlap)
                .Select(x => x.Novel);

            var relatedNovels = sameAuthor.Concat(remaining).Take(maxItems).ToList();

            RelatedList.ItemsSource = relatedNovels.Select(n =>
            {
                var totalChapters = db.Chapters.Count(c => c.NovelId == n.Id);

                var tagRows = (from nt in db.NovelTags
                               join t in db.Tags on nt.TagId equals t.Id
                               where nt.NovelId == n.Id
                               select new { t.Name, t.Category }).ToList();

                var directionRow = tagRows.FirstOrDefault(t =>
                    t.Category.Contains("hướng", StringComparison.OrdinalIgnoreCase) ||
                    t.Category.Contains("giới tính", StringComparison.OrdinalIgnoreCase) ||
                    t.Category.Contains("giới", StringComparison.OrdinalIgnoreCase));

                var direction = directionRow?.Name.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(direction))
                {
                    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Ngôn tình", "Đam mỹ", "Bách hợp", "Vô CP", "Không CP", "BG", "BL", "GL", "言情", "纯爱", "百合", "无CP"
                    };
                    direction = tagRows.Select(t => t.Name.Trim()).FirstOrDefault(known.Contains) ?? string.Empty;
                }

                return new RelatedNovelItem
                {
                    Id = n.Id,
                    DisplayTitle = n.DisplayTitle,
                    Author = n.DisplayAuthor,
                    DirectionTag = direction,
                    Status = n.Status,
                    ReadProgress = $"Đã đọc {n.LastReadChapterNumber}/{totalChapters}",
                    CoverBitmap = LoadThumb(n.CoverImagePath)
                };
            }).ToList();
        }

        private void LoadChapterPage()
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(_allChapters.Count / (double)ChaptersPerPage));

            var pageChapters = _allChapters.Skip((_chapterPage - 1) * ChaptersPerPage).Take(ChaptersPerPage).ToList();
            ChaptersList.ItemsSource = BuildChapterSections(pageChapters);

            ChapterPagination.IsVisible = totalPages > 1;
            ChapterPageText.Text = $"{_chapterPage} / {totalPages}";
        }

        private List<ChapterSection> BuildChapterSections(List<ChapterListItem> chapters)
        {
            var sections = new List<ChapterSection>();

            foreach (var chapter in chapters)
            {
                var current = sections.Count > 0 ? sections[^1] : null;
                if (current == null || current.VolumeId != chapter.VolumeId)
                {
                    current = new ChapterSection { VolumeId = chapter.VolumeId, Header = BuildVolumeHeader(chapter.VolumeId) };
                    sections.Add(current);
                }

                current.Chapters.Add(chapter);
            }

            return sections;
        }

        private string? BuildVolumeHeader(Guid? volumeId)
        {
            if (volumeId == null || !_volumeNames.TryGetValue(volumeId.Value, out var name))
                return null;

            var count = _volumeChapterCounts.TryGetValue(volumeId.Value, out var c) ? c : 0;
            return $"{name} · Cộng{count}Chương";
        }

        private void OnPreviousChapterPageClick(object? sender, RoutedEventArgs e)
        {
            if (_chapterPage > 1)
            {
                _chapterPage--;
                LoadChapterPage();
            }
        }

        private void OnNextChapterPageClick(object? sender, RoutedEventArgs e)
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(_allChapters.Count / (double)ChaptersPerPage));
            if (_chapterPage < totalPages)
            {
                _chapterPage++;
                LoadChapterPage();
            }
        }

        private static Bitmap? LoadThumb(string? path)
        {
            var fullPath = CoverPathResolver.Resolve(path);
            if (fullPath == null || !File.Exists(fullPath))
                return null;

            try
            {
                using var stream = File.OpenRead(fullPath);
                return Bitmap.DecodeToWidth(stream, 200);
            }
            catch
            {
                return null;
            }
        }

        private void TryLoadCoverPreview(string? path)
        {
            var fullPath = CoverPathResolver.Resolve(path);

            if (fullPath == null || !File.Exists(fullPath))
            {
                fullPath = Path.Combine(AppContext.BaseDirectory, "Assets", "default-cover.jpg");
                if (!File.Exists(fullPath))
                    return;
            }

            try
            {
                CoverImage.Source = new Bitmap(fullPath);
            }
            catch
            {
                // Ảnh lỗi hoặc không đọc được thì bỏ qua.
            }
        }

        // ===================== Điều hướng =====================

        private void OnRelatedClick(Guid relatedId) => AppNavigator.NavigateTo(new NovelDetailPage(relatedId));

        private void OnEditMenuClick(object? sender, RoutedEventArgs e) => EditPopup.IsOpen = !EditPopup.IsOpen;

        private void OnEditInfoClick(object? sender, RoutedEventArgs e) => AppNavigator.NavigateTo(new NovelEditPage(_novelId));

        private void OnAuthorClick(object? sender, PointerPressedEventArgs e) => AppNavigator.NavigateTo(new AuthorPage(_authorName));

        private void OnReadClick(object? sender, RoutedEventArgs e) => AppNavigator.NavigateTo(new ReaderPage(_novelId, 1));

        private void OnChapterButtonClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int number)
                AppNavigator.NavigateTo(new ReaderPage(_novelId, number));
        }

        private void OnWorkspaceClick(object? sender, RoutedEventArgs e) => AppNavigator.NavigateTo(new WorkspacePage(_novelId));

        // ===================== Modal dùng chung (ModalService) =====================

        private void ShowModal(Control card)
        {
            if (card.Parent is Panel panel)
                panel.Children.Remove(card);

            card.IsVisible = true;
            ModalService.Show(card);
        }

        private void ShowConfirm(string message, Action onConfirm)
        {
            ConfirmMessageText.Text = message;
            _pendingConfirmAction = onConfirm;
            ShowModal(ConfirmCard);
        }

        private void OnConfirmYesClick(object? sender, RoutedEventArgs e)
        {
            var action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            ModalService.Close();
            action?.Invoke();
        }

        private void OnConfirmNoClick(object? sender, RoutedEventArgs e)
        {
            _pendingConfirmAction = null;
            ModalService.Close();
        }

        // ===================== Cập nhật chương mới từ link nguồn =====================

        private async void OnUpdateClick(object? sender, RoutedEventArgs e)
        {
            EditPopup.IsOpen = false;

            var linksToCheck = new List<string>();
            if (!string.IsNullOrWhiteSpace(_sourceUrl))
                linksToCheck.Add(_sourceUrl);

            using (var linkDb = new MiaoDbContext(AppPaths.DbFilePath))
            {
                linksToCheck.AddRange(
                    linkDb.NovelLinks.Where(l => l.NovelId == _novelId)
                        .Select(l => l.Url)
                        .Where(u => !string.IsNullOrWhiteSpace(u)));
            }

            if (linksToCheck.Count == 0)
            {
                UpdateStatusText.Text = "Truyện này không có link nguồn để cập nhật.";
                return;
            }

            UpdateButton.IsEnabled = false;

            int totalAdded = 0, totalFailed = 0, totalSkippedDuplicateTitle = 0;
            var linkResults = new List<string>();

            try
            {
                foreach (var url in linksToCheck)
                {
                    var source = _sources.FirstOrDefault(s => s.CanHandle(url));
                    if (source == null)
                    {
                        linkResults.Add($"{url}: không hỗ trợ nguồn này, bỏ qua.");
                        continue;
                    }

                    if (source is LofterDownloadSource)
                    {
                        linkResults.Add($"{url}: là Lofter, dùng nút cập nhật Lofter riêng.");
                        continue;
                    }

                    UpdateStatusText.Text = $"Đang kiểm tra: {url}";

                    List<(int number, string title, string chapterUrl)> allChapters;
                    try
                    {
                        allChapters = await source.GetChapterListAsync(url);
                    }
                    catch (Exception ex)
                    {
                        linkResults.Add($"{url}: lỗi khi lấy danh sách chương — {ex.Message}");
                        continue;
                    }

                    HashSet<string> existingUrls;
                    HashSet<string> existingTitles;
                    int nextNumber;
                    using (var checkDb = new MiaoDbContext(AppPaths.DbFilePath))
                    {
                        var existingChapters = checkDb.Chapters.Where(c => c.NovelId == _novelId).ToList();
                        existingUrls = existingChapters.Select(c => c.SourceUrl).ToHashSet();
                        existingTitles = existingChapters.Select(c => NormalizeTitle(c.Title)).ToHashSet();
                        nextNumber = existingChapters.Count == 0 ? 1 : existingChapters.Max(c => c.Number) + 1;
                    }

                    var newChapters = new List<(int number, string title, string chapterUrl)>();
                    foreach (var ch in allChapters)
                    {
                        if (existingUrls.Contains(ch.chapterUrl))
                            continue;

                        if (existingTitles.Contains(NormalizeTitle(ch.title)))
                        {
                            totalSkippedDuplicateTitle++;
                            continue;
                        }

                        newChapters.Add(ch);
                    }

                    if (newChapters.Count == 0)
                    {
                        linkResults.Add($"{url}: không có chương mới.");
                        continue;
                    }

                    int done = 0, addedThisLink = 0, failedThisLink = 0;

                    foreach (var (_, title, chapterUrl) in newChapters)
                    {
                        UpdateStatusText.Text = $"[{url}] Đang tải chương ({++done}/{newChapters.Count})...";

                        string content;
                        try { content = await source.GetChapterContentAsync(chapterUrl); }
                        catch { content = ""; }

                        if (string.IsNullOrWhiteSpace(content))
                        {
                            failedThisLink++;
                            continue;
                        }

                        string translatedTitle = title;
                        string displayContent = content;

                        if (!source.ProvidesTranslatedContent)
                        {
                            try
                            {
                                var t = (await _titleTranslator.TranslateChapterAsync(title)).Trim();
                                if (!string.IsNullOrWhiteSpace(t)) translatedTitle = t;
                            }
                            catch { }

                            if (Regex.IsMatch(content, @"\p{IsCJKUnifiedIdeographs}"))
                            {
                                UpdateStatusText.Text = $"[{url}] Đang dịch chương ({done}/{newChapters.Count})...";
                                try
                                {
                                    var result = await _titleTranslator.TranslateChapterAsync(content);
                                    if (!string.IsNullOrWhiteSpace(result)) displayContent = result;
                                }
                                catch { }
                            }
                        }

                        using var chapterDb = new MiaoDbContext(AppPaths.DbFilePath);
                        chapterDb.Chapters.Add(new Chapter
                        {
                            NovelId = _novelId,
                            Number = nextNumber++,
                            Title = title,
                            TranslatedTitle = translatedTitle,
                            SourceUrl = chapterUrl,
                            OriginalContent = content,
                            DisplayContent = displayContent
                        });
                        chapterDb.SaveChanges();
                        addedThisLink++;
                    }

                    totalAdded += addedThisLink;
                    totalFailed += failedThisLink;
                    linkResults.Add($"{url}: +{addedThisLink} chương mới" + (failedThisLink > 0 ? $", {failedThisLink} lỗi" : ""));
                }

                if (totalAdded > 0)
                {
                    using var novelDb = new MiaoDbContext(AppPaths.DbFilePath);
                    var novel = novelDb.Novels.FirstOrDefault(n => n.Id == _novelId);
                    if (novel != null)
                    {
                        novel.LastUpdatedAt = DateTime.Now;
                        novelDb.SaveChanges();
                    }
                }

                var summary = $"Hoàn tất — tổng cộng {totalAdded} chương mới từ {linksToCheck.Count} link.";
                if (totalSkippedDuplicateTitle > 0)
                    summary += $" Đã bỏ qua {totalSkippedDuplicateTitle} chương trùng tiêu đề (khác nguồn).";
                if (totalFailed > 0)
                    summary += $" {totalFailed} chương tải lỗi.";

                UpdateStatusText.Text = summary + "\n" + string.Join("\n", linkResults);
                LoadNovel();
            }
            finally
            {
                UpdateButton.IsEnabled = true;
            }
        }

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";

            var normalized = title.Trim().ToLowerInvariant()
                .Replace("：", ":").Replace("（", "(").Replace("）", ")")
                .Replace("，", ",").Replace("！", "!").Replace("？", "?");

            return Regex.Replace(normalized, @"[\s\-_:,.!?()\[\]""'']+", "");
        }

        // ===================== Cập nhật chương mới từ Lofter =====================

        private async Task StartLofterUpdateAsync(IDownloadSource source)
        {
            _lofterUpdateSource = source;
            UpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "Đang kiểm tra bài đăng mới từ Lofter...";

            try
            {
                HashSet<string> existingUrls;
                using (var db = new MiaoDbContext(AppPaths.DbFilePath))
                    existingUrls = db.Chapters.Where(c => c.NovelId == _novelId).Select(c => c.SourceUrl).ToHashSet();

                var allChapters = await source.GetChapterListAsync(_sourceUrl);
                var newChapters = allChapters.Where(c => !existingUrls.Contains(c.ChapterUrl)).ToList();

                if (newChapters.Count == 0)
                {
                    UpdateStatusText.Text = "Không có bài đăng mới trên blog này.";
                    return;
                }

                _lofterUpdateItems.Clear();
                foreach (var (_, title, chapterUrl) in newChapters)
                {
                    _lofterUpdateItems.Add(new LofterUpdateItem
                    {
                        Title = title,
                        ChapterUrl = chapterUrl,
                        TranslatedTitle = "Đang dịch..."
                    });
                }

                LofterUpdateList.ItemsSource = _lofterUpdateItems;
                LofterUpdateSubText.Text =
                    $"Tìm thấy {newChapters.Count} bài đăng mới trên blog. Vì Lofter là blog cá nhân có thể chứa " +
                    "nhiều truyện khác nhau, hãy bỏ chọn những bài KHÔNG thuộc truyện đang xem trước khi xác nhận.";

                UpdateStatusText.Text = "";
                ShowModal(LofterUpdateCard);

                _ = TranslateLofterUpdateTitlesAsync(_lofterUpdateItems.ToList());
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Lỗi: {ex.Message}";
            }
            finally
            {
                UpdateButton.IsEnabled = true;
            }
        }

        private async Task TranslateLofterUpdateTitlesAsync(List<LofterUpdateItem> items)
        {
            foreach (var item in items)
            {
                try
                {
                    var translated = await _titleTranslator.TranslateChapterAsync(item.Title);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        item.TranslatedTitle = string.IsNullOrWhiteSpace(translated) ? item.Title : translated.Trim());
                }
                catch
                {
                    await Dispatcher.UIThread.InvokeAsync(() => item.TranslatedTitle = item.Title);
                }
            }
        }

        private void OnLofterSelectAllClick(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _lofterUpdateItems)
                item.IsSelected = true;
        }

        private void OnLofterDeselectAllClick(object? sender, RoutedEventArgs e)
        {
            foreach (var item in _lofterUpdateItems)
                item.IsSelected = false;
        }

        private void OnLofterUpdateCancelClick(object? sender, RoutedEventArgs e)
        {
            ModalService.Close();
            UpdateStatusText.Text = "Đã hủy cập nhật.";
        }

        private async void OnLofterUpdateConfirmClick(object? sender, RoutedEventArgs e)
        {
            var selected = _lofterUpdateItems.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                UpdateStatusText.Text = "Chưa chọn bài đăng nào.";
                return;
            }

            if (_lofterUpdateSource == null)
                return;

            ModalService.Close();
            LofterUpdateConfirmButton.IsEnabled = false;

            int nextNumber;
            using (var numberDb = new MiaoDbContext(AppPaths.DbFilePath))
            {
                var existingNumbers = numberDb.Chapters.Where(c => c.NovelId == _novelId).Select(c => c.Number).ToList();
                nextNumber = existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;
            }

            int done = 0, added = 0, failed = 0;

            try
            {
                foreach (var item in selected)
                {
                    UpdateStatusText.Text = $"Đang tải chương ({++done}/{selected.Count})...";

                    string content;
                    try { content = await _lofterUpdateSource.GetChapterContentAsync(item.ChapterUrl); }
                    catch { content = ""; }

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        failed++;
                        continue;
                    }

                    string displayContent = content;
                    if (Regex.IsMatch(content, @"\p{IsCJKUnifiedIdeographs}"))
                    {
                        UpdateStatusText.Text = $"Đang dịch chương ({done}/{selected.Count})...";
                        try
                        {
                            var result = await _titleTranslator.TranslateChapterAsync(content);
                            if (!string.IsNullOrWhiteSpace(result))
                                displayContent = result;
                        }
                        catch { }
                    }

                    using var chapterDb = new MiaoDbContext(AppPaths.DbFilePath);
                    chapterDb.Chapters.Add(new Chapter
                    {
                        NovelId = _novelId,
                        Number = nextNumber++,
                        Title = item.Title,
                        TranslatedTitle = item.TranslatedTitle,
                        SourceUrl = item.ChapterUrl,
                        OriginalContent = content,
                        DisplayContent = displayContent
                    });
                    chapterDb.SaveChanges();
                    added++;
                }

                if (added > 0)
                {
                    using var novelDb = new MiaoDbContext(AppPaths.DbFilePath);
                    var novel = novelDb.Novels.FirstOrDefault(n => n.Id == _novelId);
                    if (novel != null)
                    {
                        novel.LastUpdatedAt = DateTime.Now;
                        novelDb.SaveChanges();
                    }
                }

                UpdateStatusText.Text = failed == 0
                    ? $"Hoàn tất — đã thêm {added} chương mới."
                    : $"Hoàn tất — đã thêm {added} chương mới, {failed} chương tải lỗi.";
                LoadNovel();
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Lỗi khi cập nhật: {ex.Message}";
            }
            finally
            {
                LofterUpdateConfirmButton.IsEnabled = true;
            }
        }

        // ===================== Cập nhật chương mới từ file (txt/epub/docx) =====================

        private async void OnUpdateFileClick(object? sender, RoutedEventArgs e)
        {
            EditPopup.IsOpen = false;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Chọn file để cập nhật truyện",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Tệp truyện (*.txt;*.epub;*.docx)") { Patterns = new[] { "*.txt", "*.epub", "*.docx" } }
                }
            });

            if (files.Count == 0 || files[0].Path.LocalPath is not { } filePath)
                return;

            UpdateStatusText.Text = "Đang đọc file...";

            try
            {
                var imported = _fileImportService.ImportFromFile(filePath);
                if (imported.Chapters.Count == 0)
                {
                    UpdateStatusText.Text = "File không có chương nào để cập nhật.";
                    return;
                }

                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var novel = db.Novels.FirstOrDefault(n => n.Id == _novelId);
                if (novel == null)
                {
                    UpdateStatusText.Text = "Không tìm thấy truyện cần cập nhật.";
                    return;
                }

                var existing = db.Chapters.Where(c => c.NovelId == _novelId).OrderBy(c => c.Number).ToList();
                var usedNumbers = existing.Select(c => c.Number).ToHashSet();
                var nextNumber = usedNumbers.Count == 0 ? 1 : usedNumbers.Max() + 1;
                var titleOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

                var added = 0;
                var updated = 0;
                var translated = 0;
                var translationFailed = 0;
                var empty = 0;

                for (var i = 0; i < imported.Chapters.Count; i++)
                {
                    var importedChapter = imported.Chapters[i];
                    var title = (importedChapter.Title ?? string.Empty).Trim();
                    var content = (importedChapter.Content ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        empty++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(title))
                        title = $"Chương {i + 1}";

                    titleOccurrences.TryGetValue(title, out var occurrence);
                    occurrence++;
                    titleOccurrences[title] = occurrence;

                    var fileKey = BuildFileChapterKey(filePath, title, occurrence);
                    var chapter = existing.FirstOrDefault(c => string.Equals(c.SourceUrl, fileKey, StringComparison.OrdinalIgnoreCase));

                    if (chapter == null)
                    {
                        var sameTitle = existing
                            .Where(c => string.Equals(c.Title, title, StringComparison.Ordinal))
                            .OrderBy(c => c.Number)
                            .ToList();
                        if (sameTitle.Count >= occurrence)
                            chapter = sameTitle[occurrence - 1];
                    }

                    var displayContent = content;
                    var translatedTitle = chapter?.TranslatedTitle ?? string.Empty;
                    var needsTranslation = Regex.IsMatch(content, @"\p{IsCJKUnifiedIdeographs}");

                    if (needsTranslation)
                    {
                        UpdateStatusText.Text = $"Đang dịch file — chương {i + 1}/{imported.Chapters.Count}...";
                        try
                        {
                            var result = await _fileContentTranslator.TranslateChapterAsync(content);
                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                displayContent = result;
                                translated++;
                            }
                            else
                            {
                                translationFailed++;
                            }

                            if (Regex.IsMatch(title, @"\p{IsCJKUnifiedIdeographs}"))
                            {
                                try
                                {
                                    var translatedChapterTitle = (await _fileContentTranslator.TranslateChapterAsync(title)).Trim();
                                    if (!string.IsNullOrWhiteSpace(translatedChapterTitle))
                                        translatedTitle = translatedChapterTitle;
                                }
                                catch { }
                            }
                        }
                        catch
                        {
                            translationFailed++;
                        }
                    }
                    else
                    {
                        translatedTitle = string.Empty;
                    }

                    if (chapter == null)
                    {
                        while (usedNumbers.Contains(nextNumber))
                            nextNumber++;

                        chapter = new Chapter
                        {
                            NovelId = _novelId,
                            Number = nextNumber++,
                            Title = title,
                            TranslatedTitle = translatedTitle,
                            SourceUrl = fileKey,
                            OriginalContent = content,
                            DisplayContent = displayContent
                        };
                        db.Chapters.Add(chapter);
                        existing.Add(chapter);
                        usedNumbers.Add(chapter.Number);
                        added++;
                    }
                    else
                    {
                        chapter.Title = title;
                        chapter.TranslatedTitle = translatedTitle;
                        chapter.SourceUrl = fileKey;
                        chapter.OriginalContent = content;
                        chapter.DisplayContent = displayContent;
                        chapter.LastEditedAt = DateTime.Now;
                        updated++;
                    }
                }

                if (added > 0)
                    novel.LastUpdatedAt = DateTime.Now;

                db.SaveChanges();

                var parts = new List<string> { $"Cập nhật file xong: {added} chương mới, {updated} chương cập nhật" };
                if (translated > 0) parts.Add($"{translated} chương đã dịch");
                if (translationFailed > 0) parts.Add($"{translationFailed} chương dịch lỗi");
                if (empty > 0) parts.Add($"{empty} chương rỗng");

                UpdateStatusText.Text = string.Join("; ", parts) + ".";
                LoadNovel();
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Lỗi cập nhật file: {ex.Message}";
            }
        }

        private static string BuildFileChapterKey(string filePath, string title, int occurrence)
        {
            var fullPath = Path.GetFullPath(filePath).Trim().ToLowerInvariant();
            return $"file://miao-update/{Uri.EscapeDataString(fullPath)}#title={Uri.EscapeDataString(title)}&occurrence={occurrence}";
        }

        // ===================== Xuất truyện (EPUB / DOCX / PDF) =====================

        private void OnExportEpubClick(object? sender, RoutedEventArgs e) => _ = ExportNovelAsync(NovelExportFormat.Epub);

        private void OnExportDocxClick(object? sender, RoutedEventArgs e) => _ = ExportNovelAsync(NovelExportFormat.Docx);

        private void OnExportPdfClick(object? sender, RoutedEventArgs e) => _ = ExportNovelAsync(NovelExportFormat.Pdf);

        private async Task ExportNovelAsync(NovelExportFormat format)
        {
            EditPopup.IsOpen = false;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var novel = db.Novels.FirstOrDefault(n => n.Id == _novelId);
            if (novel == null)
                return;

            var chapters = db.Chapters.Where(c => c.NovelId == _novelId).OrderBy(c => c.Number).ToList();
            if (chapters.Count == 0)
            {
                UpdateStatusText.Text = "Truyện chưa có chương nào để xuất.";
                return;
            }

            var (ext, filterLabel) = format switch
            {
                NovelExportFormat.Epub => ("epub", "Epub Book"),
                NovelExportFormat.Docx => ("docx", "Word Document"),
                NovelExportFormat.Pdf => ("pdf", "PDF Document"),
                _ => ("epub", "Epub Book")
            };

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var safeName = string.Join("_", novel.DisplayTitle.Split(Path.GetInvalidFileNameChars()));

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"{safeName}.{ext}",
                FileTypeChoices = new[] { new FilePickerFileType(filterLabel) { Patterns = new[] { $"*.{ext}" } } }
            });

            if (file?.Path.LocalPath is not { } savePath)
                return;

            UpdateStatusText.Text = $"Đang xuất file {ext.ToUpperInvariant()}...";
            try
            {
                NovelExportService.Export(novel, chapters, savePath, format);
                UpdateStatusText.Text = $"Đã lưu file: {savePath}";
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Lỗi khi xuất file: {ex.Message}";
            }
        }

        // ===================== Bộ tên (glossary set) áp dụng cho truyện =====================

        private void OnNameClick(object? sender, RoutedEventArgs e)
        {
            EditPopup.IsOpen = false;
            RefreshSelectSetsCard();
            ShowModal(SelectSetsCard);
        }

        private void RefreshSelectSetsCard()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var appliedIds = db.NovelGlossarySets.Where(ns => ns.NovelId == _novelId).Select(ns => ns.GlossarySetId).ToHashSet();
            var allSets = db.GlossarySets.OrderBy(s => s.Name).ToList();

            var sharedKeyword = SharedSearchBox.Text?.Trim() ?? "";
            var privateKeyword = PrivateSearchBox.Text?.Trim() ?? "";

            var shared = allSets.Where(s => s.IsShared);
            if (!string.IsNullOrEmpty(sharedKeyword))
                shared = shared.Where(s => s.Name.Contains(sharedKeyword, StringComparison.OrdinalIgnoreCase));

            var priv = allSets.Where(s => !s.IsShared);
            if (!string.IsNullOrEmpty(privateKeyword))
                priv = priv.Where(s => s.Name.Contains(privateKeyword, StringComparison.OrdinalIgnoreCase));

            SharedOptionsList.ItemsSource = shared.Select(s => ToOption(s, appliedIds)).ToList();
            PrivateOptionsList.ItemsSource = priv.Select(s => ToOption(s, appliedIds)).ToList();

            var appliedSets = allSets.Where(s => appliedIds.Contains(s.Id)).ToList();
            AppliedOptionsList.ItemsSource = appliedSets.Select(s => ToOption(s, appliedIds)).ToList();
            AppliedEmptyText.IsVisible = appliedSets.Count == 0;

            Dispatcher.UIThread.Post(ConfigureSetOptionTextWrapping, DispatcherPriority.Loaded);
            ConfigureSelectSetsCloseButton();
        }

        private static SetOptionItem ToOption(GlossarySet s, HashSet<Guid> appliedIds)
            => new() { Id = s.Id, Name = s.Name, IsApplied = appliedIds.Contains(s.Id) };

        private void OnSetSearchChanged(object? sender, TextChangedEventArgs e) => RefreshSelectSetsCard();

        private void OnToggleSharedSectionClick(object? sender, PointerPressedEventArgs e)
            => SharedSectionPanel.IsVisible = !SharedSectionPanel.IsVisible;

        private void OnTogglePrivateSectionClick(object? sender, PointerPressedEventArgs e)
            => PrivateSectionPanel.IsVisible = !PrivateSectionPanel.IsVisible;

        private void OnToggleApplyClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not SetOptionItem opt)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var existing = db.NovelGlossarySets.FirstOrDefault(ns => ns.NovelId == _novelId && ns.GlossarySetId == opt.Id);

            if (cb.IsChecked == true)
            {
                if (existing == null)
                {
                    db.NovelGlossarySets.Add(new NovelGlossarySet { NovelId = _novelId, GlossarySetId = opt.Id });
                    db.SaveChanges();
                }
            }
            else if (existing != null)
            {
                db.NovelGlossarySets.Remove(existing);
                db.SaveChanges();
            }

            RefreshSelectSetsCard();
        }

        private void OnViewSetClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control fe || fe.Tag is not SetOptionItem opt)
                return;

            _viewingSetId = opt.Id;
            _viewingSetName = opt.Name;
            LoadNameList();
            ShowModal(NameCard);
        }

        private void OnCloseSelectSetsClick(object? sender, RoutedEventArgs e) => ModalService.Close();

        private void LoadNameList()
        {
            if (_viewingSetId == null)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var allEntries = db.GlossarySetEntries.Where(e => e.GlossarySetId == _viewingSetId.Value).OrderBy(e => e.OriginalTerm).ToList();

            if (_showDuplicateNamesOnly)
            {
                var duplicateTranslations = allEntries
                    .Where(e => !string.IsNullOrWhiteSpace(e.TranslatedTerm))
                    .GroupBy(e => e.TranslatedTerm.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _nameEntries = allEntries.Where(e => duplicateTranslations.Contains(e.TranslatedTerm.Trim())).ToList();
            }
            else
            {
                _nameEntries = allEntries;
            }

            NameList.ItemsSource = _nameEntries;
            NameCountText.Text = _showDuplicateNamesOnly
                ? $"{_viewingSetName} — Tên dịch trùng: {_nameEntries.Count}"
                : $"{_viewingSetName} — Tổng số name: {_nameEntries.Count}";

            ConfigureNameListCard();
        }

        private void ConfigureSetOptionTextWrapping()
        {
            foreach (var list in new[] { SharedOptionsList, PrivateOptionsList, AppliedOptionsList })
            {
                foreach (var text in FindVisualChildren<TextBlock>(list))
                {
                    text.TextWrapping = TextWrapping.Wrap;
                    text.MaxWidth = 340;
                }
            }
        }

        private void ConfigureSelectSetsCloseButton()
        {
            foreach (var button in FindVisualChildren<Button>(SelectSetsCard))
            {
                if (button.Content?.ToString() != "Đóng")
                    continue;

                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
                button.Margin = new Thickness(0);
            }
        }

        private void ConfigureNameListCard()
        {
            foreach (var button in FindVisualChildren<Button>(NameCard))
            {
                var content = button.Content?.ToString();
                if (content is "Lọc lại tên" or "Xóa tất cả" or "Đóng")
                {
                    button.HorizontalContentAlignment = HorizontalAlignment.Center;
                    button.VerticalContentAlignment = VerticalAlignment.Center;
                }
            }
        }

        private void OnCloseNamePopupClick()
        {
            _showDuplicateNamesOnly = false;
            RefreshSelectSetsCard();
            ShowModal(SelectSetsCard);
        }

        private void OnCloseNamePopupClickFixed(object? sender, RoutedEventArgs e) => OnCloseNamePopupClick();

        private void OnNameEntryClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control b && b.Tag is GlossarySetEntry entry)
                OpenNameEditPopup(entry);
        }

        private void OpenNameEditPopup(GlossarySetEntry entry)
        {
            _editingEntry = entry;
            EditOriginalText.Text = entry.OriginalTerm;
            EditHanVietBox.Text = entry.HanViet ?? "";
            EditNameBox.Text = entry.TranslatedTerm;
            ConfigureNameEditCard();
            ShowModal(NameEditCard);
        }

        private void ConfigureNameEditCard()
        {
            NameEditCard.Width = 340;
            NameEditCard.Height = double.NaN;
            NameEditCard.Padding = new Thickness(20);
            NameEditCard.Background = Brushes.White;
            NameEditCard.CornerRadius = new CornerRadius(12);

            foreach (var text in FindVisualChildren<TextBlock>(NameEditCard))
            {
                if (text.Text is "Bính Âm:" or "Bính Âm")
                    text.IsVisible = false;
                else if (text.Text is "Name:" or "Name" or "Tên dịch")
                {
                    text.Text = "Dịch:";
                    text.IsVisible = true;
                }
            }

            EditPinYinBox.IsVisible = false;
            EditOriginalText.Height = 32;
            EditOriginalText.Background = Brushes.White;
            EditNameBox.Height = 32;
            EditNameBox.Background = Brushes.White;
            EditNameBox.BorderBrush = (IBrush)this.FindResource("BorderSoft")!;
            EditHanVietBox.Height = 32;

            var buttons = FindVisualChildren<Button>(NameEditCard).ToList();
            if (buttons.Count >= 3)
            {
                buttons[0].Content = "Sửa";
                buttons[1].Content = "Xóa";
                buttons[2].Content = "Hủy";

                foreach (var button in buttons.Take(3))
                {
                    button.FontSize = 15;
                    button.HorizontalContentAlignment = HorizontalAlignment.Center;
                    button.VerticalContentAlignment = VerticalAlignment.Center;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(Visual root) where T : Visual
        {
            foreach (var child in root.GetVisualChildren())
            {
                if (child is T match)
                    yield return match;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void OnNameEditSaveClick(object? sender, RoutedEventArgs e)
        {
            if (_editingEntry == null)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var entry = db.GlossarySetEntries.Find(_editingEntry.Id);
            if (entry == null)
                return;

            entry.HanViet = EditHanVietBox.Text?.Trim() ?? "";
            entry.TranslatedTerm = EditNameBox.Text?.Trim() ?? "";
            db.SaveChanges();
            _editingEntry = null;
            ModalService.Close();
        }

        private void OnNameEditDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (_editingEntry == null)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var entry = db.GlossarySetEntries.Find(_editingEntry.Id);
            if (entry != null)
            {
                db.GlossarySetEntries.Remove(entry);
                db.SaveChanges();
            }

            _editingEntry = null;
            ModalService.Close();
        }

        private void OnNameEditCancelToNameList(object? sender, RoutedEventArgs e)
        {
            _editingEntry = null;
            NameEditCard.IsVisible = false;
            _showDuplicateNamesOnly = false;
            LoadNameList();
            ShowModal(NameCard);
        }

        private void OnRefilterNamesClick(object? sender, RoutedEventArgs e)
        {
            _showDuplicateNamesOnly = true;
            LoadNameList();
        }

        private void OnClearAllNamesClick(object? sender, RoutedEventArgs e)
        {
            if (_viewingSetId == null)
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var entries = db.GlossarySetEntries.Where(e => e.GlossarySetId == _viewingSetId.Value).ToList();
            if (entries.Count > 0)
            {
                db.GlossarySetEntries.RemoveRange(entries);
                db.SaveChanges();
            }

            LoadNameList();
        }

        // ===================== Mẫu hiển thị "Truyện liên quan" =====================

        private IDataTemplate BuildLibraryLikeRelatedTemplate()
        {
            return new FuncDataTemplate<RelatedNovelItem>((item, _) =>
            {
                var border = new Border
                {
                    Margin = new Thickness(0, 0, 0, 16),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = item.Id
                };
                border.PointerPressed += (_, _) => OnRelatedClick(item.Id);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition(104, GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                var imageBorder = new Border
                {
                    Width = 104,
                    Height = 144,
                    Margin = new Thickness(0, 0, 12, 0),
                    CornerRadius = new CornerRadius(5),
                    ClipToBounds = true
                };
                var image = new Image { Stretch = Stretch.UniformToFill, Source = item.CoverBitmap };
                imageBorder.Child = image;

                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                Grid.SetColumn(content, 1);

                content.Children.Add(MakeRelatedText(item.DisplayTitle, 17.6, FontWeight.Bold, "#444444", 0, TextTrimming.None, TextWrapping.Wrap));
                content.Children.Add(MakeRelatedText(item.Author, 15, FontWeight.Normal, "#333333", 2, TextTrimming.CharacterEllipsis, TextWrapping.NoWrap));
                content.Children.Add(MakeRelatedText(item.DirectionTag, 15, FontWeight.Normal, "#999999", 2, TextTrimming.CharacterEllipsis, TextWrapping.NoWrap));
                content.Children.Add(MakeRelatedText(item.Status, 15, FontWeight.Normal, "#999999", 2, TextTrimming.CharacterEllipsis, TextWrapping.NoWrap));
                content.Children.Add(MakeRelatedText(item.ReadProgress, 15, FontWeight.Normal, "#999999", 2, TextTrimming.None, TextWrapping.NoWrap));

                grid.Children.Add(imageBorder);
                grid.Children.Add(content);
                border.Child = grid;

                return border;
            });
        }

        private static TextBlock MakeRelatedText(string text, double size, FontWeight weight, string foreground, double top, TextTrimming trimming, TextWrapping wrapping)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Arial"),
                FontSize = size,
                FontWeight = weight,
                Foreground = SolidColorBrush.Parse(foreground),
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = wrapping,
                TextTrimming = trimming,
                Margin = new Thickness(0, top, 0, 0)
            };
        }

        // ===================== Thêm vào thư viện tuỳ chỉnh =====================

        private void OnAddToLibraryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button addButton)
                return;

            if (_libraryPopup != null)
                _libraryPopup.IsOpen = false;
            _libraryButtonTarget = addButton;

            _libraryPopup = BuildLibraryPopup(addButton);
            _libraryPopup.IsOpen = true;
        }

        private Popup BuildLibraryPopup(Control placementTarget)
        {
            var popup = new Popup
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true
            };

            var panel = new StackPanel();
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (IBrush)this.FindResource("BorderSoft")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Width = 240,
                Child = panel
            };

            using (var db = new MiaoDbContext(AppPaths.DbFilePath))
            {
                var addedLibraryIds = db.CustomLibraryNovels.Where(x => x.NovelId == _novelId).Select(x => x.CustomLibraryId).ToHashSet();

                foreach (var library in db.CustomLibraries.OrderBy(x => x.Name).ToList())
                {
                    var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                    row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
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
                            Foreground = (IBrush)this.FindResource("AccentJade")!,
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
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    libraryButton.Styles.Add((Avalonia.Styling.Style)this.FindResource("MenuItemButton")!);
                    libraryButton.Click += OnLibraryItemClick;
                    panel.Children.Add(libraryButton);
                }
            }

            panel.Children.Add(new Separator { Margin = new Thickness(4, 3, 4, 3) });

            var createButton = new Button { Content = "+ Thêm danh sách mới" };
            createButton.Styles.Add((Avalonia.Styling.Style)this.FindResource("MenuItemButton")!);
            createButton.Click += OnCreateLibraryClick;
            panel.Children.Add(createButton);

            popup.Child = card;
            return popup;
        }

        private void OnLibraryItemClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not Guid libraryId)
                return;

            AddNovelToLibrary(libraryId);
            if (_libraryPopup != null)
                _libraryPopup.IsOpen = false;
        }

        private void OnCreateLibraryClick(object? sender, RoutedEventArgs e)
        {
            if (_libraryPopup != null)
                _libraryPopup.IsOpen = false;

            if (_newLibraryPopup == null)
                _newLibraryPopup = BuildNewLibraryPopup(_libraryButtonTarget);
            else
                _newLibraryPopup.PlacementTarget = _libraryButtonTarget;

            _newLibraryNameBox!.Text = "";
            _newLibraryPopup.IsOpen = true;
            _newLibraryNameBox.Focus();
        }

        private Popup BuildNewLibraryPopup(Control? placementTarget)
        {
            var popup = new Popup
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true
            };

            var panel = new StackPanel();
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (IBrush)this.FindResource("BorderSoft")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18),
                Width = 320,
                Child = panel
            };

            var nameRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            nameRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            nameRow.Children.Add(new TextBlock
            {
                Text = "Tên",
                FontSize = 15,
                Foreground = (IBrush)this.FindResource("TextPrimary")!,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });

            _newLibraryNameBox = new TextBox
            {
                FontFamily = new FontFamily("Arial"),
                FontSize = 15,
                Padding = new Thickness(10, 7),
                BorderBrush = (IBrush)this.FindResource("BorderSoft")!,
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_newLibraryNameBox, 1);
            nameRow.Children.Add(_newLibraryNameBox);
            panel.Children.Add(nameRow);

            var addButton = new Button { Content = "Thêm", Width = 80 };
            addButton.Styles.Add((Avalonia.Styling.Style)this.FindResource("NovelPrimaryButton")!);
            addButton.Click += OnConfirmCreateLibraryClick;

            var cancelButton = new Button
            {
                Content = "Hủy",
                Width = 80,
                Margin = new Thickness(8, 0, 0, 0)
            };
            cancelButton.Styles.Add((Avalonia.Styling.Style)this.FindResource("NovelActionButton")!);
            cancelButton.Click += OnCancelCreateLibraryClick;

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttonRow.Children.Add(addButton);
            buttonRow.Children.Add(cancelButton);
            panel.Children.Add(buttonRow);

            popup.Child = card;
            return popup;
        }

        private void OnConfirmCreateLibraryClick(object? sender, RoutedEventArgs e)
        {
            var name = _newLibraryNameBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var existing = db.CustomLibraries.FirstOrDefault(x => x.Name == name);
            var library = existing ?? new CustomLibrary { Name = name };

            if (existing == null)
            {
                db.CustomLibraries.Add(library);
                db.SaveChanges();
            }

            var novelExists = db.CustomLibraryNovels.Any(x => x.CustomLibraryId == library.Id && x.NovelId == _novelId);
            if (!novelExists)
            {
                db.CustomLibraryNovels.Add(new CustomLibraryNovel { CustomLibraryId = library.Id, NovelId = _novelId });
                db.SaveChanges();
            }

            if (_newLibraryPopup != null)
                _newLibraryPopup.IsOpen = false;

            UpdateStatusText.Text = $"Đã thêm vào thư viện: {library.Name}";
        }

        private void OnCancelCreateLibraryClick(object? sender, RoutedEventArgs e)
        {
            if (_newLibraryPopup != null)
                _newLibraryPopup.IsOpen = false;
        }

        private void AddNovelToLibrary(Guid libraryId)
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var library = db.CustomLibraries.FirstOrDefault(x => x.Id == libraryId);
            if (library == null)
                return;

            var exists = db.CustomLibraryNovels.Any(x => x.CustomLibraryId == libraryId && x.NovelId == _novelId);
            if (!exists)
            {
                db.CustomLibraryNovels.Add(new CustomLibraryNovel { CustomLibraryId = libraryId, NovelId = _novelId });
                db.SaveChanges();
            }

            UpdateStatusText.Text = $"Đã thêm vào thư viện: {library.Name}";
        }

        // ===================== Xóa truyện =====================

        private void AddDeleteNovelMenuItem()
        {
            if (EditPopup.Child is not Border border || border.Child is not StackPanel menu)
                return;

            foreach (var child in menu.Children)
            {
                if (child is Button button && button.Tag as string == "DeleteNovelButton")
                    return;
            }

            var separator = new Separator { Margin = new Thickness(4) };
            var deleteButton = new Button
            {
                Content = "🗑 Xóa truyện",
                FontSize = 13,
                Tag = "DeleteNovelButton"
            };
            deleteButton.Styles.Add((Avalonia.Styling.Style)this.FindResource("MenuItemButton")!);
            deleteButton.Click += OnDeleteNovelClick;

            menu.Children.Add(separator);
            menu.Children.Add(deleteButton);
        }

        private void OnDeleteNovelClick(object? sender, RoutedEventArgs e)
        {
            EditPopup.IsOpen = false;
            ShowConfirm(
                "Xóa truyện này khỏi Miao? Toàn bộ chương, ghi chú và dữ liệu liên quan của truyện sẽ bị xóa khỏi cơ sở dữ liệu.",
                DeleteNovel);
        }

        private void DeleteNovel()
        {
            try
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var novel = db.Novels.Find(_novelId);
                if (novel == null)
                    return;

                db.Novels.Remove(novel);
                db.SaveChanges();
                AppNavigator.NavigateTo(new LibraryPage());
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Lỗi khi xóa truyện: {ex.Message}";
            }
        }
    }
}