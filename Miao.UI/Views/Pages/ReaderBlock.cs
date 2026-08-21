using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Miao.UI.Views.Pages.Reader
{
    public enum ReaderBlockType { Text, Image }

    // Thay thế FlowDocument/Paragraph/InlineUIContainer của WPF — vì Avalonia không có
    // RichTextBox, ta tự tách nội dung chương thành danh sách khối (text hoặc ảnh) để
    // render bằng ItemsControl. Mỗi dòng gốc trong Chapter.DisplayContent/OriginalContent
    // ứng với đúng 1 khối, giữ nguyên ý nghĩa dữ liệu cũ.
    public class ReaderBlock
    {
        public ReaderBlockType Type { get; set; }
        public string Text { get; set; } = "";       // dùng khi Type == Text
        public string ImagePath { get; set; } = "";  // dùng khi Type == Image

        private static readonly Regex ImagePlaceholderRegex = new(@"^\[\[IMG:(.+?)\]\]$", RegexOptions.Compiled);

        public static List<ReaderBlock> Parse(string? content)
        {
            var lines = (content ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var blocks = new List<ReaderBlock>();

            foreach (var line in lines)
            {
                var match = ImagePlaceholderRegex.Match(line.Trim());
                blocks.Add(match.Success
                    ? new ReaderBlock { Type = ReaderBlockType.Image, ImagePath = match.Groups[1].Value.Trim() }
                    : new ReaderBlock { Type = ReaderBlockType.Text, Text = line });
            }

            return blocks;
        }

        // Ngược lại với Parse — ghép các khối trở về text thô để lưu vào DB,
        // giữ đúng format "[[IMG:path]]" như dữ liệu cũ.
        public static string Serialize(IEnumerable<ReaderBlock> blocks) =>
            string.Join("\n", blocks.Select(b =>
                b.Type == ReaderBlockType.Image ? $"[[IMG:{b.ImagePath}]]" : b.Text));
    }
}
