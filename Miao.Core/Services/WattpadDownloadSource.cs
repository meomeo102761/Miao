using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class WattpadDownloadSource : IDownloadSource
    {
        public string SourceName => "Wattpad";

        private static readonly string[] KnownDomains =
        {
            "wattpad.com",
            "www.wattpad.com"
        };

        public bool ProvidesTranslatedContent => false;

        private readonly IPageFetcher _fetcher;

        public WattpadDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        // NHẬN DIỆN URL

        public bool CanHandle(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return KnownDomains.Any(
                d => url.Contains(
                    d,
                    StringComparison.OrdinalIgnoreCase));
        }

        // API URL

        private static string GetStoryApiUrl(string storyId)
        {
            return
                $"https://www.wattpad.com/api/v3/stories/{storyId}" +
                "?drafts=0" +
                "&mature=1" +
                "&include_deleted=1" +
                "&fields=" +
                "id,title,description,cover,url,user(name,username)," +
                "firstPartId,numParts,parts(id,title,length,url,deleted,draft,createDate)";
        }

        private static string GetPartApiUrl(string partId)
        {
            return
                $"https://www.wattpad.com/apiv2/storytext?id={partId}";
        }

        // LẤY STORY ID

        private static string? ExtractStoryId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            var match = Regex.Match(
                url,
                @"(?:story/)?(\d{5,})",
                RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups[1].Value
                : null;
        }

        // LOAD JSON

        private async Task<JsonDocument?> LoadJsonAsync(string url)
        {
            try
            {
                var html = await _fetcher.FetchHtmlAsync(url);

                if (string.IsNullOrWhiteSpace(html))
                    return null;

                return JsonDocument.Parse(html);
            }
            catch
            {
                return null;
            }
        }

        // HELPER JSON

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

            return property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? ""
                : property.ToString();
        }

        private static int GetInt(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out var property))
            {
                return 0;
            }

            if (property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt32(out var value))
            {
                return value;
            }

            return int.TryParse(
                property.ToString(),
                out var parsed)
                ? parsed
                : 0;
        }

        private static string GetNestedString(
            JsonElement element,
            string parentProperty,
            string childProperty)
        {
            if (!element.TryGetProperty(
                    parentProperty,
                    out var parent))
            {
                return "";
            }

            if (parent.ValueKind != JsonValueKind.Object)
                return "";

            return GetString(parent, childProperty);
        }

        // LẤY THÔNG TIN TRUYỆN

        public async Task<(
            string Title,
            string Author,
            string CoverImageUrl,
            string Description)> GetNovelInfoAsync(string url)
        {
            var storyId = ExtractStoryId(url);

            if (string.IsNullOrWhiteSpace(storyId))
                return ("", "", "", "");

            var apiUrl = GetStoryApiUrl(storyId);

            using var doc = await LoadJsonAsync(apiUrl);

            if (doc == null)
                return ("", "", "", "");

            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("stories", out var stories) &&
                stories.ValueKind == JsonValueKind.Array)
            {
                if (stories.GetArrayLength() == 0)
                    return ("", "", "", "");

                root = stories[0];
            }

            var title = GetString(root, "title");

            var author =
                GetNestedString(root, "user", "name");

            if (string.IsNullOrWhiteSpace(author))
            {
                author =
                    GetNestedString(root, "user", "username");
            }

            var cover =
                GetString(root, "cover");

            var description =
                GetString(root, "description");

            if (!string.IsNullOrWhiteSpace(cover))
                cover = MakeAbsolute(url, cover);

            return (
                HtmlEntity.DeEntitize(title).Trim(),
                HtmlEntity.DeEntitize(author).Trim(),
                cover.Trim(),
                HtmlEntity.DeEntitize(description).Trim());
        }

        // LẤY DANH SÁCH CHƯƠNG

        public async Task<List<(
            int Number,
            string Title,
            string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var result =
                new List<(int Number, string Title, string ChapterUrl)>();

            var storyId = ExtractStoryId(url);

            if (string.IsNullOrWhiteSpace(storyId))
                return result;

            var apiUrl = GetStoryApiUrl(storyId);

            using var doc = await LoadJsonAsync(apiUrl);

            if (doc == null)
                return result;

            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("stories", out var stories) &&
                stories.ValueKind == JsonValueKind.Array)
            {
                if (stories.GetArrayLength() == 0)
                    return result;

                root = stories[0];
            }

            if (!root.TryGetProperty(
                    "parts",
                    out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            var number = 1;

            foreach (var part in parts.EnumerateArray())
            {
                var partId =
                    GetString(part, "id");

                var title =
                    GetString(part, "title");

                var partUrl =
                    GetString(part, "url");

                if (string.IsNullOrWhiteSpace(partId))
                    continue;

                // Bỏ các part đã bị xóa.
                var deleted = false;

                if (part.TryGetProperty(
                        "deleted",
                        out var deletedProperty) &&
                    deletedProperty.ValueKind == JsonValueKind.True)
                {
                    deleted = true;
                }

                if (deleted)
                    continue;

                // Bỏ draft.
                var draft = false;

                if (part.TryGetProperty(
                        "draft",
                        out var draftProperty) &&
                    draftProperty.ValueKind == JsonValueKind.True)
                {
                    draft = true;
                }

                if (draft)
                    continue;

                if (string.IsNullOrWhiteSpace(title))
                    title = $"Chương {number}";

                string chapterUrl;

                if (!string.IsNullOrWhiteSpace(partUrl))
                {
                    chapterUrl =
                        MakeAbsolute(
                            url,
                            partUrl);
                }
                else
                {

                    chapterUrl =
                        GetPartApiUrl(partId);
                }

                result.Add(
                    (
                        number,
                        HtmlEntity.DeEntitize(title).Trim(),
                        chapterUrl
                    ));

                number++;
            }

            return result;
        }

        // LẤY PART ID TỪ CHAPTER URL

        private static string? ExtractPartId(string chapterUrl)
        {
            if (string.IsNullOrWhiteSpace(chapterUrl))
                return null;

            var match = Regex.Match(
                chapterUrl,
                @"[?&]id=(\d+)",
                RegexOptions.IgnoreCase);

            if (match.Success)
                return match.Groups[1].Value;

            match = Regex.Match(
                chapterUrl,
                @"(?:wattpad\.com/)(\d{5,})",
                RegexOptions.IgnoreCase);

            if (match.Success)
                return match.Groups[1].Value;

            // Fallback: tìm số dài đầu tiên.
            match = Regex.Match(
                chapterUrl,
                @"\d{5,}");

            return match.Success
                ? match.Value
                : null;
        }

        // LẤY NỘI DUNG CHƯƠNG

        public async Task<string> GetChapterContentAsync(
            string chapterUrl)
        {
            if (string.IsNullOrWhiteSpace(chapterUrl))
                return "";

            string apiUrl;

            // Nếu chapterUrl đã là API storytext thì dùng luôn.
            if (chapterUrl.Contains(
                    "/apiv2/storytext",
                    StringComparison.OrdinalIgnoreCase))
            {
                apiUrl = chapterUrl;
            }
            else
            {
                var partId =
                    ExtractPartId(chapterUrl);

                if (string.IsNullOrWhiteSpace(partId))
                    return "";

                apiUrl =
                    GetPartApiUrl(partId);
            }

            try
            {
                var html =
                    await _fetcher.FetchHtmlAsync(apiUrl);

                if (string.IsNullOrWhiteSpace(html))
                    return "";

                return ExtractWattpadContent(html);
            }
            catch
            {
                return "";
            }
        }

        // TRÍCH NỘI DUNG PART

        private static string ExtractWattpadContent(
            string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";

            var doc = new HtmlDocument();

            doc.LoadHtml(html);

            // storytext thường trả về fragment HTML trực tiếp.
            // Thử các container thường gặp trước.
            var contentNode =
                doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'panel-reading')]");

            if (contentNode == null)
            {
                contentNode =
                    doc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'story-content')]");
            }

            if (contentNode == null)
            {
                contentNode =
                    doc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'chapter-content')]");
            }

            // Nếu không tìm thấy wrapper thì dùng body.
            if (contentNode == null)
            {
                contentNode =
                    doc.DocumentNode.SelectSingleNode(
                        "//body");
            }

            if (contentNode == null)
                contentNode = doc.DocumentNode;

            return HtmlContentExtractor.ExtractTextWithImages(
                contentNode,
                Array.Empty<string>());
        }

        // URL

        private static string MakeAbsolute(
            string baseUrl,
            string href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return "";

            if (Uri.TryCreate(
                    href,
                    UriKind.Absolute,
                    out var absolute))
            {
                return absolute.ToString();
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