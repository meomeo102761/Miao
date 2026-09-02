using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class WikidichDownloadSource : IDownloadSource
    {
        public string SourceName => "wikidich";

        public bool ProvidesTranslatedContent => true;

        private static readonly string[] KnownDomains =
        {
            "wikicv.org",
            "wikicv.net",
            "wikidich.com",
        };

        private const int MaxChapterCrawl = 3000;
        private const int MaxChapterRetry = 3;
        private const int ChapterRequestDelayMs = 1200;
        private static readonly SemaphoreSlim ChapterSemaphore = new(1, 1);
        private readonly IPageFetcher _fetcher;

        public WikidichDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) =>
            KnownDomains.Any(d =>
                url.Contains(d, StringComparison.OrdinalIgnoreCase));

        private async Task<HtmlDocument> LoadAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }

        private async Task<HtmlDocument> LoadFastAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)>
            GetNovelInfoAsync(string url)
        {
            var doc = await LoadAsync(url);

            var title = HtmlEntity.DeEntitize(
                doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? ""
            ).Trim();

            var author = "";

            var authorNode = doc.DocumentNode.SelectSingleNode(
                "//p[contains(normalize-space(.), 'Tác giả:')]"
            );

            if (authorNode != null)
            {
                var authorLink = authorNode.SelectSingleNode(".//a");

                if (authorLink != null)
                {
                    author = HtmlEntity.DeEntitize(
                        authorLink.InnerText
                    ).Trim();
                }
            }

            var coverNode = doc.DocumentNode.SelectSingleNode(
                "//div[@class='cover-wrapper']//img"
            );

            var coverSrc = coverNode?.GetAttributeValue("src", "") ?? "";

            var cover = string.IsNullOrWhiteSpace(coverSrc)
                ? ""
                : MakeAbsolute(url, coverSrc);

            var descriptionNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'book-desc-detail')]"
            );

            var description = "";

            if (descriptionNode != null)
            {
                var paragraphs = descriptionNode.SelectNodes(".//p");

                if (paragraphs != null)
                {
                    var sb = new StringBuilder();

                    foreach (var p in paragraphs)
                    {
                        var text = HtmlEntity.DeEntitize(
                            p.InnerText
                        ).Trim();

                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        sb.AppendLine(text);
                        sb.AppendLine();
                    }

                    description = sb.ToString().Trim();
                }
            }

            return (title, author, cover, description);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>>
            GetChapterListAsync(string url)
        {
            var result = new List<(int, string, string)>();

            var doc = await LoadAsync(url);

            var volumeNodes = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'volume-list')]//div[contains(@class,'row')]"
            );

            if (volumeNodes == null)
                return result;

            int number = 1;

            foreach (var volumeNode in volumeNodes)
            {
                var chapterNodes = volumeNode.SelectNodes(
                    ".//li[contains(@class,'chapter-name')]//a"
                );

                if (chapterNodes == null)
                    continue;

                foreach (var chapterNode in chapterNodes)
                {
                    if (number > MaxChapterCrawl)
                        return result;

                    var href = chapterNode.GetAttributeValue(
                        "href",
                        ""
                    );

                    if (string.IsNullOrWhiteSpace(href))
                        continue;

                    var title = HtmlEntity.DeEntitize(
                        chapterNode.InnerText
                    ).Trim();

                    if (string.IsNullOrWhiteSpace(title))
                        title = $"Chương {number}";

                    var chapterUrl = MakeAbsolute(url, href);

                    result.Add((
                        number,
                        title,
                        chapterUrl
                    ));

                    number++;
                }
            }

            return result;
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            await ChapterSemaphore.WaitAsync();

            try
            {
                for (var attempt = 1; attempt <= MaxChapterRetry; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            await Task.Delay(ChapterRequestDelayMs);
                        }

                        var doc = await LoadAsync(chapterUrl);

                        var contentNode = doc.DocumentNode.SelectSingleNode(
                            "//div[@id='bookContentBody']"
                        );

                        if (contentNode != null)
                        {
                            var content = ExtractChapterText(contentNode);

                            if (!string.IsNullOrWhiteSpace(content))
                                return content;
                        }

                        if (attempt < MaxChapterRetry)
                        {
                            await Task.Delay(ChapterRequestDelayMs);
                        }
                    }
                    catch
                    {
                        if (attempt >= MaxChapterRetry)
                            throw;

                        await Task.Delay(ChapterRequestDelayMs);
                    }
                }

                return "";
            }
            finally
            {
                ChapterSemaphore.Release();
            }
        }

        private static string ExtractChapterText(HtmlNode contentNode)
        {
            var sb = new StringBuilder();

            var paragraphNodes = contentNode.SelectNodes(".//p");

            if (paragraphNodes == null)
            {
                var fallbackText = HtmlEntity.DeEntitize(
                    contentNode.InnerText
                ).Trim();

                return fallbackText;
            }

            foreach (var p in paragraphNodes)
            {
                var text = HtmlEntity.DeEntitize(
                    p.InnerText
                ).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.Contains(
                    "Wikidich",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sb.AppendLine(text);
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private static string MakeAbsolute(string baseUrl, string href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return "";

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