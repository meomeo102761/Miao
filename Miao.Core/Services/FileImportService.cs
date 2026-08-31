using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using VersOne.Epub;

namespace Miao.Core.Services
{
    public class ImportedChapter
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class ImportedNovel
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public List<ImportedChapter> Chapters { get; set; } = new();
    }

    public class FileImportService
    {
        public ImportedNovel ImportFromFile(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".txt" => ImportTxt(filePath),
                ".epub" => ImportEpub(filePath),
                ".docx" => ImportDocx(filePath),
                _ => throw new NotSupportedException($"Chưa hỗ trợ định dạng {ext} — hiện chỉ hỗ trợ .txt, .epub và .docx")
            };
        }

        private ImportedNovel ImportDocx(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) throw new Exception("Không đọc được nội dung file .docx.");

            var sb = new System.Text.StringBuilder();
            foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var text = para.InnerText;
                sb.AppendLine(text);
            }

            var fullText = sb.ToString();
            var pattern = @"(?m)^\s*(Chương\s*\d+|Chapter\s*\d+|第\s*\d+\s*章)[^\n]*$";
            var matches = Regex.Matches(fullText, pattern);

            var chapters = new List<ImportedChapter>();

            if (matches.Count == 0)
            {
                chapters.Add(new ImportedChapter { Title = fileName, Content = fullText.Trim() });
            }
            else
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    var start = matches[i].Index;
                    var end = (i + 1 < matches.Count) ? matches[i + 1].Index : fullText.Length;
                    var chunk = fullText.Substring(start, end - start).Trim();

                    var title = matches[i].Value.Trim();
                    var content = chunk.Substring(matches[i].Length).Trim();

                    chapters.Add(new ImportedChapter { Title = title, Content = content });
                }
            }

            return new ImportedNovel { Title = fileName, Author = "", Chapters = chapters };
        }

        private ImportedNovel ImportTxt(string filePath)
        {
            var text = File.ReadAllText(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            var pattern = @"(?m)^\s*(Chương\s*\d+|Chapter\s*\d+|第\s*\d+\s*章)[^\n]*$";
            var matches = Regex.Matches(text, pattern);

            var chapters = new List<ImportedChapter>();

            if (matches.Count == 0)
            {
                chapters.Add(new ImportedChapter { Title = fileName, Content = text.Trim() });
            }
            else
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    var start = matches[i].Index;
                    var end = (i + 1 < matches.Count) ? matches[i + 1].Index : text.Length;
                    var chunk = text.Substring(start, end - start).Trim();

                    var title = matches[i].Value.Trim();
                    var content = chunk.Substring(matches[i].Length).Trim();

                    chapters.Add(new ImportedChapter { Title = title, Content = content });
                }
            }

            return new ImportedNovel { Title = fileName, Author = "", Chapters = chapters };
        }

        private ImportedNovel ImportEpub(string filePath)
        {
            var book = EpubReader.ReadBook(filePath);
            var chapters = new List<ImportedChapter>();
            var index = 1;

            foreach (var contentFile in book.ReadingOrder)
            {
                var html = contentFile.Content;
                var plainText = HtmlToPlainText(html);
                if (string.IsNullOrWhiteSpace(plainText))
                    continue;

                var title = ExtractChapterTitle(html);
                if (string.IsNullOrWhiteSpace(title))
                    title = $"Mục không có tiêu đề ({index})";

                chapters.Add(new ImportedChapter
                {
                    Title = title.Trim(),
                    Content = plainText
                });

                index++;
            }

            return new ImportedNovel
            {
                Title = book.Title ?? Path.GetFileNameWithoutExtension(filePath),
                Author = book.Author ?? "",
                Chapters = chapters
            };
        }

        private static string ExtractChapterTitle(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var node = doc.DocumentNode.SelectSingleNode("//h1 | //h2 | //h3 | //title");
            if (node == null)
                return string.Empty;

            return HtmlEntity.DeEntitize(node.InnerText).Trim();
        }

        private string HtmlToPlainText(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sb = new System.Text.StringBuilder();
            var textNodes = doc.DocumentNode.SelectNodes("//text()");
            if (textNodes == null)
                return "";

            foreach (var node in textNodes)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }

            return sb.ToString().Trim();
        }
    }
}
