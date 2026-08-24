using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class FanqieDownloadSource : IDownloadSource
    {
        public string SourceName => "fanqienovel";

        private readonly IPageFetcher _fetcher;
        private readonly FanqieDecryptService _decryptService;
        private readonly FanqieClientConfig _config;

        private static readonly HttpClient Http = CreateHttpClient();

        private const string BookApi =
            "https://api5-normal-sinfonlineb.fqnovel.com/reading/bookapi/multi-detail/v/";

        public FanqieDownloadSource(
            IPageFetcher fetcher,
            IScreenshotFetcher screenshotFetcher,
            OcrService ocr)
        {
            _fetcher = fetcher;

            _config =
                FanqieClientConfig.FromEnvironment();

            _decryptService =
                new FanqieDecryptService(_config);
        }

        public bool CanHandle(string url)
        {
            return url.Contains(
                "fanqienovel.com",
                StringComparison.OrdinalIgnoreCase);
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)>
            GetNovelInfoAsync(string url)
        {
            var bookId =
                ExtractBookId(url);

            if (string.IsNullOrWhiteSpace(bookId))
                return ("", "", "", "");

            var (title, author, cover, description) =
                await GetNovelInfoFromApiAsync(
                    bookId,
                    url);

            // Nếu API mobile (hay bị đổi domain/chặn) không trả đủ thông tin,
            // fallback sang parse thẳng thẻ meta của trang /page/{bookId} —
            // trang này chắc chắn tải được vì GetChapterListAsync đang dùng nó.
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(cover))
            {
                var (fbTitle, fbAuthor, fbCover, fbDescription) =
                    await GetNovelInfoFromPageAsync(
                        bookId);

                if (string.IsNullOrWhiteSpace(title)) title = fbTitle;
                if (string.IsNullOrWhiteSpace(author)) author = fbAuthor;
                if (string.IsNullOrWhiteSpace(cover)) cover = fbCover;
                if (string.IsNullOrWhiteSpace(description)) description = fbDescription;
            }

            return (title, author, cover, description);
        }

        private async Task<(string Title, string Author, string CoverImageUrl, string Description)>
            GetNovelInfoFromApiAsync(string bookId, string url)
        {
            try
            {
                var apiUrl =
                    $"{BookApi}?aid=2329&iid=1&version_code=999&book_id={bookId}";

                using var request =
                    CreateRequest(
                        apiUrl,
                        url);

                using var response =
                    await Http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return ("", "", "", "");

                var json =
                    await response.Content.ReadAsStringAsync();

                using var doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "data",
                        out var data))
                {
                    return ("", "", "", "");
                }

                if (data.ValueKind != JsonValueKind.Array ||
                    data.GetArrayLength() == 0)
                {
                    return ("", "", "", "");
                }

                var book =
                    data[0];

                var title =
                    GetString(
                        book,
                        "book_name");

                var author =
                    GetString(
                        book,
                        "author");

                var cover =
                    GetString(
                        book,
                        "thumb_url");

                var description =
                    GetString(
                        book,
                        "abstract");

                return (
                    HtmlEntity.DeEntitize(title).Trim(),
                    HtmlEntity.DeEntitize(author).Trim(),
                    cover.Trim(),
                    HtmlEntity.DeEntitize(description).Trim()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE] BOOK INFO API ERROR, thử fallback trang HTML: {ex}");

                return ("", "", "", "");
            }
        }

        private async Task<(string Title, string Author, string CoverImageUrl, string Description)>
            GetNovelInfoFromPageAsync(string bookId)
        {
            try
            {
                var pageUrl =
                    $"https://fanqienovel.com/page/{bookId}";

                var html =
                    await _fetcher.FetchHtmlAsync(
                        pageUrl);

                if (string.IsNullOrWhiteSpace(html))
                    return ("", "", "", "");

                var doc =
                    new HtmlDocument();

                doc.LoadHtml(html);

                // Nguồn chính: khối <script type="application/ld+json"> chứa
                // structured data (schema.org NewsArticle) mà trang luôn nhúng
                // cho SEO — có title/author/cover đầy đủ, đáng tin cậy hơn hẳn
                // og:title/og:image (Fanqie không dùng OpenGraph tag trên trang
                // /page/{bookId} nên 2 tag đó không hề tồn tại).
                var (ldTitle, ldAuthor, ldCover) = GetNovelInfoFromJsonLd(doc);

                var title = ldTitle;
                var author = ldAuthor;
                var cover = ldCover;

                var description =
                    GetMetaContent(doc, "og:description", "description", "twitter:description");

                // Dự phòng: nếu vì lý do gì đó trang không có JSON-LD, thử
                // meta tag chuẩn rồi mới tới regex mò chữ "作者:".
                if (string.IsNullOrWhiteSpace(title))
                    title = GetMetaContent(doc, "og:title", "twitter:title");

                if (string.IsNullOrWhiteSpace(cover))
                    cover = GetMetaContent(doc, "og:image", "twitter:image");

                if (string.IsNullOrWhiteSpace(author))
                    author = GetMetaContent(doc, "author", "og:novel:author", "twitter:creator");

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
                        title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
                }

                // headline/og:title/<title> đều có thể dính hậu tố quảng cáo SEO
                // (VD "《Tên》完整版在线免费阅读_《Tên》小说_番茄小说官网") — dọn chung.
                title = CleanNovelTitle(title);

                // description có tiền tố quảng cáo cố định kiểu
                // "番茄小说提供{tên truyện}完整版在线免费阅读，精彩小说尽在番茄小说网。"
                // trước phần tóm tắt thật — cắt bỏ tiền tố này nếu có.
                description = CleanNovelDescription(description);

                return (
                    title.Trim(),
                    author.Trim(),
                    cover.Trim(),
                    description.Trim()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE] BOOK INFO PAGE FALLBACK ERROR: {ex}");

                return ("", "", "", "");
            }
        }

                private static readonly string[] TitleJunkKeywords =
        {
            "免费阅读", "在线阅读", "最新章节", "全文阅读", "完整版",
            "小说网", "TXT下载", "全本", "无弹窗", "番茄小说", "fanqienovel"
        };

        // Fanqie hay đặt <title>/og:title theo kiểu SEO nhồi từ khoá, ví dụ
        // "《Tên truyện》最新章节_《Tên truyện》全文阅读_番茄小说" — tên thật luôn
        // nằm ở đoạn đầu tiên trước dấu phân cách hoặc từ khoá rác đầu tiên.
        private static string CleanNovelTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title ?? "";

            var parts =
                Regex.Split(title, @"[-_|｜–—丨_]")
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p));

            var candidate = parts.FirstOrDefault() ?? title.Trim();

            foreach (var keyword in TitleJunkKeywords)
            {
                var index = candidate.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                    candidate = candidate.Substring(0, index).Trim();
            }

            return candidate.Trim();
        }

        private static (string Title, string Author, string Cover) GetNovelInfoFromJsonLd(HtmlDocument doc)
        {
            var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (scripts == null)
                return ("", "", "");

            foreach (var script in scripts)
            {
                var json = script.InnerText;
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                try
                {
                    using var doc2 = JsonDocument.Parse(json);
                    var root = doc2.RootElement;

                    var type = root.TryGetProperty("@type", out var typeEl) ? typeEl.GetString() : null;
                    if (!string.Equals(type, "NewsArticle", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var title = root.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "";

                    var author = "";
                    if (root.TryGetProperty("author", out var authorEl))
                    {
                        if (authorEl.ValueKind == JsonValueKind.Array && authorEl.GetArrayLength() > 0)
                            author = authorEl[0].TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        else if (authorEl.ValueKind == JsonValueKind.Object)
                            author = authorEl.TryGetProperty("name", out var n2) ? n2.GetString() ?? "" : "";
                    }

                    var cover = "";
                    if (root.TryGetProperty("image", out var imageEl))
                    {
                        if (imageEl.ValueKind == JsonValueKind.Array && imageEl.GetArrayLength() > 0)
                            cover = imageEl[0].GetString() ?? "";
                        else if (imageEl.ValueKind == JsonValueKind.String)
                            cover = imageEl.GetString() ?? "";
                    }

                    return (title.Trim(), author.Trim(), cover.Trim());
                }
                catch (JsonException)
                {
                    // Khối ld+json này không parse được (hoặc không đúng dạng
                    // mong đợi) -> thử khối tiếp theo nếu có.
                }
            }

            return ("", "", "");
        }

        private static string CleanNovelDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return description ?? "";

            // "番茄小说提供{tên truyện}完整版在线免费阅读，精彩小说尽在番茄小说网。{tóm tắt thật}"
            var match = Regex.Match(description, @"^番茄小说提供.*?小说网[。.]\s*");
            if (match.Success)
                return description.Substring(match.Length).Trim();

            return description.Trim();
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
                    node?.GetAttributeValue("content", "") ?? "";

                if (!string.IsNullOrWhiteSpace(content))
                {
                    return HtmlEntity.DeEntitize(content).Trim();
                }
            }

            return "";
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>>
            GetChapterListAsync(string url)
        {
            var result =
                new List<(int, string, string)>();

            var bookId =
                ExtractBookId(url);

            if (string.IsNullOrWhiteSpace(bookId))
                return result;

            var pageUrl =
                $"https://fanqienovel.com/page/{bookId}";

            try
            {
                var html =
                    await _fetcher.FetchHtmlAsync(
                        pageUrl);

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
                    $"[FANQIE] Tìm thấy {result.Count} chương.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FANQIE] CHAPTER LIST ERROR: {ex}");
            }

            return result;
        }

        public async Task<string> GetChapterContentAsync(
            string chapterUrl)
        {
            var itemId =
                ExtractItemId(chapterUrl);

            if (string.IsNullOrWhiteSpace(itemId))
                return "";

            // Đường 1: API giải mã "chính thức" (AES + gunzip) — chỉ dùng được
            // khi đã cấu hình MIAO_FANQIE_REG_KEY, nên bỏ qua sớm nếu chưa có
            // để khỏi tốn 1 request chắc chắn lỗi.
            if (!string.IsNullOrWhiteSpace(_config.RegKey))
            {
                try
                {
                    var content =
                        await _decryptService.GetChapterContentAsync(
                            itemId);

                    if (!string.IsNullOrWhiteSpace(content))
                        return CleanContent(content);

                    System.Diagnostics.Debug.WriteLine(
                        "[FANQIE] DECRYPT trả về rỗng, thử fallback HTML.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FANQIE] DECRYPT ERROR, thử fallback HTML: {ex}");
                }
            }

            // Đường 2 (fallback, KHÔNG cần REG_KEY): mở thẳng trang đọc bằng
            // trình duyệt thật (WebView2, qua IPageFetcher.FetchFanqieChapterAsync),
            // lấy phần nội dung thô rồi giải mã bảng thế ký tự PUA.
            // Lưu ý: Miao không track được chương nào VIP/free (chapter list
            // của app không có cờ này) nên đường này chạy cho MỌI chương.
            // Trên thực tế đa số web đọc kiểu Fanqie chỉ khóa ở lớp giao diện —
            // HTML gốc vẫn chứa sẵn nội dung (đã obfuscate bằng PUA) kể cả
            // chương bị khóa, nên fallback này thường đọc được cả chương VIP.
            // Nếu về sau Fanqie thắt chặt và chặn thật ở server, đường này sẽ
            // trả về rỗng cho chương VIP và cần quay lại dùng REG_KEY/API ngoài.
            return await GetChapterContentViaHtmlFallbackAsync(
                chapterUrl);
        }

        private async Task<string> GetChapterContentViaHtmlFallbackAsync(
            string chapterUrl)
        {
            try
            {
                var html =
                    await _fetcher.FetchFanqieChapterAsync(
                        chapterUrl);

                if (string.IsNullOrWhiteSpace(html))
                    return "";

                // QUAN TRỌNG: không dùng regex "<div ...>(.*?)</div>" để tách nội
                // dung — non-greedy sẽ dừng ở thẻ </div> ĐẦU TIÊN gặp phải, mà
                // ảnh trong chương thường được bọc trong 1 <div> con riêng
                // (vd <div class="img-wrapper"><img .../></div>), khiến toàn bộ
                // nội dung từ sau ảnh trở đi bị cắt mất. Parse bằng HtmlAgilityPack
                // (dựng cây DOM thật) để bắt đúng thẻ đóng tương ứng, xử lý được
                // div lồng nhau.
                var htmlDoc =
                    new HtmlDocument();

                htmlDoc.LoadHtml(html);

                var contentNode =
                    htmlDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(concat(' ', normalize-space(@class), ' '), ' muye-reader-content ')]");

                if (contentNode == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[FANQIE] FALLBACK: không tìm thấy muye-reader-content " +
                        "trong HTML (trang có thể đã đổi cấu trúc, chưa render kịp, hoặc bị chặn).");

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
                    $"[FANQIE] FALLBACK HTML ERROR: {ex}");

                return "";
            }
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
                    m =>
                        $"\n[[IMG:{m.Groups[1].Value}]]\n",
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
                FixQuotes(content);

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
                    "");

            cleanTitle =
                cleanTitle.Trim();

            if (string.IsNullOrWhiteSpace(cleanTitle))
                return $"第{number}章";

            return
                $"第{number}章 {cleanTitle}";
        }

        private static string FixQuotes(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (!text.Contains('＂'))
                return text;

            var normalized =
                text
                    .Replace('＂', '"')
                    .Replace('“', '"')
                    .Replace('”', '"');

            var quoteCount =
                normalized.Count(
                    c => c == '"');

            if (quoteCount % 2 == 0)
            {
                var open = true;

                return Regex.Replace(
                    normalized,
                    "\"",
                    _ =>
                    {
                        var result =
                            open
                                ? "“"
                                : "”";

                        open = !open;

                        return result;
                    });
            }

            var isOpen = true;

            var lastOpen =
                text.LastIndexOf('“');

            var lastClose =
                text.LastIndexOf('”');

            if (lastOpen >= 0 ||
                lastClose >= 0)
            {
                isOpen =
                    lastClose > lastOpen;
            }

            return Regex.Replace(
                text,
                "＂",
                _ =>
                {
                    var result =
                        isOpen
                            ? "“"
                            : "”";

                    isOpen = !isOpen;

                    return result;
                });
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

        private static string ExtractItemId(
            string url)
        {
            var match =
                Regex.Match(
                    url,
                    @"/reader/(\d+)",
                    RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups[1].Value
                : "";
        }

        private static string GetString(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out var property))
            {
                return "";
            }

            return property.ValueKind ==
                   JsonValueKind.String
                ? property.GetString() ?? ""
                : "";
        }

        private static HttpRequestMessage CreateRequest(
            string url,
            string referer)
        {
            var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/150.0.0.0 Safari/537.36");

            if (Uri.TryCreate(
                    referer,
                    UriKind.Absolute,
                    out var refererUri))
            {
                request.Headers.Referrer =
                    refererUri;
            }

            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/json,text/plain,*/*");

            return request;
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(30)
            };
        }
    }
}