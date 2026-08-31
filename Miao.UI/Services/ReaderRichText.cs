using System;
using System.Collections.Generic;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Miao.UI.Services
{
    public static class ReaderRichText
    {
        private static readonly (string Tag, Action<Run> Apply)[] Styles =
        {
            ("b", r => r.FontWeight = FontWeight.Bold),
            ("i", r => r.FontStyle = FontStyle.Italic),
            ("u", r => r.TextDecorations = TextDecorations.Underline),
            ("s", r => r.TextDecorations = TextDecorations.Strikethrough),
        };

        private static readonly (string Tag, Action<Run> Apply)[] LegacyAngleTags =
        {
            ("b", r => r.FontWeight = FontWeight.Bold),
            ("i", r => r.FontStyle = FontStyle.Italic),
            ("u", r => r.TextDecorations = TextDecorations.Underline),
            ("s", r => r.TextDecorations = TextDecorations.Strikethrough),
        };

        public static InlineCollection ToInlines(string text, IBrush? foreground = null)
        {
            var result = new InlineCollection();
            AppendParsed(text, new List<Action<Run>>(), foreground, result);
            return result;
        }

        private static void AppendParsed(string text, List<Action<Run>> activeStyles, IBrush? fg, InlineCollection result)
        {
            var i = 0;
            var plainStart = 0;

            void FlushPlain(int end)
            {
                if (end <= plainStart) return;
                AppendRun(text.Substring(plainStart, end - plainStart), activeStyles, fg, result);
            }

            while (i < text.Length)
            {
                var tag = FindTagAt(text, i, "[", Styles, out var apply, out var openLen);
                if (tag != null)
                {
                    var close = $"[/{tag}]";
                    var closeIndex = text.IndexOf(close, i + openLen, StringComparison.Ordinal);
                    if (closeIndex >= 0)
                    {
                        FlushPlain(i);
                        var inner = text.Substring(i + openLen, closeIndex - i - openLen);
                        AppendParsed(inner, new List<Action<Run>>(activeStyles) { apply! }, fg, result);
                        i = closeIndex + close.Length;
                        plainStart = i;
                        continue;
                    }
                }

                var legacyTag = FindTagAt(text, i, "<", LegacyAngleTags, out var applyLegacy, out var legacyOpenLen);
                if (legacyTag != null)
                {
                    var close = $"</{legacyTag}>";
                    var closeIndex = text.IndexOf(close, i + legacyOpenLen, StringComparison.Ordinal);
                    if (closeIndex >= 0)
                    {
                        FlushPlain(i);
                        var inner = text.Substring(i + legacyOpenLen, closeIndex - i - legacyOpenLen);
                        AppendParsed(inner, new List<Action<Run>>(activeStyles) { applyLegacy! }, fg, result);
                        i = closeIndex + close.Length;
                        plainStart = i;
                        continue;
                    }
                }

                i++;
            }

            FlushPlain(text.Length);
        }

        private static void AppendRun(string text, List<Action<Run>> styles, IBrush? fg, InlineCollection result)
        {
            if (text.Length == 0) return;
            var run = new Run(text);
            foreach (var apply in styles) apply(run);
            if (fg != null) run.Foreground = fg;
            result.Add(run);
        }

        private static string? FindTagAt(string text, int i, string openChar,
            (string Tag, Action<Run> Apply)[] tags, out Action<Run>? apply, out int openLength)
        {
            foreach (var (tag, applyStyle) in tags)
            {
                var open = $"{openChar}{tag}]".Replace("]", openChar == "<" ? ">" : "]");
                if (i + open.Length <= text.Length && string.CompareOrdinal(text, i, open, 0, open.Length) == 0)
                {
                    apply = applyStyle;
                    openLength = open.Length;
                    return tag;
                }
            }
            apply = null;
            openLength = 0;
            return null;
        }

        public static (string newText, int newStart, int newEnd) WrapSelection(string text, int selStart, int selEnd, string styleName)
        {
            var open = $"[{styleName}]";
            var close = $"[/{styleName}]";
            var start = Math.Min(selStart, selEnd);
            var end = Math.Max(selStart, selEnd);

            if (start != end)
            {
                var selected = text.Substring(start, end - start);
                if (selected.StartsWith(open, StringComparison.Ordinal) && selected.EndsWith(close, StringComparison.Ordinal)
                    && selected.Length >= open.Length + close.Length)
                {
                    var inner = selected.Substring(open.Length, selected.Length - open.Length - close.Length);
                    return (text[..start] + inner + text[end..], start, start + inner.Length);
                }

                var hasOuterLeft = start >= open.Length && string.CompareOrdinal(text, start - open.Length, open, 0, open.Length) == 0;
                var hasOuterRight = end + close.Length <= text.Length && string.CompareOrdinal(text, end, close, 0, close.Length) == 0;
                if (hasOuterLeft && hasOuterRight)
                    return (text[..(start - open.Length)] + selected + text[(end + close.Length)..], start - open.Length, end - open.Length);

                return (text[..start] + open + selected + close + text[end..], start + open.Length, end + open.Length);
            }

            var cursorBetweenEmptyPair = start >= open.Length && start + close.Length <= text.Length
                && string.CompareOrdinal(text, start - open.Length, open, 0, open.Length) == 0
                && string.CompareOrdinal(text, start, close, 0, close.Length) == 0;
            if (cursorBetweenEmptyPair)
                return (text[..(start - open.Length)] + text[(start + close.Length)..], start - open.Length, start - open.Length);

            return (text[..start] + open + close + text[start..], start + open.Length, start + open.Length);
        }
    }
}