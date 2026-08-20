using System.Text.RegularExpressions;

namespace Miao.Core.Services
{
    public class ConvertStyleService
    {
        private static readonly (string Pattern, string Replacement)[] Rules = new[]
        {
            (@"\banh ta\b", "hắn"),
            (@"\bAnh ta\b", "Hắn"),
            (@"\banh ấy\b", "hắn"),
            (@"\bAnh ấy\b", "Hắn"),
            (@"\bcô ấy\b", "nàng"),
            (@"\bCô ấy\b", "Nàng"),
            (@"\bcô ta\b", "nàng"),
            (@"\bCô ta\b", "Nàng"),

            (@"\btôi\b", "ta"),
            (@"\bTôi\b", "Ta"),

            (@"\bbạn\b", "ngươi"),
            (@"\bBạn\b", "Ngươi"),
            (@"\banh\b", "ngươi"),
            (@"\bAnh\b", "Ngươi"),
        };

        public string Apply(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var result = text;
            foreach (var (pattern, replacement) in Rules)
                result = Regex.Replace(result, pattern, replacement);

            return result;
        }
    }
}