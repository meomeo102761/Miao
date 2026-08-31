using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class BiqugeDownloadSource : IDownloadSource
    {
        public string SourceName => "biquge";

        private static readonly string[] KnownDomains =
        {
            "biquge7.xyz",
            "biquge9527.com"
        };

        private readonly IPageFetcher _fetcher;

        public BiqugeDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) =>
            KnownDomains.Any(d => url.Contains(d, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] BoilerplatePatterns =
        {
            "本站所有小说为转载作品",
            "如发现本站有侵犯",
            "联系我们",
            "笔趣阁", 
            "纠错建议",
            "阅读记录",
            "上一章",
            "下一章",
            "章节列表",
            "热门推荐",
            "加载中",
        };

        private async Task<HtmlDocument> LoadAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        private static string? GetMeta(HtmlDocument doc, string property)
        {
            var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")
                       ?? doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");
            var content = node?.GetAttributeValue("content", "");
            return string.IsNullOrWhiteSpace(content) ? null : HtmlEntity.DeEntitize(content).Trim();
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url)
        {
            var doc = await LoadAsync(url);

            var title = GetMeta(doc, "og:novel:book_name")
                        ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim()
                        ?? "";

            var author = GetMeta(doc, "og:novel:author") ?? "";
            var cover = GetMeta(doc, "og:image") ?? "";

            return (title, author, cover, "");
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var doc = await LoadAsync(url);
            var result = new List<(int, string, string)>();

            var bookIdMatch = Regex.Match(url.TrimEnd('/'), @"/(\d+)$");
            if (!bookIdMatch.Success) return result;
            var bookId = bookIdMatch.Groups[1].Value;

            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes == null) return result;

            var seen = new Dictionary<int, (string Title, string Url)>();
            var chapterHrefPattern = new Regex($@"/{bookId}/(\d+)(?:[/?#]|$)");

            foreach (var node in linkNodes)
            {
                var href = node.GetAttributeValue("href", "");
                var match = chapterHrefPattern.Match(href);
                if (!match.Success) continue;

                var number = int.Parse(match.Groups[1].Value);
                var title = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;

                if (!seen.ContainsKey(number) || title.Length > seen[number].Title.Length)
                    seen[number] = (title, MakeAbsolute(url, href));
            }

            foreach (var kv in seen.OrderBy(k => k.Key))
                result.Add((kv.Key, kv.Value.Title, kv.Value.Url));

            return result;
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var doc = await LoadAsync(chapterUrl);

            var candidates = doc.DocumentNode.SelectNodes("//div | //p");
            if (candidates == null) return "";

            HtmlNode? best = null;
            int bestScore = 0;

            foreach (var node in candidates)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText);
                var score = CountHanCharacters(text);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

            if (best == null) return "";

            return HtmlContentExtractor.ExtractTextWithImages(best, BoilerplatePatterns);
        }

        private static int CountHanCharacters(string text)
        {
            int count = 0;
            foreach (var c in text)
                if (c >= 0x4E00 && c <= 0x9FFF) count++;
            return count;
        }

        private static string MakeAbsolute(string baseUrl, string href)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs.ToString();
            if (Uri.TryCreate(new Uri(baseUrl), href, out var combined)) return combined.ToString();
            return href;
        }
    }
}