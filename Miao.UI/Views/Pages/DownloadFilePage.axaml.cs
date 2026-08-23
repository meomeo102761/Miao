using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class DownloadFilePage : UserControl
    {
        private readonly FileImportService _importService = new();
        private readonly ObservableCollection<ImportFileRow> _files = new();
        private readonly TranslationService _titleTranslator = TranslationService.CreateFromSettings();
        private readonly TranslationService _contentTranslator = TranslationService.CreateFromSettings();

        public DownloadFilePage()
        {
            InitializeComponent();
            FilesList.ItemsSource = _files;
        }

        // Avalonia: IStorageProvider thay cho Microsoft.Win32.OpenFileDialog.
        // Cùng 1 API này tự chạy đúng trên Desktop (dialog hệ điều hành) lẫn
        // Android (Storage Access Framework) — không cần code riêng theo platform.
        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Chọn file truyện",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Truyện (*.txt;*.epub;*.docx)")
                    {
                        Patterns = new[] { "*.txt", "*.epub", "*.docx" }
                    }
                }
            });

            if (result is null || result.Count == 0) return;

            StatusText.Text = "Đang đọc file...";

            int okCount = 0, errorCount = 0;

            foreach (var file in result)
            {
                var filePath = file.Path.LocalPath;

                // Bỏ qua nếu file này đã có trong danh sách
                if (_files.Any(f => f.FilePath == filePath)) continue;

                var row = new ImportFileRow { FilePath = filePath };

                try
                {
                    var imported = _importService.ImportFromFile(filePath);
                    row.Imported = imported;
                    row.Title = imported.Title;
                    row.Author = imported.Author;
                    row.ChapterCountLabel = $"Đọc được {imported.Chapters.Count} chương.";
                    okCount++;
                }
                catch (Exception ex)
                {
                    row.ErrorMessage = $"Lỗi đọc file: {ex.Message}";
                    errorCount++;
                }

                _files.Add(row);
            }

            StatusText.Text = errorCount == 0
                ? $"Đã đọc {okCount} file — kiểm tra lại tên truyện/tác giả rồi bấm \"Nhập tất cả\"."
                : $"Đọc {okCount} file thành công, {errorCount} file lỗi (xem chi tiết bên dưới).";

            ImportButton.IsVisible = _files.Any(f => !f.HasError);
        }

        private void OnRemoveFileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ImportFileRow row) return;

            _files.Remove(row);
            ImportButton.IsVisible = _files.Any(f => !f.HasError);
        }

        private async void OnImportClick(object? sender, RoutedEventArgs e)
        {
            var validRows = _files.Where(f => !f.HasError && f.Imported != null).ToList();
            if (validRows.Count == 0) return;

            ImportButton.IsEnabled = false;

            var importedCount = 0;
            var mergedCount = 0;
            var skippedCount = 0;
            var translatedChapters = 0;
            var translationFailed = 0;

            using var db = new MiaoDbContext(Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "miao.db"));
            var knownNovels = db.Novels.ToList();

            foreach (var row in validRows)
            {
                var normalizedNewTitle = NormalizeTitle(row.Title);
                var duplicate = knownNovels.FirstOrDefault(n =>
                    NormalizeTitle(n.Title) == normalizedNewTitle ||
                    NormalizeTitle(n.DisplayTitle) == normalizedNewTitle);

                Novel novel;
                bool isNewNovel;

                if (duplicate != null)
                {
                    var choice = await DialogService.ShowYesNoCancelAsync(
                        $"Đã có truyện trùng tên trong thư viện: \"{duplicate.DisplayTitle}\" (file: {row.FileName}).\n\n" +
                        "Chọn CÓ để gộp các chương trong file này vào truyện đã có (thêm nối tiếp sau chương cuối).\n" +
                        "Chọn KHÔNG nếu đây thực ra là 2 truyện khác nhau trùng tên (tạo truyện mới).\n" +
                        "Chọn HỦY để bỏ qua file này.",
                        "Phát hiện truyện trùng tên");

                    if (choice == DialogResult.Cancel)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (choice == DialogResult.Yes)
                    {
                        novel = duplicate;
                        isNewNovel = false;
                    }
                    else
                    {
                        novel = CreateNovelEntity(row);
                        isNewNovel = true;
                    }
                }
                else
                {
                    novel = CreateNovelEntity(row);
                    isNewNovel = true;
                }

                if (isNewNovel)
                {
                    db.Novels.Add(novel);
                    db.SaveChanges();
                    GlossarySetService.CreateDefaultForNovel(db, novel);
                    knownNovels.Add(novel);
                    importedCount++;

                    await TranslateNovelTitleAndAuthorAsync(db, novel);
                }
                else
                {
                    mergedCount++;
                }

                var nextNumber = isNewNovel
                    ? 1
                    : db.Chapters.Where(c => c.NovelId == novel.Id).Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;

                foreach (var ch in row.Imported!.Chapters)
                {
                    StatusText.Text = $"Đang dịch \"{novel.DisplayTitle}\" — chương {nextNumber}...";

                    var translatedTitle = ch.Title;
                    var displayContent = ch.Content;

                    var titleHasChinese = System.Text.RegularExpressions.Regex.IsMatch(ch.Title, @"\p{IsCJKUnifiedIdeographs}");
                    var contentHasChinese = System.Text.RegularExpressions.Regex.IsMatch(ch.Content, @"\p{IsCJKUnifiedIdeographs}");

                    if (titleHasChinese)
                    {
                        try
                        {
                            var t = (await _titleTranslator.TranslateChapterAsync(ch.Title)).Trim();
                            if (!string.IsNullOrWhiteSpace(t)) translatedTitle = t;
                        }
                        catch
                        {
                            // Giữ nguyên tiêu đề gốc nếu dịch lỗi.
                        }
                    }

                    if (contentHasChinese)
                    {
                        try
                        {
                            var result = await _contentTranslator.TranslateChapterAsync(ch.Content);
                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                displayContent = result;
                                translatedChapters++;
                            }
                            else
                            {
                                translationFailed++;
                            }
                        }
                        catch
                        {
                            translationFailed++;
                        }
                    }

                    db.Chapters.Add(new Chapter
                    {
                        NovelId = novel.Id,
                        Number = nextNumber++,
                        Title = ch.Title,
                        TranslatedTitle = translatedTitle,
                        OriginalContent = ch.Content,
                        DisplayContent = displayContent
                    });
                }

                db.SaveChanges();
            }

            var summary = skippedCount == 0
                ? $"Đã nhập {importedCount} truyện mới, gộp thêm chương cho {mergedCount} truyện đã có."
                : $"Đã nhập {importedCount} truyện mới, gộp {mergedCount} truyện đã có, bỏ qua {skippedCount} file trùng tên.";

            StatusText.Text = translationFailed == 0
                ? $"{summary} Đã dịch {translatedChapters} chương."
                : $"{summary} Đã dịch {translatedChapters} chương; {translationFailed} chương dịch lỗi (giữ nội dung gốc).";

            ImportButton.IsEnabled = true;
            ImportButton.IsVisible = false;

            _files.Clear();
        }

        private async Task TranslateNovelTitleAndAuthorAsync(MiaoDbContext db, Novel novel)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(novel.Title, @"\p{IsCJKUnifiedIdeographs}"))
            {
                try
                {
                    var t = (await _titleTranslator.TranslateChapterAsync(novel.Title)).Trim();
                    if (!string.IsNullOrWhiteSpace(t)) novel.TranslatedTitle = t;
                }
                catch
                {
                    // Không để lỗi dịch tiêu đề làm hỏng việc nhập truyện.
                }
            }

            if (!string.IsNullOrWhiteSpace(novel.Author) &&
                System.Text.RegularExpressions.Regex.IsMatch(novel.Author, @"\p{IsCJKUnifiedIdeographs}"))
            {
                try
                {
                    var a = (await _titleTranslator.TranslateChapterAsync(novel.Author)).Trim();
                    if (!string.IsNullOrWhiteSpace(a)) novel.TranslatedAuthor = a;
                }
                catch
                {
                    // Không để lỗi dịch tác giả làm hỏng việc nhập truyện.
                }
            }

            db.SaveChanges();
        }

        private static Novel CreateNovelEntity(ImportFileRow row) => new()
        {
            Title = row.Title.Trim(),
            Author = row.Author.Trim(),
            SourceUrl = "",
            CoverImagePath = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "Assets", "default-cover.jpg"),
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

    public class ImportFileRow : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);

        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; OnChanged(nameof(Title)); }
        }

        private string _author = "";
        public string Author
        {
            get => _author;
            set { _author = value; OnChanged(nameof(Author)); }
        }

        private string _chapterCountLabel = "";
        public string ChapterCountLabel
        {
            get => _chapterCountLabel;
            set { _chapterCountLabel = value; OnChanged(nameof(ChapterCountLabel)); }
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnChanged(nameof(ErrorMessage)); OnChanged(nameof(HasError)); OnChanged(nameof(ShowFields)); }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        // Avalonia không hỗ trợ "!Binding" trực tiếp trong DataTemplate ở mọi trường hợp
        // nên thêm property tính sẵn thay vì converter (rõ ràng hơn khi maintain)
        public bool ShowFields => !HasError;

        public ImportedNovel? Imported { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));
    }
}