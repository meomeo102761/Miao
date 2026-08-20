using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public interface IScreenshotFetcher
    {
        Task<byte[]> CaptureScreenshotAsync(string url, int extraWaitMs = 2500);
    }
}