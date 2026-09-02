using System;
using System.Collections.Generic;
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
        private ImportFileRow? _unmergingRow;

        public DownloadFilePage()
        {
            InitializeComponent();
            FilesList.ItemsSource = _files;
        }

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
                ? $"Đã đọc {okCount} file — kiểm tra lại tên truyện/tác giả rồi bấm \"Nhập tất cả\", hoặc tick chọn nhiều file để gộp thành 1 truyện."
                : $"Đọc {okCount} file thành công, {errorCount} file lỗi (xem chi tiết bên dưới).";

            UpdateActionButtonsVisibility();
        }

        private void OnRemoveFileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ImportFileRow row) return;

            _files.Remove(row);
            UpdateActionButtonsVisibility();
        }

        private void OnFileCheckToggled(object? sender, RoutedEventArgs e) => UpdateActionButtonsVisibility();

        private void OnSelectAllFilesClick(object? sender, RoutedEventArgs e)
        {
            var selectable = _files.Where(f => !f.HasError).ToList();
            var shouldSelectAll = SelectAllCheckBox.IsChecked == true;

            foreach (var f in selectable)
                f.IsSelected = shouldSelectAll;

            UpdateActionButtonsVisibility();
        }

        private void UpdateActionButtonsVisibility()
        {
            var selectable = _files.Where(f => !f.HasError).ToList();

            ImportButton.IsVisible = _files.Any(f => !f.HasError);
            SelectAllCheckBox.IsVisible = selectable.Count >= 2;
            MergeButton.IsVisible = selectable.Count(f => f.IsSelected) >= 2;

            if (selectable.Count > 0)
                SelectAllCheckBox.IsChecked = selectable.All(f => f.IsSelected);
        }

        private void OnMergeSelectedFilesClick(object? sender, RoutedEventArgs e)
        {
            var selected = _files.Where(f => f.IsSelected && !f.HasError && f.Imported != null).ToList();

            if (selected.Count < 2)
            {
                StatusText.Text = "Hãy tick chọn ít nhất 2 file hợp lệ để gộp thành 1 truyện.";
                return;
            }

            var ordered = selected.OrderBy(f => f.FileName, NaturalStringComparer.Instance).ToList();

            var insertIndex = _files.IndexOf(ordered[0]);
            var totalChapters = ordered.Sum(f => f.Imported!.Chapters.Count);

            var mergedRow = new ImportFileRow
            {
                FilePath = string.Join(" + ", ordered.Select(f => f.FileName)),
                Title = ordered[0].Title,
                Author = ordered[0].Author,
                ChapterCountLabel = $"Đã gộp từ {ordered.Count} file, theo thứ tự: {string.Join(" → ", ordered.Select(f => f.FileName))} — tổng {totalChapters} chương.",
                Imported = ordered[0].Imported,
                MergedRows = ordered
            };

            foreach (var row in ordered)
                _files.Remove(row);

            _files.Insert(Math.Clamp(insertIndex, 0, _files.Count), mergedRow);

            StatusText.Text = $"Đã gộp {ordered.Count} file thành 1 truyện — kiểm tra lại tên truyện/tác giả và thứ tự file trước khi nhập. Bấm \"Tách\" nếu gộp nhầm.";
            UpdateActionButtonsVisibility();
        }

        private void OnOpenUnmergeDialogClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ImportFileRow row || row.MergedRows == null) return;

            _unmergingRow = row;

            UnmergeCandidatesList.ItemsSource = row.MergedRows
                .Select(r => new UnmergeCandidate { FileName = r.FileName, SourceRow = r, IsSelected = false })
                .ToList();

            UnmergeCard.IsVisible = true;
            if (UnmergeCard.Parent is Panel panel) panel.Children.Remove(UnmergeCard);
            ModalService.Show(UnmergeCard);
        }

        private void OnCancelUnmergeClick(object? sender, RoutedEventArgs e)
        {
            _unmergingRow = null;
            ModalService.Close();
        }

        private void OnConfirmUnmergeClick(object? sender, RoutedEventArgs e)
        {
            if (_unmergingRow?.MergedRows == null || UnmergeCandidatesList.ItemsSource is not IEnumerable<UnmergeCandidate> candidates)
            {
                ModalService.Close();
                return;
            }

            var toExtract = candidates.Where(c => c.IsSelected).Select(c => c.SourceRow).ToList();
            if (toExtract.Count == 0)
            {
                StatusText.Text = "Chưa chọn file nào để tách.";
                return;
            }

            var mergedRow = _unmergingRow;
            var remaining = mergedRow.MergedRows!.Except(toExtract).ToList();
            var index = _files.IndexOf(mergedRow);

            _files.Remove(mergedRow);

            var insertAt = index;

            if (remaining.Count <= 1)
            {
                var all = toExtract.Concat(remaining).ToList();
                foreach (var child in all)
                {
                    child.IsSelected = false;
                    _files.Insert(insertAt, child);
                    insertAt++;
                }

                StatusText.Text = remaining.Count == 0
                    ? $"Đã tách {toExtract.Count} file khỏi nhóm — nhóm gộp không còn file nào."
                    : $"Đã tách {toExtract.Count} file — chỉ còn 1 file nên nhóm gộp được giải tán.";
            }
            else
            {
                foreach (var child in toExtract)
                {
                    child.IsSelected = false;
                    _files.Insert(insertAt, child);
                    insertAt++;
                }

                mergedRow.MergedRows = remaining;
                mergedRow.FilePath = string.Join(" + ", remaining.Select(r => r.FileName));
                mergedRow.ChapterCountLabel =
                    $"Đã gộp từ {remaining.Count} file, theo thứ tự: {string.Join(" → ", remaining.Select(r => r.FileName))} — tổng {remaining.Sum(r => r.Imported!.Chapters.Count)} chương.";

                _files.Insert(insertAt, mergedRow);

                StatusText.Text = $"Đã tách {toExtract.Count} file khỏi nhóm — nhóm gộp còn lại {remaining.Count} file.";
            }

            _unmergingRow = null;
            ModalService.Close();
            UpdateActionButtonsVisibility();
        }

        private sealed class NaturalStringComparer : IComparer<string>
        {
            public static readonly NaturalStringComparer Instance = new();

            public int Compare(string? a, string? b)
            {
                a ??= ""; b ??= "";
                int i = 0, j = 0;

                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;

                        var na = a[si..i].TrimStart('0');
                        var nb = b[sj..j].TrimStart('0');

                        if (na.Length != nb.Length) return na.Length - nb.Length;
                        var numCmp = string.CompareOrdinal(na, nb);
                        if (numCmp != 0) return numCmp;
                    }
                    else
                    {
                        var cmp = a[i].CompareTo(b[j]);
                        if (cmp != 0) return cmp;
                        i++; j++;
                    }
                }

                return (a.Length - i) - (b.Length - j);
            }
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

                int nextNumber;
                if (isNewNovel)
                {
                    nextNumber = 1;
                }
                else
                {
                    var existingChapterNumbers = db.Chapters
                        .Where(c => c.NovelId == novel.Id)
                        .Select(c => c.Number)
                        .ToList();
                    nextNumber = existingChapterNumbers.Count == 0 ? 1 : existingChapterNumbers.Max() + 1;
                }

                var chaptersToImport = row.MergedRows != null
                    ? row.MergedRows.SelectMany(r => r.Imported!.Chapters)
                    : row.Imported!.Chapters;

                foreach (var ch in chaptersToImport)
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

    public class UnmergeCandidate
    {
        public string FileName { get; set; } = "";
        public bool IsSelected { get; set; }
        public ImportFileRow SourceRow { get; set; } = null!;
    }

    public class ImportFileRow : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnChanged(nameof(IsSelected)); }
        }

        public List<ImportFileRow>? MergedRows { get; set; }
        public bool IsMerged => MergedRows != null;

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

        public bool ShowFields => !HasError;

        public ImportedNovel? Imported { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));
    }
}