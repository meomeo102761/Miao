using Tesseract;

namespace Miao.Core.Services
{
    public class OcrService
    {
        private readonly TesseractEngine _engine;

        public OcrService(string tessdataFolderPath)
        {
            _engine = new TesseractEngine(tessdataFolderPath, "chi_sim", EngineMode.Default);
        }

        public string RecognizeText(byte[] imageBytes)
        {
            using var img = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(img);
            return page.GetText();
        }
    }
}