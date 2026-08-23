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
                    $"[FANQIE] BOOK INFO ERROR: {ex}");

                return ("", "", "", "");
            }
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

                var match =
                    Regex.Match(
                        html,
                        "<div class=\"muye-reader-content.*?\">(.*?)</div>",
                        RegexOptions.Singleline);

                if (!match.Success)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[FANQIE] FALLBACK: không tìm thấy muye-reader-content " +
                        "trong HTML (trang có thể đã đổi cấu trúc, hoặc bị chặn).");

                    return "";
                }

                var rawContent =
                    match.Groups[1].Value;

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