using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    /// <summary>
    /// Nguồn Fanqie Web.
    ///
    /// Không dùng FanqieDecryptService và không gọi API app.
    /// Chỉ lấy dữ liệu mà phiên bản web trả về.
    /// </summary>
    public class FanqieWebDownloadSource : IDownloadSource
    {
        public string SourceName => "fanqienovel-web";

        private readonly IPageFetcher _fetcher;

        public FanqieWebDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url)
        {
            return url.Contains(
                "fanqienovel.com",
                StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // THÔNG TIN TRUYỆN
        // ============================================================

        public async Task<(
            string Title,
            string Author,
            string CoverImageUrl,
            string Description)>
            GetNovelInfoAsync(string url)
        {
            var bookId = ExtractBookId(url);

            if (string.IsNullOrWhiteSpace(bookId))
                return ("", "", "", "");

            try
            {
                var pageUrl =
                    $"https://fanqienovel.com/page/{bookId}";

                var html =
                    await _fetcher.FetchHtmlAsync(pageUrl);

                if (string.IsNullOrWhiteSpace(html))
                    return ("", "", "", "");

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var (title, author, cover) =
                    GetNovelInfoFromJsonLd(doc);

                var description =
                    GetMetaContent(
                        doc,
                        "og:description",
                        "description",
                        "twitter:description");

                if (string.IsNullOrWhiteSpace(title))
                {
                    title =
                        GetMetaContent(
                            doc,
                            "og:title",
                            "twitter:title");
                }

                if (string.IsNullOrWhiteSpace(author))
                {
                    author =
                        GetMetaContent(
                            doc,
                            "author",
                            "og:novel:author",
                            "twitter:creator");
                }

                if (string.IsNullOrWhiteSpace(cover))
                {
                    cover =
                        GetMetaContent(
                            doc,
                            "og:image",
                            "twitter:image");
                }

                if (string.IsNullOrWhiteSpace(author))
                {
                    var authorMatch =
                        Regex.Match(
                            html,
                            "作者[:：]\\s*([^\\s<\"'，,|｜]{1,30})");

                    if (authorMatch.Success)
                    {
                        author =
                            HtmlEntity.DeEntitize(
                                authorMatch.Groups[1].Value)
                            .Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    var titleNode =
                        doc.DocumentNode.SelectSingleNode("//title");

                    if (titleNode != null)
                    {
                        title =
                            HtmlEntity.DeEntitize(
                                titleNode.InnerText)
                            .Trim();
                    }
                }

                title = CleanNovelTitle(title);
                description = CleanNovelDescription(description);

                return
                (
                    title.Trim(),
                    author.Trim(),
                    cover.Trim(),
                    description.Trim()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE WEB] BOOK INFO ERROR: {ex}");

                return ("", "", "", "");
            }
        }

        // ============================================================
        // DANH SÁCH CHƯƠNG
        // ============================================================

        public async Task<
            List<(int Number, string Title, string ChapterUrl)>>
            GetChapterListAsync(string url)
        {
            var result =
                new List<(int, string, string)>();

            var bookId =
                ExtractBookId(url);

            if (string.IsNullOrWhiteSpace(bookId))
                return result;

            try
            {
                var pageUrl =
                    $"https://fanqienovel.com/page/{bookId}";

                var html =
                    await _fetcher.FetchHtmlAsync(pageUrl);

                if (string.IsNullOrWhiteSpace(html))
                    return result;

                var doc =
                    new HtmlDocument();

                doc.LoadHtml(html);

                var chapterNodes =
                    doc.DocumentNode.SelectNodes(
                        "//div[contains(@class,'page-directory-content')]" +
                        "//a[contains(@class,'chapter-item-title')]");

                if (chapterNodes == null)
                    return result;

                var number = 1;

                foreach (var node in chapterNodes)
                {
                    var href =
                        node.GetAttributeValue(
                            "href",
                            "");

                    if (string.IsNullOrWhiteSpace(href))
                        continue;

                    var match =
                        Regex.Match(
                            href,
                            @"/reader/(\d+)",
                            RegexOptions.IgnoreCase);

                    if (!match.Success)
                        continue;

                    var itemId =
                        match.Groups[1].Value;

                    var rawTitle =
                        HtmlEntity.DeEntitize(
                            node.InnerText)
                        .Trim();

                    if (string.IsNullOrWhiteSpace(rawTitle))
                        continue;

                    var title =
                        NormalizeChapterTitle(
                            rawTitle,
                            number);

                    result.Add(
                    (
                        number,
                        title,
                        $"https://fanqienovel.com/reader/{itemId}"
                    ));

                    number++;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE WEB] Tìm thấy {result.Count} chương.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE WEB] CHAPTER LIST ERROR: {ex}");
            }

            return result;
        }

        // ============================================================
        // NỘI DUNG CHƯƠNG
        // ============================================================

        public async Task<string>
            GetChapterContentAsync(
                string chapterUrl)
        {
            try
            {
                var html =
                    await _fetcher.FetchFanqieChapterAsync(
                        chapterUrl);

                if (string.IsNullOrWhiteSpace(html))
                    return "";

                var htmlDoc =
                    new HtmlDocument();

                htmlDoc.LoadHtml(html);

                var contentNode =
                    htmlDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(" +
                        "concat(' ', normalize-space(@class), ' ')," +
                        "' muye-reader-content ')]");

                if (contentNode == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[FANQIE WEB] Không tìm thấy nội dung chương.");

                    return "";
                }

                var rawContent =
                    contentNode.InnerHtml;

                var decoded =
                    FanqiePuaDecoder.Decode(
                        rawContent);

                return CleanContent(decoded);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE WEB] CHAPTER CONTENT ERROR: {ex}");

                return "";
            }
        }

        // ============================================================
        // HELPER
        // ============================================================

        private static (
            string Title,
            string Author,
            string Cover)
            GetNovelInfoFromJsonLd(
                HtmlDocument doc)
        {
            var scripts =
                doc.DocumentNode.SelectNodes(
                    "//script[@type='application/ld+json']");

            if (scripts == null)
                return ("", "", "");

            foreach (var script in scripts)
            {
                var json =
                    script.InnerText;

                if (string.IsNullOrWhiteSpace(json))
                    continue;

                try
                {
                    using var jsonDoc =
                        JsonDocument.Parse(json);

                    var root =
                        jsonDoc.RootElement;

                    var type =
                        root.TryGetProperty(
                            "@type",
                            out var typeElement)
                            ? typeElement.GetString()
                            : null;

                    if (!string.Equals(
                            type,
                            "NewsArticle",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var title =
                        root.TryGetProperty(
                            "headline",
                            out var headline)
                            ? headline.GetString() ?? ""
                            : "";

                    var author = "";

                    if (root.TryGetProperty(
                            "author",
                            out var authorElement))
                    {
                        if (authorElement.ValueKind ==
                            JsonValueKind.Array &&
                            authorElement.GetArrayLength() > 0)
                        {
                            author =
                                authorElement[0]
                                    .TryGetProperty(
                                        "name",
                                        out var name)
                                    ? name.GetString() ?? ""
                                    : "";
                        }
                        else if (
                            authorElement.ValueKind ==
                            JsonValueKind.Object)
                        {
                            author =
                                authorElement
                                    .TryGetProperty(
                                        "name",
                                        out var name)
                                    ? name.GetString() ?? ""
                                    : "";
                        }
                    }

                    var cover = "";

                    if (root.TryGetProperty(
                            "image",
                            out var imageElement))
                    {
                        if (imageElement.ValueKind ==
                            JsonValueKind.Array &&
                            imageElement.GetArrayLength() > 0)
                        {
                            cover =
                                imageElement[0]
                                    .GetString() ?? "";
                        }
                        else if (
                            imageElement.ValueKind ==
                            JsonValueKind.String)
                        {
                            cover =
                                imageElement.GetString() ?? "";
                        }
                    }

                    return
                    (
                        title.Trim(),
                        author.Trim(),
                        cover.Trim()
                    );
                }
                catch (JsonException)
                {
                    // Thử script JSON-LD tiếp theo.
                }
            }

            return ("", "", "");
        }

        private static string GetMetaContent(
            HtmlDocument doc,
            params string[] metaKeys)
        {
            foreach (var key in metaKeys)
            {
                var node =
                    doc.DocumentNode.SelectSingleNode(
                        $"//meta[@property='{key}']") ??
                    doc.DocumentNode.SelectSingleNode(
                        $"//meta[@name='{key}']");

                var content =
                    node?.GetAttributeValue(
                        "content",
                        "") ?? "";

                if (!string.IsNullOrWhiteSpace(content))
                {
                    return
                        HtmlEntity.DeEntitize(
                            content)
                        .Trim();
                }
            }

            return "";
        }

        private static string CleanNovelTitle(
            string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title ?? "";

            var parts =
                Regex.Split(
                        title,
                        @"[-_|｜–—丨_]")
                    .Select(x => x.Trim())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x));

            var candidate =
                parts.FirstOrDefault() ??
                title.Trim();

            string[] keywords =
            {
                "免费阅读",
                "在线阅读",
                "最新章节",
                "全文阅读",
                "完整版",
                "小说网",
                "TXT下载",
                "全本",
                "无弹窗",
                "番茄小说",
                "fanqienovel"
            };

            foreach (var keyword in keywords)
            {
                var index =
                    candidate.IndexOf(
                        keyword,
                        StringComparison.OrdinalIgnoreCase);

                if (index > 0)
                {
                    candidate =
                        candidate[..index]
                        .Trim();
                }
            }

            return candidate.Trim();
        }

        private static string CleanNovelDescription(
            string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return description ?? "";

            var match =
                Regex.Match(
                    description,
                    @"^番茄小说提供.*?小说网[。.]\s*");

            if (match.Success)
            {
                return description[
                    match.Length..]
                    .Trim();
            }

            return description.Trim();
        }

        private static string NormalizeChapterTitle(
            string title,
            int number)
        {
            if (Regex.IsMatch(
                    title,
                    @"^(番外|特别篇|if线)\s*",
                    RegexOptions.IgnoreCase))
            {
                return title;
            }

            var cleanTitle =
                Regex.Replace(
                    title,
                    @"^第[一二三四五六七八九十百千万\d]+章\s*",
                    "")
                .Trim();

            if (string.IsNullOrWhiteSpace(cleanTitle))
                return $"第{number}章";

            return $"第{number}章 {cleanTitle}";
        }

        private static string CleanContent(
            string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "";

            content =
                Regex.Replace(
                    content,
                    @"<header\b[^>]*>.*?</header>",
                    "",
                    RegexOptions.IgnoreCase |
                    RegexOptions.Singleline);

            content =
                Regex.Replace(
                    content,
                    @"<footer\b[^>]*>.*?</footer>",
                    "",
                    RegexOptions.IgnoreCase |
                    RegexOptions.Singleline);

            content =
                Regex.Replace(
                    content,
                    @"</?article\b[^>]*>",
                    "",
                    RegexOptions.IgnoreCase);

            content =
                Regex.Replace(
                    content,
                    @"<img[^>]+(?:src|data-src)\s*=\s*[""']([^""']+)[""'][^>]*>",
                    m => $"\n[[IMG:{m.Groups[1].Value}]]\n",
                    RegexOptions.IgnoreCase);

            content =
                Regex.Replace(
                    content,
                    @"<br\s*/?>",
                    "\n",
                    RegexOptions.IgnoreCase);

            content =
                Regex.Replace(
                    content,
                    @"<p\b[^>]*>",
                    "\n",
                    RegexOptions.IgnoreCase);

            content =
                Regex.Replace(
                    content,
                    @"</p\s*>",
                    "\n",
                    RegexOptions.IgnoreCase);

            content =
                Regex.Replace(
                    content,
                    @"<[^>]+>",
                    "");

            content =
                System.Net.WebUtility.HtmlDecode(
                    content);

            content =
                content
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');

            var lines =
                content
                    .Split('\n')
                    .Select(x => x.Trim())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x));

            return string.Join(
                "\n",
                lines)
                .Trim();
        }

        private static string ExtractBookId(
            string url)
        {
            var match =
                Regex.Match(
                    url,
                    @"(?:book_id=|bookid=|/page/|/book/|/reader/)(\d+)",
                    RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups[1].Value
                : "";
        }
    }
}