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

        public SinoVietnameseConverter(string handataPath, string? hanVietDictionaryPath = null)
        {
            _hanViet = LoadCombinedHanViet(handataPath, hanVietDictionaryPath);
            _pinyin = LoadMap(Path.Combine(handataPath, "kMandarin.json"));
        }

        private static Dictionary<string, string[]> LoadCombinedHanViet(
            string handataPath, string? hanVietDictionaryPath)
        {
            var result = new Dictionary<string, string[]>();

            // Nguồn 1 (ưu tiên): HanViet.json của bộ dịch dictionary — phủ nhiều ký tự hơn.
            if (!string.IsNullOrWhiteSpace(hanVietDictionaryPath) && File.Exists(hanVietDictionaryPath))
            {
                try
                {
                    var json = File.ReadAllText(hanVietDictionaryPath);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var val = prop.Value.TryGetProperty("val", out var v) ? v.GetString() : null;
                        if (string.IsNullOrWhiteSpace(val)) continue;

                        var readings = new List<string> { val };
                        if (prop.Value.TryGetProperty("alts", out var altsEl) && altsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var a in altsEl.EnumerateArray())
                            {
                                var s = a.GetString();
                                if (!string.IsNullOrWhiteSpace(s) && !readings.Contains(s))
                                    readings.Add(s);
                            }
                        }
                        result[prop.Name] = readings.ToArray();
                    }
                }
                catch (JsonException)
                {
                    // File hỏng/không đúng format thì bỏ qua, dùng kVietnamese.json bên dưới.
                }
            }

            // Nguồn 2 (bù thêm): kVietnamese.json — chỉ bù ký tự HanViet.json chưa có.
            var kvPath = Path.Combine(handataPath, "kVietnamese.json");
            if (File.Exists(kvPath))
            {
                foreach (var pair in LoadMap(kvPath))
                {
                    if (!result.ContainsKey(pair.Key))
                        result[pair.Key] = pair.Value;
                }
            }

            return result;
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