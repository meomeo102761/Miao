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
                    
                }
            }

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

        public string ToHanViet(string hanText) => Convert(hanText, _hanViet, capitalize: true);

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
                    continue;

                var reading = readings[0];
                if (capitalize && reading.Length > 0)
                    reading = char.ToUpperInvariant(reading[0]) + reading[1..];

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(reading);
            }
            return sb.ToString();
        }
    }
}