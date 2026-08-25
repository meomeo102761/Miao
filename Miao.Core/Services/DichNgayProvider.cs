using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    /// <summary>
    /// Translation provider for dichngay.com.
    /// Sends the whole chapter first.
    /// If Dịch Ngay returns HTTP 413, the chapter is automatically
    /// split into smaller paragraph/sentence batches and translated
    /// piece by piece.
    /// </summary>
    public class DichNgayProvider : ITranslationProvider
    {
        private static readonly HttpClient Http = CreateHttpClient();

        private readonly string _endpoint;

        // Giới hạn an toàn cho mỗi request fallback.
        // Đây là giới hạn phía Miao, không phải giới hạn chính thức
        // của Dịch Ngay.
        private const int MaxBatchUtf8Bytes = 12000;

        public DichNgayProvider(string? endpoint = null)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint)
                ? "https://dichngay.com/translate/text"
                : endpoint;
        }

        public async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            try
            {
                // ----------------------------------------------------
                // Lần đầu: gửi nguyên chương như trước.
                // ----------------------------------------------------
                return await TranslateRequestAsync(text);
            }
            catch (DichNgayPayloadTooLargeException)
            {
                // ----------------------------------------------------
                // Nếu 413: tự động chuyển sang chế độ chia nhỏ.
                // ----------------------------------------------------
                return await TranslateLargeTextAsync(text);
            }
        }

        // ============================================================
        // Gửi một request tới Dịch Ngay
        // ============================================================

        private async Task<string> TranslateRequestAsync(string text)
        {
            var payload = new
            {
                content = text,
                tl = "vi"
            };

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    _endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json")
                };

            request.Headers.Referrer =
                new Uri("https://dichngay.com/");

            try
            {
                using var response =
                    await Http.SendAsync(request);

                // ----------------------------------------------------
                // 413 = payload quá lớn.
                //
                // Không throw InvalidOperationException bình thường,
                // vì cần báo cho TranslateAsync biết phải chia nhỏ.
                // ----------------------------------------------------
                if (response.StatusCode ==
                    HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new DichNgayPayloadTooLargeException();
                }

                if (response.StatusCode ==
                    HttpStatusCode.ServiceUnavailable)
                {
                    throw new InvalidOperationException(
                        "Dịch Ngay hiện không khả dụng (503 Service Unavailable). " +
                        "Hãy thử lại sau.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status =
                        (int)response.StatusCode;

                    throw new InvalidOperationException(
                        $"Dịch Ngay không khả dụng (HTTP {status}). " +
                        "Hãy thử lại sau.");
                }

                var json =
                    await response.Content
                        .ReadAsStringAsync();

                using var document =
                    JsonDocument.Parse(json);

                var root =
                    document.RootElement;

                if (root.TryGetProperty(
                        "data",
                        out var data) &&
                    data.ValueKind ==
                        JsonValueKind.Object &&
                    data.TryGetProperty(
                        "content",
                        out var translatedContent))
                {
                    var result =
                        ExtractTranslatedValue(
                            translatedContent);

                    if (!string.IsNullOrWhiteSpace(result))
                        return Normalize(result);
                }

                if (root.TryGetProperty(
                        "translatedText",
                        out var translatedText))
                {
                    var result =
                        ExtractTranslatedValue(
                            translatedText);

                    if (!string.IsNullOrWhiteSpace(result))
                        return Normalize(result);
                }

                throw new InvalidOperationException(
                    "Dịch Ngay trả về response không có nội dung dịch.");
            }
            catch (TaskCanceledException)
            {
                throw new InvalidOperationException(
                    "Dịch Ngay hết thời gian chờ. " +
                    "Hãy thử lại sau.");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    "Không thể kết nối tới Dịch Ngay. " +
                    "Hãy kiểm tra kết nối mạng.",
                    ex);
            }
        }

        // ============================================================
        // Dịch nội dung lớn sau khi nhận 413
        // ============================================================

        private async Task<string> TranslateLargeTextAsync(
            string text)
        {
            var paragraphs =
                SplitIntoParagraphs(text);

            if (paragraphs.Count == 0)
                return string.Empty;

            var batches =
                BuildBatches(paragraphs);

            var translatedParts =
                new List<string>();

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                var translated =
                    await TranslateBatchWithFallbackAsync(batch);

                translatedParts.Add(translated);
            }

            return string.Join(
                "\n\n",
                translatedParts);
        }

        // ============================================================
        // Dịch batch.
        //
        // Nếu paragraph/batch vẫn quá lớn và Dịch Ngay trả 413,
        // tiếp tục chia nhỏ theo câu.
        // ============================================================

        private async Task<string> TranslateBatchWithFallbackAsync(
            string text)
        {
            try
            {
                return await TranslateRequestAsync(text);
            }
            catch (DichNgayPayloadTooLargeException)
            {
                // ----------------------------------------------------
                // Nếu batch vẫn quá lớn:
                // chia tiếp theo câu.
                // ----------------------------------------------------

                var sentences =
                    SplitIntoSentences(text);

                if (sentences.Count <= 1)
                {
                    // Không thể chia nhỏ thêm một cách an toàn.
                    throw new InvalidOperationException(
                        "Một đoạn nội dung vẫn vượt giới hạn của Dịch Ngay " +
                        "ngay cả sau khi đã chia nhỏ.");
                }

                var sentenceBatches =
                    BuildBatches(sentences);

                var translatedParts =
                    new List<string>();

                foreach (var batch in sentenceBatches)
                {
                    var translated =
                        await TranslateBatchWithFallbackAsync(batch);

                    translatedParts.Add(translated);
                }

                return string.Join(
                    " ",
                    translatedParts);
            }
        }

        // ============================================================
        // Tách paragraph
        // ============================================================

        private static List<string> SplitIntoParagraphs(
            string text)
        {
            var normalized =
                text
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');

            var matches =
                Regex.Split(
                    normalized,
                    @"\n\s*\n+");

            var result =
                new List<string>();

            foreach (var paragraph in matches)
            {
                var value =
                    paragraph.Trim();

                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }

            // Nếu nội dung không có dòng trống,
            // coi mỗi dòng là một paragraph.
            if (result.Count <= 1 &&
                normalized.Contains('\n'))
            {
                result.Clear();

                foreach (var line in
                         normalized.Split(
                             '\n',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    var value =
                        line.Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value);
                }
            }

            if (result.Count == 0 &&
                !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }

            return result;
        }

        // ============================================================
        // Tách câu
        // ============================================================

        private static List<string> SplitIntoSentences(
            string text)
        {
            var result =
                new List<string>();

            var matches =
                Regex.Matches(
                    text,
                    @"[^。！？!?\.]+[。！？!?\.]?",
                    RegexOptions.Multiline);

            foreach (Match match in matches)
            {
                var value =
                    match.Value.Trim();

                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }

            if (result.Count == 0)
                result.Add(text.Trim());

            return result;
        }

        // ============================================================
        // Gom paragraph/câu thành batch
        // ============================================================

        private static List<string> BuildBatches(
            List<string> parts)
        {
            var batches =
                new List<string>();

            var current =
                new StringBuilder();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var separator =
                    current.Length == 0
                        ? string.Empty
                        : "\n\n";

                var candidate =
                    current.ToString() +
                    separator +
                    part;

                var candidateBytes =
                    Encoding.UTF8.GetByteCount(candidate);

                // ----------------------------------------------------
                // Batch hiện tại còn đủ chỗ.
                // ----------------------------------------------------
                if (candidateBytes <= MaxBatchUtf8Bytes)
                {
                    if (current.Length > 0)
                        current.Append("\n\n");

                    current.Append(part);
                    continue;
                }

                // ----------------------------------------------------
                // Đẩy batch hiện tại vào danh sách.
                // ----------------------------------------------------
                if (current.Length > 0)
                {
                    batches.Add(
                        current.ToString());

                    current.Clear();
                }

                // ----------------------------------------------------
                // Một paragraph đơn lẻ đã vượt giới hạn.
                // Để TranslateBatchWithFallbackAsync xử lý tiếp
                // bằng cách chia theo câu.
                // ----------------------------------------------------
                if (Encoding.UTF8.GetByteCount(part)
                    > MaxBatchUtf8Bytes)
                {
                    batches.Add(part);
                }
                else
                {
                    current.Append(part);
                }
            }

            if (current.Length > 0)
            {
                batches.Add(
                    current.ToString());
            }

            return batches;
        }

        // ============================================================
        // Parse response
        // ============================================================

        private static string ExtractTranslatedValue(
            JsonElement value)
        {
            if (value.ValueKind ==
                JsonValueKind.Array)
            {
                foreach (var item in
                         value.EnumerateArray())
                {
                    if (item.ValueKind ==
                        JsonValueKind.String)
                    {
                        return item.GetString()
                               ?? string.Empty;
                    }
                }

                return string.Empty;
            }

            if (value.ValueKind ==
                JsonValueKind.String)
            {
                var raw =
                    value.GetString()
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                try
                {
                    using var nested =
                        JsonDocument.Parse(raw);

                    if (nested.RootElement.ValueKind ==
                        JsonValueKind.Array)
                    {
                        foreach (var item in
                                 nested.RootElement
                                     .EnumerateArray())
                        {
                            if (item.ValueKind ==
                                JsonValueKind.String)
                            {
                                return item.GetString()
                                       ?? string.Empty;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Response có thể chứa text trực tiếp.
                }

                return raw;
            }

            return string.Empty;
        }

        // ============================================================
        // Normalize
        // ============================================================

        private static string Normalize(string text)
        {
            return Regex.Replace(
                    text
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n'),
                    @"[ \t]+",
                    " ")
                .Trim();
        }

        // ============================================================
        // HttpClient
        // ============================================================

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(60)
            };

            client.DefaultRequestHeaders
                .UserAgent
                .ParseAdd("Miao/1.0");

            return client;
        }

        // ============================================================
        // Exception riêng cho HTTP 413
        // ============================================================

        private sealed class DichNgayPayloadTooLargeException
            : Exception
        {
        }
    }
}