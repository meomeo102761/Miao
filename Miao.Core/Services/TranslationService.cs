using System;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public enum TranslationEngine
    {
        DichNgay,
        Dictionary
    }

    /// <summary>
    /// Đầu mối điều phối engine dịch.
    ///
    /// TranslationService không trực tiếp thực hiện việc dịch.
    /// Nó chọn một ITranslationProvider:
    ///
    /// - DichNgayProvider
    /// - DictionaryTranslationProvider
    /// </summary>
    public sealed class TranslationService
    {
        private ITranslationProvider _provider;

        public TranslationEngine Engine { get; private set; }

        public TranslationService(
            TranslationEngine engine = TranslationEngine.DichNgay,
            TranslationOptions? options = null)
        {
            SetEngine(engine, options);
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

        public Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Task.FromResult(text ?? string.Empty);

            return _provider.TranslateAsync(text);
        }
    }
}