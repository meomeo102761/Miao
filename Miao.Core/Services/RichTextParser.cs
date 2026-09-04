using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Miao.Core.Services
{
    public enum RichTextRunStyle { None, Bold, Italic, Underline, Strike }

    public class RichTextSegment
    {
        public string Text { get; set; } = "";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strike { get; set; }
    }

    // Parser dùng chung cho việc xuất file (docx/epub/pdf) — tách 1 dòng chứa các tag nội bộ
    // [b]/[i]/[u]/[s] thành các đoạn (segment) kèm cờ định dạng tương ứng, để nơi xuất file tự
    // quyết định cách vẽ (Bold/Italic run trong Word, <b>/<i> trong epub, .Bold()/.Italic() trong PDF).
    // Đây LÀ NƠI DUY NHẤT hiểu ý nghĩa tag — nếu ReaderRichText.ToInlines dùng ký hiệu khác,
    // chỉ cần sửa Regex ở đây, không phải sửa lại cả 3 hàm xuất file.
    public static class RichTextParser
    {
        // Khớp cả 2 dạng tag mà ReaderRichText.ToInlines đang nhận diện:
        // - Dạng chính:  [b]...[/b], [i]...[/i], [u]...[/u], [s]...[/s]
        // - Dạng cũ:     <b>...</b>, <i>...</i>, <u>...</u>, <s>...</s>  (dữ liệu/nguồn cũ)
        // Bắt cái nào xuất hiện SỚM NHẤT trong chuỗi trước, để 2 dạng có thể xen kẽ đúng thứ tự.
        private static readonly Regex SquareTagRegex = new(@"\[(b|i|u|s)\](.*?)\[/\1\]", RegexOptions.Singleline);
        private static readonly Regex AngleTagRegex = new(@"<(b|i|u|s)>(.*?)</\1>", RegexOptions.Singleline);

        public static List<RichTextSegment> ParseLine(string line)
        {
            var segments = new List<RichTextSegment>();
            if (string.IsNullOrEmpty(line))
            {
                segments.Add(new RichTextSegment { Text = "" });
                return segments;
            }

            ParseRecursive(line, false, false, false, false, segments);
            return segments;
        }

        private static void ParseRecursive(string text, bool bold, bool italic, bool underline, bool strike, List<RichTextSegment> output)
        {
            var squareMatch = SquareTagRegex.Match(text);
            var angleMatch = AngleTagRegex.Match(text);

            Match? match = null;
            if (squareMatch.Success && angleMatch.Success)
                match = squareMatch.Index <= angleMatch.Index ? squareMatch : angleMatch;
            else if (squareMatch.Success)
                match = squareMatch;
            else if (angleMatch.Success)
                match = angleMatch;

            if (match == null)
            {
                if (text.Length > 0)
                    output.Add(new RichTextSegment { Text = text, Bold = bold, Italic = italic, Underline = underline, Strike = strike });
                return;
            }

            var before = text[..match.Index];
            if (before.Length > 0)
                output.Add(new RichTextSegment { Text = before, Bold = bold, Italic = italic, Underline = underline, Strike = strike });

            var tag = match.Groups[1].Value;
            var inner = match.Groups[2].Value;

            ParseRecursive(
                inner,
                bold || tag == "b",
                italic || tag == "i",
                underline || tag == "u",
                strike || tag == "s",
                output);

            var after = text[(match.Index + match.Length)..];
            if (after.Length > 0)
                ParseRecursive(after, bold, italic, underline, strike, output);
        }
    }
}