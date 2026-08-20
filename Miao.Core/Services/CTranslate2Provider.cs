using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    /// <summary>
    /// Translation provider entry point used by the app.
    /// The selected engine is read from AppSettings so existing callers do not
    /// need to know which translation backend is active.
    /// </summary>
    public class CTranslate2Provider : ITranslationProvider
    {
        private readonly HttpClient _http = new();
        private readonly string _endpoint;

        public CTranslate2Provider(string? endpoint = null)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint)
                ? AppSettingsService.Instance.Settings.CTranslate2Endpoint
                : endpoint;
        }

        public async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var settings = AppSettingsService.Instance.Settings;

            // Dịch Ngay is the default engine. This keeps the current
            // TranslationService/DownloadPage wiring intact while allowing
            // the future Settings page to switch the backend to CT2.
            if (string.Equals(settings.TranslationEngine, "DichNgay", StringComparison.OrdinalIgnoreCase))
            {
                var provider = new DichNgayProvider(settings.DichNgayEndpoint);
                return await provider.TranslateAsync(text);
            }

            // CT2 is only started when it is the selected engine.
            if (!await CTranslate2ServerService.EnsureRunningAsync())
            {
                throw new InvalidOperationException(
                    "Không thể khởi động CTranslate2 server. " +
                    "Hãy kiểm tra Python, dependencies và TranslateServer/ct2_model.");
            }

            var payload = new { text };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync(_endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("translation")
                .GetString() ?? "";
        }
    }
}
