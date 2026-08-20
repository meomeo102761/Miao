using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public class TranslationService
    {
        private readonly ITranslationProvider _provider;
        private readonly ConvertStyleService _convertStyle = new();

        // Dịch theo khối lớn để giảm số request tới Dịch Ngay.
        private const int PreferredChunkCharacters = 900;
        private const int HardChunkCharacters = 1400;

        public TranslationService(ITranslationProvider provider)
        {
            _provider = provider;
        }

        public async Task<string> TranslateTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!Regex.IsMatch(text, @"\p{IsCJKUnifiedIdeographs}"))
                return text.Trim();

            var translated = await TranslateWithRetryAsync(text.Trim());
            return _convertStyle.Apply(translated).Trim();
        }

        public async Task<string> TranslateChapterAsync(string originalContent)
            => await TranslateChapterAsync(originalContent, null);

        /// <summary>
        /// Dịch theo các khối lớn thay vì gọi API cho từng dòng.
        /// Việc này giảm mạnh số request tới Dịch Ngay và hạn chế 503 do
        /// gửi quá nhiều request liên tiếp.
        /// </summary>
        public async Task<string> TranslateChapterAsync(
            string originalContent,
            Func<int, int, string, Task>? onChunkTranslated)
        {
            if (string.IsNullOrWhiteSpace(originalContent))
                return string.Empty;

            var normalized = originalContent
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();

            if (!Regex.IsMatch(normalized, @"\p{IsCJKUnifiedIdeographs}"))
                return normalized;

            var chunks = SplitChapterIntoChunks(normalized);
            var translatedChunks = new List<string>();

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                var translated = await TranslateWithRetryAsync(chunk);
                translated = _convertStyle.Apply(translated).Trim();

                if (string.IsNullOrWhiteSpace(translated))
                    throw new InvalidOperationException("Engine trả về nội dung dịch rỗng.");

                translatedChunks.Add(translated);

                if (onChunkTranslated != null)
                    await onChunkTranslated(i + 1, chunks.Count, translated);

                // Cho Dịch Ngay một khoảng nghỉ nhỏ giữa các request.
                if (i < chunks.Count - 1 &&
                    AppSettingsService.Instance.Settings.TranslationEngine.Equals(
                        "DichNgay",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(350);
                }
            }

            return string.Join("\n\n", translatedChunks).Trim();
        }

        private static List<string> SplitChapterIntoChunks(string text)
        {
            var result = new List<string>();
            var current = new StringBuilder();

            foreach (var line in text.Split('\n'))
            {
                var cleanLine = line.Trim();

                if (cleanLine.Length == 0)
                {
                    if (current.Length > 0)
                        current.Append("\n");
                    continue;
                }

                if (current.Length > 0 &&
                    current.Length + cleanLine.Length + 1 > HardChunkCharacters)
                {
                    AddChunk(result, current);
                }

                if (current.Length > 0)
                    current.Append('\n');

                current.Append(cleanLine);

                if (current.Length >= PreferredChunkCharacters)
                    AddChunk(result, current);
            }

            if (current.Length > 0)
                AddChunk(result, current);

            return result;
        }

        private static void AddChunk(List<string> result, StringBuilder current)
        {
            var chunk = current.ToString().Trim();
            if (chunk.Length > 0)
                result.Add(chunk);
            current.Clear();
        }

        private async Task<string> TranslateWithRetryAsync(string text)
        {
            const int maxAttempts = 4;
            Exception? lastError = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await _provider.TranslateAsync(text);
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                }
            }

            throw lastError ?? new InvalidOperationException("Dịch thất bại.");
        }
    }
}
