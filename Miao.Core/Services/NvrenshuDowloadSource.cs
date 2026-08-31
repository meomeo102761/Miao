using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class NvrenshuDownloadSource : IDownloadSource
    {
        public string SourceName => "nvrenshu";

        private static readonly string[] KnownDomains = { "nvrenshu.com" };

        public bool ProvidesTranslatedContent => false;


        private readonly IPageFetcher _fetcher;


        public NvrenshuDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) 
            => KnownDomains.Any(d => url.Contains(d, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] BoilerplatePatterns =
        {
            "上一章", "章节目录", "下一章",
            "温馨提示", "其他类型推荐阅读",
            "背景", "字色", "字体",
            "首页",
            "投票推荐", "加入书签", "留言反馈",
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
                "//div[@class='bookinfo']//h1[@class='booktitle']"
            });

            var author = FirstNonEmpty(doc, new[]
            {
                "//div[@class='bookinfo']//p[@class='booktag']//a"
            });

            var cover = FirstNonEmpty(doc, new[]
            {
                "//div[@class='bookinfo']//img[@src]",
                "//meta[@property='og:image']"
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
                cover = MakeAbsolute(url, cover);

            var description = FirstNonEmpty(doc,new[]
            {
                "//div[@class='bookinfo']//p[@class='bookintro']"
            });

            return (title, author, cover, description);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var result = new List<(int Number, string Title, string ChapterUrl)>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            const string chapterLinkXPath = "//div[@id='list-chapterAll']//a[@href]";

            var doc = await LoadAsync(url);
            var linkNodes = doc.DocumentNode.SelectNodes(chapterLinkXPath);

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

                result.Add((result.Count + 1, title, chapterUrl));
            }
            return result;
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var doc = await LoadAsync(chapterUrl);

            var node = doc.DocumentNode.SelectSingleNode("//div[@class='readcontent']");

            if (node == null)
                return "";

            return HtmlContentExtractor.ExtractTextWithImages(node, BoilerplatePatterns);
        }

        private static string MakeAbsolute(string baseUrl, string href)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
            {
                return abs.ToString();
            }

            if (Uri.TryCreate(new Uri(baseUrl), href, out var combined))
            {
                return combined.ToString();
            }

            return href;
        }
    }
}