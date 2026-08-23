using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    // ======================================================================
    // KHUÔN MẪU THÊM 1 NGUỒN TẢI MỚI
    // ======================================================================
    // Copy nguyên file này, đổi tên FILE + tên CLASS theo site (vd:
    // TruyenFullDownloadSource), rồi chỉ sửa 6 chỗ đánh dấu "// ĐỔI Ở ĐÂY"
    // bên dưới. Phần còn lại (fetch HTML, lật trang, trích nội dung...) đã
    // viết sẵn, thường không cần đụng vào.
    // ======================================================================
    public class TrangMauDownloadSource : IDownloadSource
    {
        // ĐỔI Ở ĐÂY 1: tên hiển thị khi báo lỗi "chưa hỗ trợ trang này"
        public string SourceName => "trangmau";

        // ĐỔI Ở ĐÂY 2: domain để nhận diện link có thuộc site này không
        private static readonly string[] KnownDomains = { "trangmau.com" };

        // true nếu site đã có sẵn bản tiếng Việt (không cần dịch lại nội dung/tiêu đề)
        public bool ProvidesTranslatedContent => false;

        private readonly IPageFetcher _fetcher;

        public TrangMauDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) =>
            KnownDomains.Any(d => url.Contains(d, StringComparison.OrdinalIgnoreCase));

        // ĐỔI Ở ĐÂY 3: các dòng rác cố định (bản quyền, quảng cáo, "chương trước/sau"...)
        // cần lọc khỏi nội dung chương khi trích xuất
        private static readonly string[] BoilerplatePatterns =
        {
            "Nguồn: trangmau.com",
            "Chương trước",
            "Chương sau",
        };

        private async Task<HtmlDocument> LoadAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        // Thử từng XPath theo thứ tự, lấy kết quả không rỗng đầu tiên.
        // attribute rỗng ("") thì lấy InnerText; khác rỗng thì lấy giá trị attribute đó
        // (dùng cho <meta content="..."> hoặc <img src="...">).
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
                "//meta[@property='og:novel:author']/@content",
            });

            var cover = FirstNonEmpty(doc, new[]
            {
               "//meta[@property='og:image']/@content",
            }, attribute: "content"); 
            if (!string.IsNullOrWhiteSpace(cover)) cover = MakeAbsolute(url, cover);

            var description = FirstNonEmpty(doc, new[]
            {
                "//div[@class='book-intro']",
            });

            return (title, author, cover, description);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var result = new List<(int Number, string Title, string ChapterUrl)>();
            var seenUrls = new HashSet<string>();

            // ĐỔI Ở ĐÂY 5a: XPath tới các thẻ <a> trong danh sách chương
            const string chapterLinkXPath = "//div[@id='chapter-list']//a[@href]";

            // ĐỔI Ở ĐÂY 5b: nếu site đánh số chương ngay trong href (vd .../chuong-45),
            // điền regex group 1 = số chương vào đây. Để null nếu không có, sẽ tự đánh
            // số 1,2,3... theo thứ tự xuất hiện.
            const string? chapterNumberPattern = null; // ví dụ: @"/chuong-(\d+)"
            var chapterNumberRegex = chapterNumberPattern == null ? null : new Regex(chapterNumberPattern);

            // ĐỔI Ở ĐÂY 5c: nếu danh sách chương chia nhiều trang, điền XPath tới link
            // "trang sau" vào đây (vd "//a[@rel='next']"). Để null nếu chỉ có 1 trang.
            const string? nextPageXPath = null;

            var pageUrl = url;
            const int maxPages = 50; // chốt an toàn tránh lặp vô hạn nếu nextPageXPath cấu hình sai

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
                        if (!seenUrls.Add(chapterUrl)) continue; // tránh trùng nếu 1 link xuất hiện 2 lần

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

            // ĐỔI Ở ĐÂY 6: XPath tới khối chứa nội dung chương
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