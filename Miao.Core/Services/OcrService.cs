using System;
using System.IO;
using Tesseract;

namespace Miao.Core.Services
{
    public class OcrService
    {
        private readonly TesseractEngine? _engine;
        private readonly string? _initializationError;

        public OcrService(string tessdataFolderPath)
        {
            try
            {
                var trainedDataFile = Path.Combine(tessdataFolderPath, "chi_sim.traineddata");

                if (!Directory.Exists(tessdataFolderPath))
                {
                    _initializationError = $"Không tìm thấy thư mục tessdata tại: {tessdataFolderPath}";
                    return;
                }

                if (!File.Exists(trainedDataFile))
                {
                    _initializationError = $"Thiếu file huấn luyện chi_sim.traineddata tại: {trainedDataFile}";
                    return;
                }

                _engine = new TesseractEngine(tessdataFolderPath, "chi_sim", EngineMode.Default);
            }
            catch (Exception ex)
            {
                _initializationError = ex.Message;
            }
        }

        public string RecognizeText(byte[] imageBytes)
        {
            if (_engine is null)
                throw new InvalidOperationException(
                    "OCR chưa sẵn sàng. " + (_initializationError ?? "Không rõ nguyên nhân."));

            using var img = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(img);
            return page.GetText();
        }
    }
}
