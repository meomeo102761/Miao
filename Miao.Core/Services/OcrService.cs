using System;
using System.IO;
using Tesseract;

namespace Miao.Core.Services
{
    public class OcrService
    {
        private readonly TesseractEngine _engine;

        public OcrService(string tessdataFolderPath)
        {
            if (!Directory.Exists(tessdataFolderPath))
                throw new DirectoryNotFoundException(
                    $"Không tìm thấy thư mục tessdata tại: {tessdataFolderPath}");

            var trainedDataFile = Path.Combine(tessdataFolderPath, "chi_sim.traineddata");
            if (!File.Exists(trainedDataFile))
                throw new FileNotFoundException(
                    $"Thiếu file huấn luyện chi_sim.traineddata tại: {trainedDataFile}");

            try
            {
                _engine = new TesseractEngine(tessdataFolderPath, "chi_sim", EngineMode.Default);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không khởi tạo được Tesseract OCR. Nguyên nhân thường gặp: " +
                    "(1) tessdata folder không được copy vào thư mục publish/output, " +
                    "(2) thiếu DLL native leptonica/tesseract đúng kiến trúc (x64/x86/ARM64), " +
                    "(3) file traineddata sai phiên bản. Chi tiết gốc: " + ex.Message, ex);
            }
        }

        public string RecognizeText(byte[] imageBytes)
        {
            using var img = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(img);
            return page.GetText();
        }
    }
}