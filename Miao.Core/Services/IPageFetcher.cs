using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public interface IPageFetcher
    {
        Task<string> FetchHtmlAsync(string url);
        Task<string> FetchHtmlFastAsync(string url, int waitMs = 500);
        Task<string> FetchFanqieChapterAsync(string readerUrl);
    }
}