using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class NvrenshuDowloadSource : IDownloadSource
    {
        public string SourceName => "nvrenshu";

        private static readonly string[] KnownDomains = { "nvrenshu.com" };

        public bool ProvidesTranslatedContent => false;

        private readonly IPageFetcher _fetcher;

        public NvrenshuDowloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) =>
            KnownDomains.Any(d => url.Contains(d, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] BoilerplatePatterns =
        {
            "背景：", "字色：", "字体: ", "[小中大]",
            "落地成盒", "投票推荐", "加入书签", "留言反馈",
            "温馨提示：按 回车[Enter]键 返回书目，按 ←键 返回上一页， 按 →键 进入下一页，加入书签方便您下次继续阅读。", 
            "其他类型推荐阅读：",
        };

        private async Task<HtmlDocument> LoadAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        private static string FirstNonEmpty(HtmlDocument doc, IEnumerable<string> xpaths, string attribute = "")
        {
            foreach (var xpath in xpaths)
            {
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node == null) continue;

                var raw = string.IsNullOrEmpty(attribute) ? node.InnerText : node.GetAttributeValue(attribute, "");
                var value = HtmlEntity.DeEntitize(raw ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return "";
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url)
        {
            var doc = await LoadAsync(url);

            var title = FirstNonEmpty(doc, new[]
            {
                "//h1[@class='booktitle']",
            });

            var author = FirstNonEmpty(doc, new[]
            {
                "//a[@class='red']",
            });

            var cover = FirstNonEmpty(doc, new[]
            {
                "//div[@class='cover']//img/@src",
                "//meta[@property='og:image']/@content",
            });
            if (!string.IsNullOrWhiteSpace(cover)) cover = MakeAbsolute(url, cover);

            var description = FirstNonEmpty(doc, new[]
            {
                "//p[@class='bookintro']",
            });

            return (title, author, cover, description);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var result = new List<(int Number, string Title, string ChapterUrl)>();
            var seenUrls = new HashSet<string>();

            const string chapterLinkXPath = "//div[@id='list-chapterAll']//a[@href]";

            const string? chapterNumberPattern = null;
            var chapterNumberRegex = chapterNumberPattern == null ? null : new Regex(chapterNumberPattern);

            const string? nextPageXPath = null;

            var pageUrl = url;
            const int maxPages = 50;

            for (var page = 0; page < maxPages; page++)
            {
                var doc = await LoadAsync(pageUrl);
                var linkNodes = doc.DocumentNode.SelectNodes(chapterLinkXPath);

                if (linkNodes != null)
                {
                    foreach (var node in linkNodes)
                    {
                        var href = node.GetAttributeValue("href", "");
                        if (string.IsNullOrWhiteSpace(href)) continue;

                        var chapterUrl = MakeAbsolute(pageUrl, href);
                        if (!seenUrls.Add(chapterUrl)) continue; 

                        var title = HtmlEntity.DeEntitize(node.InnerText).Trim();
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var number = result.Count + 1;
                        if (chapterNumberRegex != null)
                        {
                            var match = chapterNumberRegex.Match(href);
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                                number = parsed;
                        }

                        result.Add((number, title, chapterUrl));
                    }
                }

                if (string.IsNullOrWhiteSpace(nextPageXPath)) break;

                var nextNode = doc.DocumentNode.SelectSingleNode(nextPageXPath);
                var nextHref = nextNode?.GetAttributeValue("href", "") ?? "";
                if (string.IsNullOrWhiteSpace(nextHref)) break;

                var nextUrl = MakeAbsolute(pageUrl, nextHref);
                if (nextUrl == pageUrl) break;
                pageUrl = nextUrl;
            }

            if (chapterNumberRegex == null)
            {
                for (var i = 0; i < result.Count; i++)
                    result[i] = (i + 1, result[i].Title, result[i].ChapterUrl);
            }

            return result;
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var doc = await LoadAsync(chapterUrl);

            var node = doc.DocumentNode.SelectSingleNode("//div[@class='chapter-content']");
            if (node == null) return "";

            return HtmlContentExtractor.ExtractTextWithImages(node, BoilerplatePatterns);
        }

        private static string MakeAbsolute(string baseUrl, string href)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs.ToString();
            if (Uri.TryCreate(new Uri(baseUrl), href, out var combined)) return combined.ToString();
            return href;
        }
    }
}