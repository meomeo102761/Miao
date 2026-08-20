using System;
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
    /// Matches the request format used by Dịch Ngay's web client.
    /// </summary>
    public class DichNgayProvider : ITranslationProvider
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private readonly string _endpoint;

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

            // Dịch Ngay's own web client sends the original text directly:
            // { "content": "...", "tl": "vi" }
            // Do not JSON-serialize an array into content.
            var payload = new
            {
                content = text,
                tl = "vi"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Referrer = new Uri("https://dichngay.com/");

            try
            {
                using var response = await Http.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    throw new InvalidOperationException(
                        "Dịch Ngay hiện không khả dụng (503 Service Unavailable). " +
                        "Hãy thử lại sau hoặc chuyển sang engine CT2 trong Cài đặt.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    throw new InvalidOperationException(
                        $"Dịch Ngay không khả dụng (HTTP {status}). " +
                        "Hãy thử lại sau hoặc chuyển sang engine CT2 trong Cài đặt.");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("content", out var translatedContent))
                {
                    var result = ExtractTranslatedValue(translatedContent);
                    if (!string.IsNullOrWhiteSpace(result))
                        return Normalize(result);
                }

                if (root.TryGetProperty("translatedText", out var translatedText))
                {
                    var result = ExtractTranslatedValue(translatedText);
                    if (!string.IsNullOrWhiteSpace(result))
                        return Normalize(result);
                }

                throw new InvalidOperationException(
                    "Dịch Ngay trả về response không có nội dung dịch. " +
                    "Hãy thử lại sau hoặc chuyển sang engine CT2 trong Cài đặt.");
            }
            catch (TaskCanceledException)
            {
                throw new InvalidOperationException(
                    "Dịch Ngay hết thời gian chờ. Hãy thử lại sau hoặc chuyển sang engine CT2 trong Cài đặt.");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    "Không thể kết nối tới Dịch Ngay. Hãy kiểm tra kết nối mạng hoặc chuyển sang engine CT2 trong Cài đặt.",
                    ex);
            }
        }

        private static string ExtractTranslatedValue(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        return item.GetString() ?? string.Empty;
                }

                return string.Empty;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var raw = value.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                try
                {
                    using var nested = JsonDocument.Parse(raw);
                    if (nested.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in nested.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                return item.GetString() ?? string.Empty;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Response may contain translated text directly.
                }

                return raw;
            }

            return string.Empty;
        }

        private static string Normalize(string text)
        {
            return Regex.Replace(
                text.Replace("\r\n", "\n").Replace('\r', '\n'),
                @"[ \t]+",
                " ").Trim();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Miao/1.0");
            return client;
        }
    }
}
