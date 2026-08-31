using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Miao.UI.Views.Pages.Reader
{
    public enum ReaderBlockType { Text, Image }

    public class ReaderBlock
    {
        public ReaderBlockType Type { get; set; }
        public string Text { get; set; } = "";
        public string ImagePath { get; set; } = ""; 

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

        public static string Serialize(IEnumerable<ReaderBlock> blocks) =>
            string.Join("\n", blocks.Select(b =>
                b.Type == ReaderBlockType.Image ? $"[[IMG:{b.ImagePath}]]" : b.Text));
    }
}
