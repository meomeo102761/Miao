using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class Sixty9ShubaDownloadSource : IDownloadSource
    {
        public string SourceName => "69shuba";
        private readonly IPageFetcher _fetcher;

        public Sixty9ShubaDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) => url.Contains("69shuba.com");

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var titleNode = doc.DocumentNode.SelectSingleNode(
                "//a[contains(@href,'/book/') and contains(@href,'.htm')]");
            var title = titleNode?.InnerText.Trim() ?? "";

            var authorNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'author.php')]");
            var author = authorNode?.InnerText.Trim() ?? "";

            var coverNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bookimg2')]//img");
            var coverUrl = coverNode?.GetAttributeValue("src", "") ?? "";

            return (title, author, coverUrl, "");
        }

        public async Task<List<(int, string, string)>> GetChapterListAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = new List<(int, string, string)>();
            var chapterNodes = doc.DocumentNode.SelectNodes("//a[contains(@href,'/txt/')]");
            if (chapterNodes == null) return result;

            int index = 1;
            foreach (var node in chapterNodes)
            {
                var chapterUrl = node.GetAttributeValue("href", "");
                var spanNode = node.SelectSingleNode(".//span");
                var chapterTitle = spanNode?.InnerText.Trim() ?? node.InnerText.Trim();
                result.Add((index++, chapterTitle, chapterUrl));
            }
            return result;
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var html = await _fetcher.FetchHtmlAsync(chapterUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var navNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'txtnav')]");
            if (navNode == null) return "";

            return HtmlContentExtractor.ExtractTextWithImages(navNode);
        }
    }
}