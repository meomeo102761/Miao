using System;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public enum TranslationEngine
    {
        DichNgay,
        Dictionary
    }
    public sealed class TranslationService
    {
        private ITranslationProvider _provider = null!;

        public TranslationEngine Engine { get; private set; }

        public TranslationService(
            TranslationEngine engine = TranslationEngine.DichNgay,
            TranslationOptions? options = null)
        {
            SetEngine(engine, options);
        }

        public Task<string> TranslateChapterAsync(string text)
        {
            return TranslateAsync(text);
        }

        public static TranslationEngine ParseEngine(string? value)
        {
            return string.Equals(value, "Dictionary", StringComparison.OrdinalIgnoreCase)
                ? TranslationEngine.Dictionary
                : TranslationEngine.DichNgay;
        }

        public static TranslationService CreateFromSettings(
            TranslationOptions? options = null)
        {
            var engine = ParseEngine(
                AppSettingsService.Instance.Settings.TranslationEngine);

            return new TranslationService(engine, options);
        }

        public void SetEngine(
            TranslationEngine engine,
            TranslationOptions? options = null)
        {
            Engine = engine;

            _provider = engine switch
            {
                TranslationEngine.DichNgay =>
                    new DichNgayProvider(),

                TranslationEngine.Dictionary =>
                    new DictionaryTranslationProvider(options),

                _ => throw new ArgumentOutOfRangeException(
                    nameof(engine),
                    engine,
                    "Engine dịch không được hỗ trợ.")
            };
        }

        public async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            const int maxAttempts = 3;

            for (var attempt = 1; attempt < maxAttempts; attempt++)
            {
                try
                {
                    return await _provider.TranslateAsync(text);
                }
                catch
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1200 * attempt));
                }
            }

            return await _provider.TranslateAsync(text);
        }
    }
}