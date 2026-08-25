using System.Collections.Generic;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public interface IDownloadSource
    {
        string SourceName { get; }
        bool CanHandle(string url);

        bool ProvidesTranslatedContent => false;

        // true: nguồn có số chương cố định từ website
        // false: nguồn chỉ quản lý theo thứ tự bài đăng/nội bộ
        bool UsesSourceChapterNumbers => true;

        Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url);
        Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url);
        Task<string> GetChapterContentAsync(string chapterUrl);
    }
}