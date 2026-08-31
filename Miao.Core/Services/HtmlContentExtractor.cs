using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public static class HtmlContentExtractor
    {
        public static string ExtractTextWithImages(HtmlNode root, IEnumerable<string>? boilerplatePatterns = null)
        {
            var sb = new StringBuilder();
            AppendNode(root, sb);

            var text = sb.ToString();
            if (boilerplatePatterns != null)
            {
                var patterns = boilerplatePatterns.ToList();
                var lines = text.Split('\n')
                    .Where(line => !patterns.Any(p => line.Contains(p)));
                text = string.Join("\n", lines);
            }

            return text.Trim();
        }

        private static void AppendNode(HtmlNode node, StringBuilder sb)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
                {
                    var src = child.GetAttributeValue("src", "").Trim();
                    if (!string.IsNullOrWhiteSpace(src))
                    {
                        sb.AppendLine($"[[IMG:{src}]]");
                        sb.AppendLine();
                    }
                    continue;
                }

                if (child.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                    continue;
                }

                if (child.NodeType == HtmlNodeType.Text)
                {
                    var text = HtmlEntity.DeEntitize(child.InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                    continue;
                }

                if (child.HasChildNodes)
                    AppendNode(child, sb);
            }
        }
    }
}