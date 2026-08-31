using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class CzbooksDownloadSource : IDownloadSource
    {
        public string SourceName => "czbooks";

        private static readonly string[] KnownDomains = { "czbooks.net" };

        public bool ProvidesTranslatedContent => false;

        private readonly IPageFetcher _fetcher;

        private static readonly string[] BoilerplatePatterns =
        {
            "首頁", "繁简轉換",
            "選擇背景顏色", "選擇字體大小",
            "上一章", "下一章",
            "鍵盤左右鍵 ← → 可以切換章節", "聯繫方式"
        };

        public CzbooksDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url)
        {
            return KnownDomains.Any(domain => url.Contains(domain, StringComparison.OrdinalIgnoreCase));
        }

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

                if (node == null)
                    continue;

                var raw = string.IsNullOrEmpty(attribute)
                    ? node.InnerText
                    : node.GetAttributeValue(attribute, "");

                var value = HtmlEntity.DeEntitize(raw ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "";
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url)
        {
            var doc = await LoadAsync(url);

            var title = FirstNonEmpty(doc, new[]
            {
                "//div[contains(@class,'info')]//span[contains(@class,'title')]"
            });

            var author = FirstNonEmpty(doc, new[]
            {
                "//div[contains(@class,'info')]//span[contains(@class,'author')]//a"
            });

            var cover = FirstNonEmpty(doc, new[]
            {
                "//div[contains(@class,'thumbnail')]//img[@src]"
            },
            attribute: "src");

            if (string.IsNullOrWhiteSpace(cover))
            {
                cover = FirstNonEmpty(doc, new[]
                {
                    "//meta[@property='og:image']"
                },
                attribute: "content");
            }

            if (!string.IsNullOrWhiteSpace(cover))
            {
                cover = MakeAbsolute(url, cover);

                if (cover.Contains("default_no_thumbnail", StringComparison.OrdinalIgnoreCase))
                {
                    cover = "";
                }
            }

            var description = FirstNonEmpty(doc, new[]
            {
                "//div[contains(@class,'description')]"
            });

            return (title, author, cover, description);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var result = new List<(int Number, string Title, string ChapterUrl)>();

            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var doc = await LoadAsync(url);

            var linkNodes = doc.DocumentNode.SelectNodes("//ul[@id='chapter-list']//a[@href]");

            if (linkNodes == null)
                return result;

            foreach (var node in linkNodes)
            {
                var href = node.GetAttributeValue("href", "");

                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var chapterUrl = MakeAbsolute(url, href);

                if (!seenUrls.Add(chapterUrl))
                    continue;

                var title = HtmlEntity.DeEntitize(node.InnerText ?? "").Trim();

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var number = result.Count + 1;

                var match = Regex.Match(chapterUrl, @"[?&]chapterNumber=(\d+)", RegexOptions.IgnoreCase);

                if (match.Success && int.TryParse( match.Groups[1].Value, out var parsedNumber))
                {
                    number = parsedNumber + 1;
                }

                result.Add((number, title, chapterUrl));
            }

            return result.OrderBy(x => x.Number).ToList();
        }

        public async Task<string>GetChapterContentAsync(string chapterUrl)
        {
            var doc = await LoadAsync(chapterUrl);

            var node = doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' content ')]");

            if (node == null)
                return "";

            return HtmlContentExtractor.ExtractTextWithImages(node, BoilerplatePatterns);
        }

        private static string MakeAbsolute(string baseUrl, string href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return "";

            if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (Uri.TryCreate(new Uri(baseUrl), href, out var combinedUri))
            {
                return combinedUri.ToString();
            }

            return href;
        }
    }
}