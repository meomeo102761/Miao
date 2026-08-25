using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public class FanqieTcDownloadSource : IDownloadSource
    {
        public string SourceName => "FanqieTC";

        private const string Domain = "fanqietc.com";
        private const string ApiBaseUrl = "https://api.fanqietc.com/proxy?api=default";

        private static readonly HttpClient Http = new();

        public bool ProvidesTranslatedContent => false;

        public bool CanHandle(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   uri.Host.Equals(Domain, StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // Lấy bookId từ URL
        // Ví dụ:
        // https://fanqietc.com/catalog?bookId=7279292199060114444
        // ============================================================

        private static string GetBookId(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "";

            var query = uri.Query.TrimStart('?');

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);

                if (pair.Length == 2 &&
                    pair[0].Equals("bookId", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return "";
        }

        // ============================================================
        // GET API
        // ============================================================

        private const string ApiToken = "fqtc_7nKp2mQ8xR4vL6wT1yZ3bC5dF0hJ8aE9uI3kM7";

        private static async Task<string> GetApiAsync(string action, string parameterName, string parameterValue)
        {
            var url =
                $"{ApiBaseUrl}" +
                $"&action={Uri.EscapeDataString(action)}" +
                $"&{parameterName}={Uri.EscapeDataString(parameterValue)}";

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                url);

            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/150.0.0.0 Safari/537.36");

            request.Headers.Accept.ParseAdd("*/*");

            request.Headers.Referrer =
                new Uri("https://fanqietc.com/");

            request.Headers.Add(
                "Origin",
                "https://fanqietc.com");

            request.Headers.Add(
                "x-api-token",
                ApiToken);

            using var response =
                await Http.SendAsync(request);

            var content =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"FanqieTC API trả về {(int)response.StatusCode} " +
                    $"{response.ReasonPhrase}: {content}");
            }

            return content;
        }

        // ============================================================
        // Thông tin truyện
        // ============================================================

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)>
            GetNovelInfoAsync(string url)
        {
            var bookId = GetBookId(url);

            if (string.IsNullOrWhiteSpace(bookId))
                throw new Exception("Không tìm thấy bookId trong link FanqieTC.");

            var json = await GetApiAsync(
                "detail",
                "book_id",
                bookId);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data))
                throw new Exception("FanqieTC không trả về thông tin truyện.");

            var title = GetString(data, "book_name");

            if (string.IsNullOrWhiteSpace(title))
                title = GetString(data, "original_book_name");

            var author = GetString(data, "author");

            var cover = GetString(data, "thumb_url");

            var description = GetString(data, "abstract");

            return (
                title,
                author,
                cover,
                description
            );
        }

        // ============================================================
        // Danh sách chương
        // ============================================================

        public async Task<List<(int Number, string Title, string ChapterUrl)>>
            GetChapterListAsync(string url)
        {
            var bookId = GetBookId(url);

            if (string.IsNullOrWhiteSpace(bookId))
                throw new Exception("Không tìm thấy bookId trong link FanqieTC.");

            var json = await GetApiAsync(
                "directory",
                "book_id",
                bookId);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data))
                throw new Exception("FanqieTC không trả về danh sách chương.");

            if (!data.TryGetProperty("item_data_list", out var chapters))
                throw new Exception("Không tìm thấy danh sách chương.");

            var result =
                new List<(int Number, string Title, string ChapterUrl)>();

            var index = 0;

            foreach (var item in chapters.EnumerateArray())
            {
                var itemId = GetString(item, "item_id");

                var title = GetString(item, "title");

                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                index++;

                // Không dùng link web thật của chương.
                // Dùng URL API làm ChapterUrl để IDownloadSource
                // có thể nhận lại item_id khi tải nội dung.
                var chapterUrl =
                    $"https://fanqietc.com/chapter?itemId={itemId}";

                result.Add((
                    index,
                    title,
                    chapterUrl
                ));
            }

            return result;
        }

        // ============================================================
        // Nội dung chương
        // ============================================================

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var itemId = GetItemId(chapterUrl);

            if (string.IsNullOrWhiteSpace(itemId))
                throw new Exception("Không tìm thấy itemId của chương.");

            var json = await GetApiAsync(
                "content",
                "item_id",
                itemId);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data))
                throw new Exception("FanqieTC không trả về nội dung chương.");

            return GetString(data, "content");
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static string GetItemId(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "";

            var query = uri.Query.TrimStart('?');

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);

                if (pair.Length == 2 &&
                    pair[0].Equals("itemId", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return "";
        }

        private static string GetString(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return "";

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? "",
                JsonValueKind.Number => property.ToString(),
                _ => ""
            };
        }
    }
}