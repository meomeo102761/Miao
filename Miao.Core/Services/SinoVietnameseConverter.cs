using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Miao.Core.Services
{
    public class SinoVietnameseConverter
    {
        private readonly Dictionary<string, string[]> _hanViet;
        private readonly Dictionary<string, string[]> _pinyin;

        public SinoVietnameseConverter(string handataPath)
        {
            _hanViet = LoadMap(Path.Combine(handataPath, "kVietnamese.json"));
            _pinyin = LoadMap(Path.Combine(handataPath, "kMandarin.json"));
        }

        private static Dictionary<string, string[]> LoadMap(string path)
        {
            if (!File.Exists(path))
                return new Dictionary<string, string[]>();

            var json = File.ReadAllText(path);

            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrArrayConverter());

            return JsonSerializer.Deserialize<Dictionary<string, string[]>>(json, options)
                ?? new Dictionary<string, string[]>();
        }

        // Chuyển từng ký tự Hán trong chuỗi sang âm Hán Việt, cách nhau bằng khoảng trắng.
        // VD: "硝子" -> "Tiêu Tử"
        public string ToHanViet(string hanText) => Convert(hanText, _hanViet, capitalize: true);

        // Chuyển từng ký tự Hán trong chuỗi sang bính âm, cách nhau bằng khoảng trắng.
        // VD: "硝子" -> "xiāo zǐ"
        public string ToPinYin(string hanText) => Convert(hanText, _pinyin, capitalize: false);

        private static string Convert(string hanText, Dictionary<string, string[]> map, bool capitalize)
        {
            if (string.IsNullOrWhiteSpace(hanText))
                return "";

            var sb = new StringBuilder();
            foreach (var ch in hanText)
            {
                var key = ch.ToString();
                if (!map.TryGetValue(key, out var readings) || readings.Length == 0)
                    continue; // ký tự không tra được (dấu câu, chữ Latin lẫn vào...) thì bỏ qua

                var reading = readings[0]; // ký tự đa âm: tạm lấy âm đầu tiên trong danh sách
                if (capitalize && reading.Length > 0)
                    reading = char.ToUpperInvariant(reading[0]) + reading[1..];

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(reading);
            }
            return sb.ToString();
        }
    }
}