using System;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public static class NameHanVietLookup
    {
        private static readonly Lazy<DictionaryTranslationProvider> Shared =
            new(() => new DictionaryTranslationProvider());

        public static async Task<string> ToHanVietAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? "";

            try
            {
                return await Shared.Value.ToHanVietPhraseAsync(text);
            }
            catch
            {
                return "";
            }
        }
    }
}