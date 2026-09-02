using System;
using System.Threading;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public static class NameHanVietLookup
    {
        private static readonly Lazy<DictionaryTranslationProvider> Shared =
            new(() => new DictionaryTranslationProvider(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static Task<string> ToHanVietAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult(text ?? "");

            return Task.Run(async () =>
            {
                try
                {
                    return await Shared.Value.ToHanVietPhraseAsync(text);
                }
                catch
                {
                    return "";
                }
            });
        }
    }
}