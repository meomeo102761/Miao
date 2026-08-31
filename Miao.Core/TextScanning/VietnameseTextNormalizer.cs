using System.Text;

namespace Miao.Core.TextScanning
{
    public static class VietnameseTextNormalizer
    {
        private const string Src =
            "àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ" +
            "ÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ";
        private const string Dst =
            "aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooooouuuuuuuuuuuyyyyyd" +
            "AAAAAAAAAAAAAAAAAEEEEEEEEEEEIIIIIOOOOOOOOOOOOOOOOOOOUUUUUUUUUUUYYYYYD";

        private static readonly Dictionary<char, char> Map = BuildMap();

        private static Dictionary<char, char> BuildMap()
        {
            var map = new Dictionary<char, char>(Src.Length);
            for (int i = 0; i < Src.Length; i++)
                map[Src[i]] = Dst[i];
            return map;
        }

        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            var lower = input.ToLowerInvariant();
            var sb = new StringBuilder(lower.Length);
            foreach (var c in lower)
                sb.Append(Map.TryGetValue(c, out var mapped) ? mapped : c);

            return sb.ToString();
        }
    }
}