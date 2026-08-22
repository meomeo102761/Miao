using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Miao.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Miao.Core.Services
{
    public enum NovelExportFormat { Epub, Docx, Pdf }

    public static class NovelExportService
    {
        public static void Export(Novel novel, List<Chapter> chapters, string outputPath, NovelExportFormat format, bool useOriginalContent = false)
        {
            var ordered = chapters.OrderBy(c => c.Number).ToList();

            switch (format)
            {
                case NovelExportFormat.Epub: ExportEpub(novel, ordered, outputPath, useOriginalContent); break;
                case NovelExportFormat.Docx: ExportDocx(novel, ordered, outputPath, useOriginalContent); break;
                case NovelExportFormat.Pdf: ExportPdf(novel, ordered, outputPath, useOriginalContent); break;
            }
        }

        // ---------------------------------------------------------------
        // EPUB — tự dựng bằng System.IO.Compression, không cần thư viện
        // ghi epub riêng (VersOne.Epub trong project chỉ dùng để ĐỌC epub).
        // Cấu trúc tối thiểu hợp lệ: mimetype (lưu thô, không nén) +
        // META-INF/container.xml + OEBPS/content.opf + toc.ncx + các .xhtml.
        // ---------------------------------------------------------------
        private static void ExportEpub(Novel novel, List<Chapter> chapters, string outputPath, bool useOriginalContent)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);

            using var zipStream = new FileStream(outputPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

            // mimetype PHẢI là entry đầu tiên và KHÔNG nén — chuẩn epub yêu cầu vậy.
            var mimetypeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var w = new StreamWriter(mimetypeEntry.Open())) w.Write("application/epub+zip");

            WriteEntry(archive, "META-INF/container.xml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);

            var manifestItems = new StringBuilder();
            var spineItems = new StringBuilder();
            var navPoints = new StringBuilder();
            int order = 1;

            foreach (var ch in chapters)
            {
                var fileName = $"chap{ch.Number}.xhtml";
                manifestItems.AppendLine($"<item id=\"c{ch.Number}\" href=\"{fileName}\" media-type=\"application/xhtml+xml\"/>");
                spineItems.AppendLine($"<itemref idref=\"c{ch.Number}\"/>");
                navPoints.AppendLine($"""
                    <navPoint id="nav{ch.Number}" playOrder="{order}">
                      <navLabel><text>{XmlEscape(ch.DisplayTitle)}</text></navLabel>
                      <content src="{fileName}"/>
                    </navPoint>
                    """);
                order++;

                var bodyHtml = string.Join("\n", GetContent(ch, useOriginalContent)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => $"<p>{XmlEscape(line.Trim())}</p>"));

                WriteEntry(archive, $"OEBPS/{fileName}", $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                    <head><title>{XmlEscape(ch.DisplayTitle)}</title></head>
                    <body>
                      <h2>{XmlEscape(ch.DisplayTitle)}</h2>
                      {bodyHtml}
                    </body>
                    </html>
                    """);
            }

            WriteEntry(archive, "OEBPS/content.opf", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="2.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>{XmlEscape(novel.DisplayTitle)}</dc:title>
                    <dc:creator>{XmlEscape(novel.Author)}</dc:creator>
                    <dc:language>vi</dc:language>
                    <dc:identifier id="BookId">miao-{novel.Id}</dc:identifier>
                  </metadata>
                  <manifest>
                    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                    {manifestItems}
                  </manifest>
                  <spine toc="ncx">
                    {spineItems}
                  </spine>
                </package>
                """);

            WriteEntry(archive, "OEBPS/toc.ncx", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                  <head>
                    <meta name="dtb:uid" content="miao-{novel.Id}"/>
                  </head>
                  <docTitle><text>{XmlEscape(novel.DisplayTitle)}</text></docTitle>
                  <navMap>
                    {navPoints}
                  </navMap>
                </ncx>
                """);
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string XmlEscape(string text) =>
            (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        
        private static string GetContent(Chapter ch, bool useOriginal) =>
            useOriginal ? (ch.OriginalContent ?? "") : (ch.DisplayContent ?? "");

        // ---------------------------------------------------------------
        // DOCX — dùng DocumentFormat.OpenXml (đã có sẵn trong project).
        // ---------------------------------------------------------------
        private static void ExportDocx(Novel novel, List<Chapter> chapters, string outputPath, bool useOriginalContent)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);

            using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(MakeParagraph(novel.DisplayTitle, "Title", 44));
            body.AppendChild(MakeParagraph($"Tác giả: {novel.Author}", null, 22));

            foreach (var ch in chapters)
            {
                body.AppendChild(MakeParagraph($"Chương {ch.Number}: {ch.DisplayTitle}", "Heading1", 28));

                var lines = GetContent(ch, useOriginalContent).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var p = new Paragraph();
                    var run = new Run(new Text(line.Trim()) { Space = SpaceProcessingModeValues.Preserve });
                    p.AppendChild(run);
                    body.AppendChild(p);
                }
            }

            mainPart.Document.Save();
        }

        private static Paragraph MakeParagraph(string text, string? styleId, int fontSizeHalfPoints)
        {
            var p = new Paragraph();
            var pPr = new ParagraphProperties();
            if (styleId != null)
                pPr.ParagraphStyleId = new ParagraphStyleId { Val = styleId };
            p.AppendChild(pPr);

            var run = new Run();
            run.AppendChild(new RunProperties(new Bold(), new FontSize { Val = fontSizeHalfPoints.ToString() }));
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            p.AppendChild(run);
            return p;
        }

        // ---------------------------------------------------------------
        // PDF — dùng QuestPDF (đã có sẵn trong project, license Community).
        // ---------------------------------------------------------------
        private static void ExportPdf(Novel novel, List<Chapter> chapters, string outputPath, bool useOriginalContent)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Content().Column(col =>
                    {
                        col.Item().Text(novel.DisplayTitle).FontSize(20).Bold();
                        col.Item().Text($"Tác giả: {novel.Author}").FontSize(12).Italic();
                        col.Item().PaddingVertical(10);

                        foreach (var ch in chapters)
                        {
                            col.Item().PageBreak();
                            col.Item().Text($"Chương {ch.Number}: {ch.DisplayTitle}").FontSize(16).Bold();
                            col.Item().PaddingVertical(6);

                            foreach (var line in GetContent(ch, useOriginalContent).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                                col.Item().Text(line.Trim()).ParagraphSpacing(4);
                        }
                    });
                });
            }).GeneratePdf(outputPath);
        }
    }
}