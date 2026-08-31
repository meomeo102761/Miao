using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class Novel543DownloadSource : IDownloadSource
    {
        public string SourceName => "novel543";

        private static readonly string[] KnownDomains =
        {
            "novel543.com"
        };

        public bool ProvidesTranslatedContent => false;

        public bool UsesSourceChapterNumbers => true;

        private readonly IPageFetcher _fetcher;

        public Novel543DownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) =>
            KnownDomains.Any(d =>
                url.Contains(d, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] BoilerplatePatterns =
        {
            "上一章",
            "目錄",
            "下一章",
            "聯絡我們",
            "首页",
            "溫馨提示",
        };

        private async Task<HtmlDocument> LoadAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }

        private static string FirstNonEmpty(
            HtmlDocument doc,
            IEnumerable<string> xpaths,
            string attribute = "")
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

        public async Task<(
            string Title,
            string Author,
            string CoverImageUrl,
            string Description)> GetNovelInfoAsync(string url)
        {
            var doc = await LoadAsync(url);

            var title = FirstNonEmpty(
                doc,
                new[]
                {
                    "//h1[@class='title']",
                });

            var author = FirstNonEmpty(
                doc,
                new[]
                {
                    "//span[@class='author']",
                });

            var cover = FirstNonEmpty(
                doc,
                new[]
                {
                    "//div[@class='cover']//img",
                },
                attribute: "src");

            if (!string.IsNullOrWhiteSpace(cover))
                cover = MakeAbsolute(url, cover);

            var description = FirstNonEmpty(
                doc,
                new[]
                {
                    "//div[contains(@class, 'intro')]",
                });

            return (
                title,
                author,
                cover,
                description);
        }

        private static bool TryGetChapterNumber(
            string url,
            out int chapterNumber,
            out bool isSubPage)
        {
            chapterNumber = 0;
            isSubPage = false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var fileName = System.IO.Path.GetFileName(uri.AbsolutePath);

            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var match = Regex.Match(
                fileName,
                @"^(.+?)_(\d+)(?:_(\d+))?\.html?$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups[2].Value, out chapterNumber))
                return false;

            isSubPage = match.Groups[3].Success;

            return true;
        }

        private static string MakeSubPageUrl(
            string chapterUrl,
            int pageNumber)
        {
            if (!Uri.TryCreate(
                    chapterUrl,
                    UriKind.Absolute,
                    out var uri))
            {
                return "";
            }

            var path = uri.AbsolutePath;

            var match = Regex.Match(
                path,
                @"^(.*\/)([^\/]+?)_(\d+)\.html?$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return "";

            var directory = match.Groups[1].Value;
            var prefix = match.Groups[2].Value;
            var chapterNumber = match.Groups[3].Value;

            var newPath =
                $"{directory}{prefix}_{chapterNumber}_{pageNumber}.html";

            var builder = new UriBuilder(uri)
            {
                Path = newPath
            };

            return builder.Uri.ToString();
        }

        public async Task<List<(
            int Number,
            string Title,
            string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var result =
                new List<(
                    int Number,
                    string Title,
                    string ChapterUrl)>();

            var seenUrls = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            const string chapterLinkXPath =
                "//ul[contains(@class,'two-700') and contains(@class,'three-900')]//a[@href]";

            const string? nextPageXPath = null;

            var pageUrl = url;

            const int maxPages = 50;

            for (var page = 0;
                 page < maxPages;
                 page++)
            {
                var doc = await LoadAsync(pageUrl);

                var linkNodes =
                    doc.DocumentNode.SelectNodes(chapterLinkXPath);

                if (linkNodes != null)
                {
                    foreach (var node in linkNodes)
                    {
                        var href =
                            node.GetAttributeValue("href", "");

                        if (string.IsNullOrWhiteSpace(href))
                            continue;

                        var chapterUrl =
                            MakeAbsolute(pageUrl, href);

                        if (!seenUrls.Add(chapterUrl))
                            continue;

                        if (!TryGetChapterNumber(
                                chapterUrl,
                                out var number,
                                out var isSubPage))
                        {
                            continue;
                        }

                        if (isSubPage)
                        {
                            continue;
                        }

                        var title =
                            HtmlEntity.DeEntitize(
                                node.InnerText ?? "")
                            .Trim();

                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        result.Add((
                            number,
                            title,
                            chapterUrl));
                    }
                }

                if (string.IsNullOrWhiteSpace(nextPageXPath))
                    break;

                var nextNode =
                    doc.DocumentNode.SelectSingleNode(
                        nextPageXPath);

                var nextHref =
                    nextNode?.GetAttributeValue(
                        "href",
                        "") ?? "";

                if (string.IsNullOrWhiteSpace(nextHref))
                    break;

                var nextUrl =
                    MakeAbsolute(
                        pageUrl,
                        nextHref);

                if (string.Equals(
                        nextUrl,
                        pageUrl,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                pageUrl = nextUrl;
            }

            result = result
                .OrderBy(x => x.Number)
                .ToList();

            return result;
        }

        public async Task<string> GetChapterContentAsync(
            string chapterUrl)
        {
            var parts = new List<string>();

            if (!Uri.TryCreate(
                    chapterUrl,
                    UriKind.Absolute,
                    out var baseUri))
            {
                return "";
            }

            var pageUrls = new List<string>
            {
                chapterUrl
            };

            if (TryGetChapterNumber(
                    chapterUrl,
                    out _,
                    out var isSubPage) &&
                !isSubPage)
            {
                const int maxSubPages = 20;

                for (var pageNumber = 2;
                     pageNumber <= maxSubPages;
                     pageNumber++)
                {
                    var subPageUrl =
                        MakeSubPageUrl(
                            chapterUrl,
                            pageNumber);

                    if (string.IsNullOrWhiteSpace(subPageUrl))
                        break;

                    try
                    {
                        var testDoc =
                            await LoadAsync(subPageUrl);

                        var contentNode =
                            testDoc.DocumentNode
                                .SelectSingleNode(
                                    "//div[contains(@class,'content')]");

                        if (contentNode == null)
                            break;

                        var text =
                            HtmlContentExtractor
                                .ExtractTextWithImages(
                                    contentNode,
                                    BoilerplatePatterns);

                        if (string.IsNullOrWhiteSpace(text))
                            break;

                        pageUrls.Add(subPageUrl);
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            var seenUrls =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var pageUrl in pageUrls)
            {
                if (!seenUrls.Add(pageUrl))
                    continue;

                try
                {
                    var doc =
                        await LoadAsync(pageUrl);

                    var node =
                        doc.DocumentNode.SelectSingleNode(
                            "//div[contains(@class,'content')]");

                    if (node == null)
                        continue;

                    var text =
                        HtmlContentExtractor
                            .ExtractTextWithImages(
                                node,
                                BoilerplatePatterns);

                    if (!string.IsNullOrWhiteSpace(text))
                        parts.Add(text);
                }
                catch
                {
                    
                }
            }

            return string.Join(
                "\n\n",
                parts);
        }

        private static string MakeAbsolute(
            string baseUrl,
            string href)
        {
            if (Uri.TryCreate(
                    href,
                    UriKind.Absolute,
                    out var abs))
            {
                return abs.ToString();
            }

            if (Uri.TryCreate(
                    new Uri(baseUrl),
                    href,
                    out var combined))
            {
                return combined.ToString();
            }

            return href;
        }
    }
}