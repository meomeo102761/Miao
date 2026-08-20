using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public interface ITranslationProvider
    {
        Task<string> TranslateAsync(string text);
    }
}