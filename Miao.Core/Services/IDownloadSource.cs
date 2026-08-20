using System.Collections.Generic;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public interface IDownloadSource
    {
        string SourceName { get; }
        bool CanHandle(string url);

        // true nếu nguồn này trả về nội dung ĐÃ SẴN tiếng Việt (vd: site dịch có sẵn
        // như Wikidich) — khi đó app sẽ không chạy máy dịch nữa mà lưu thẳng.
        bool ProvidesTranslatedContent => false;

        // Description = "" nếu nguồn không có phần giới thiệu/tóm tắt truyện.
        Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url);
        Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url);
        Task<string> GetChapterContentAsync(string chapterUrl);
    }
}