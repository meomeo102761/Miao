using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Miao.UI.Services
{
    public static class MiniMarkdown
    {
        public static InlineCollection ToInlines(string text)
        {
            var result = new InlineCollection();
            int i = 0;
            var plain = new StringBuilder();

            void FlushPlain()
            {
                if (plain.Length > 0) { result.Add(new Run(plain.ToString())); plain.Clear(); }
            }

            while (i < text.Length)
            {
                if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2);
                    if (end >= 0)
                    {
                        FlushPlain();
                        result.Add(new Run(text.Substring(i + 2, end - i - 2)) { FontWeight = FontWeight.Bold });
                        i = end + 2;
                        continue;
                    }
                }
                else if (text[i] == '*')
                {
                    int end = text.IndexOf('*', i + 1);
                    if (end >= 0)
                    {
                        FlushPlain();
                        result.Add(new Run(text.Substring(i + 1, end - i - 1)) { FontStyle = FontStyle.Italic });
                        i = end + 1;
                        continue;
                    }
                }
                plain.Append(text[i]);
                i++;
            }
            FlushPlain();
            return result;
        }

        public static (string newText, int newSelStart, int newSelEnd) WrapSelection(string text, int selStart, int selEnd, string marker)
        {
            var start = Math.Min(selStart, selEnd);
            var end = Math.Max(selStart, selEnd);
            var ml = marker.Length;

            if (start != end)
            {
                var selected = text.Substring(start, end - start);

                if (selected.Length >= ml * 2 && selected.StartsWith(marker) && selected.EndsWith(marker))
                {
                    var inner = selected.Substring(ml, selected.Length - ml * 2);
                    var stripped = text.Substring(0, start) + inner + text.Substring(end);
                    return (stripped, start, start + inner.Length);
                }

                bool hasOuterLeft = start >= ml && text.Substring(start - ml, ml) == marker;
                bool hasOuterRight = end + ml <= text.Length && text.Substring(end, ml) == marker;
                if (hasOuterLeft && hasOuterRight)
                {
                    var stripped = text.Substring(0, start - ml) + selected + text.Substring(end + ml);
                    return (stripped, start - ml, end - ml);
                }

                var wrapped = text.Substring(0, start) + marker + selected + marker + text.Substring(end);
                return (wrapped, start + ml, end + ml);
            }

            bool cursorBetweenEmptyPair = start >= ml && start + ml <= text.Length
                && text.Substring(start - ml, ml) == marker
                && text.Substring(start, ml) == marker;

            if (cursorBetweenEmptyPair)
            {
                var stripped = text.Substring(0, start - ml) + text.Substring(start + ml);
                return (stripped, start - ml, start - ml);
            }

            var inserted = text.Substring(0, start) + marker + marker + text.Substring(start);
            return (inserted, start + ml, start + ml);
        }
    }
}