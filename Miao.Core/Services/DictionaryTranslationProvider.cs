using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public sealed class TranslationOptions
    {
        public bool PriorityNameFirst { get; set; } = true;

        public bool PreferLongVP { get; set; } = true;

        public bool RiengChung { get; set; } = true;

        public int MaxSuggest { get; set; } = 200;

        public int? MaxMatchLen { get; set; }

        public string PunctuationStyle { get; set; }
            = "vietnamese";
    }

    public sealed class TranslationSegment
    {
        public string Zh { get; init; } = "";

        public string Value { get; init; } = "";

        public List<string> Alternatives { get; init; }
            = new();

        public string Source { get; init; } = "";
    }

    internal sealed class DictionaryEntry
    {
        public string Value { get; set; } = "";

        public List<string> Alternatives { get; set; }
            = new();

        public string? Tag { get; set; }

        public bool Skip { get; set; }
    }

    internal sealed class DictionaryIndex
    {
        public Dictionary<int,
            Dictionary<string, DictionaryEntry>> Buckets
            { get; }
            = new();

        public int MaxLength { get; private set; }

        public DictionaryIndex(
            Dictionary<string, DictionaryEntry> source)
        {
            foreach (var pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key))
                    continue;

                var length = pair.Key.Length;

                if (!Buckets.TryGetValue(
                        length,
                        out var bucket))
                {
                    bucket =
                        new Dictionary<string, DictionaryEntry>(
                            StringComparer.Ordinal);

                    Buckets[length] = bucket;
                }

                bucket[pair.Key] = pair.Value;

                if (length > MaxLength)
                    MaxLength = length;
            }
        }
    }

    /// <summary>
    /// Engine dịch dựa trên cơ chế dictionary của
    /// Novel Downloader 5:
    ///
    /// Name.json
    /// VP.json
    /// HanViet.json
    /// Longest Match
    /// </summary>
    public sealed class DictionaryTranslationProvider
        : ITranslationProvider
    {
        private readonly TranslationOptions _options;

        private Dictionary<string, DictionaryEntry>
            _nameDictionary =
                new(StringComparer.Ordinal);

        private Dictionary<string, DictionaryEntry>
            _vpDictionary =
                new(StringComparer.Ordinal);

        private Dictionary<string, DictionaryEntry>
            _hanVietDictionary =
                new(StringComparer.Ordinal);

        private DictionaryIndex _nameIndex =
            new(new Dictionary<string, DictionaryEntry>());

        private DictionaryIndex _vpIndex =
            new(new Dictionary<string, DictionaryEntry>());

        public bool IsReady { get; private set; }

        public DictionaryTranslationProvider(
            TranslationOptions? options = null)
        {
            _options =
                options ?? new TranslationOptions();
        }

        public async Task<string> TranslateAsync(
            string text)
        {
            await EnsureInitializedAsync();

            return TranslateText(text);
        }

        public async Task<string> TranslateChapterAsync(
            string originalContent)
        {
            return await TranslateAsync(
                originalContent);
        }

        public string TranslateText(
            string text,
            TranslationOptions? options = null)
        {
            EnsureReady();

            if (string.IsNullOrEmpty(text))
                return text ?? "";

            var actualOptions =
                options ?? _options;

            var tokens =
                TranslateToSegments(
                    text,
                    actualOptions);

            var result =
                JoinTokensMakePretty(tokens);

            return MapPunctuation(
                result,
                actualOptions.PunctuationStyle);
        }

        public string TranslateSentence(
            string text,
            TranslationOptions? options = null)
        {
            return TranslateText(
                text,
                options);
        }

        public List<TranslationSegment>
            TranslateSegments(
                string text,
                TranslationOptions? options = null)
        {
            EnsureReady();

            var actualOptions =
                options ?? _options;

            return TranslateToSegments(
                text,
                actualOptions);
        }

        public List<TranslationSegment>
            TranslateWithAlternatives(
                string text,
                TranslationOptions? options = null)
        {
            return TranslateSegments(
                text,
                options);
        }

        public List<TranslationSegment> Suggest(
            string term,
            int? limit = null)
        {
            EnsureReady();

            var max =
                limit ?? _options.MaxSuggest;

            var result =
                new List<TranslationSegment>();

            if (_nameDictionary.TryGetValue(
                    term,
                    out var nameEntry))
            {
                result.Add(
                    ToSegment(
                        term,
                        nameEntry,
                        "Name"));
            }

            if (_vpDictionary.TryGetValue(
                    term,
                    out var vpEntry))
            {
                result.Add(
                    ToSegment(
                        term,
                        vpEntry,
                        "VP"));
            }

            if (result.Count > 0)
                return result;

            ScanSuggestions(
                _nameDictionary,
                term,
                "Name",
                max,
                result);

            if (result.Count < max)
            {
                ScanSuggestions(
                    _vpDictionary,
                    term,
                    "VP",
                    max,
                    result);
            }

            if (result.Count > 0)
            {
                return result
                    .Take(max)
                    .ToList();
            }

            var fallback =
                TranslateText(term);

            return new List<TranslationSegment>
            {
                new()
                {
                    Zh = term,
                    Value = fallback,
                    Alternatives =
                        new List<string>
                        {
                            fallback
                        },
                    Source = "Fallback"
                }
            };
        }

        public void AddEntry(
            string dictionaryName,
            string zh,
            string value,
            IEnumerable<string>? alternatives = null,
            string? tag = null)
        {
            EnsureReady();

            if (string.IsNullOrWhiteSpace(zh))
            {
                throw new ArgumentException(
                    "Từ tiếng Trung không được để trống.",
                    nameof(zh));
            }

            Dictionary<string, DictionaryEntry>
                target;

            if (string.Equals(
                    dictionaryName,
                    "name",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = _nameDictionary;
            }
            else if (string.Equals(
                         dictionaryName,
                         "vp",
                         StringComparison.OrdinalIgnoreCase))
            {
                target = _vpDictionary;
            }
            else
            {
                throw new ArgumentException(
                    "dictionaryName chỉ có thể là name hoặc vp.",
                    nameof(dictionaryName));
            }

            var alternativeList =
                alternatives?
                    .Select(x => x?.Trim() ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList()
                ??
                new List<string>();

            if (alternativeList.Count == 0 &&
                !string.IsNullOrWhiteSpace(value))
            {
                alternativeList.Add(
                    value.Trim());
            }

            target[zh] =
                new DictionaryEntry
                {
                    Value =
                        value?.Trim() ?? "",

                    Alternatives =
                        alternativeList,

                    Tag = tag,

                    Skip =
                        string.IsNullOrWhiteSpace(
                            value)
                };

            if (ReferenceEquals(
                    target,
                    _nameDictionary))
            {
                _nameIndex =
                    new DictionaryIndex(
                        _nameDictionary);
            }
            else
            {
                _vpIndex =
                    new DictionaryIndex(
                        _vpDictionary);
            }
        }

        public async Task EnsureInitializedAsync(
            string? baseDirectory = null)
        {
            if (IsReady)
                return;

            baseDirectory ??=
                AppDomain.CurrentDomain.BaseDirectory;

            var dictionaryDirectory =
                Path.Combine(
                    baseDirectory,
                    "translate",
                    "zh_to_vi");

            await InitializeAsync(
                Path.Combine(
                    dictionaryDirectory,
                    "Name.json"),

                Path.Combine(
                    dictionaryDirectory,
                    "VP.json"),

                Path.Combine(
                    dictionaryDirectory,
                    "HanViet.json"));
        }

        public void Initialize(
            string namePath,
            string vpPath,
            string hanVietPath)
        {
            InitializeAsync(
                    namePath,
                    vpPath,
                    hanVietPath)
                .GetAwaiter()
                .GetResult();
        }

        public async Task InitializeAsync(
            string namePath,
            string vpPath,
            string hanVietPath)
        {
            var nameText =
                await File.ReadAllTextAsync(
                    namePath);

            var vpText =
                await File.ReadAllTextAsync(
                    vpPath);

            var hanVietText =
                await File.ReadAllTextAsync(
                    hanVietPath);

            _nameDictionary =
                NormalizeDictionary(nameText);

            _vpDictionary =
                NormalizeDictionary(vpText);

            _hanVietDictionary =
                NormalizeDictionary(hanVietText);

            _nameIndex =
                new DictionaryIndex(
                    _nameDictionary);

            _vpIndex =
                new DictionaryIndex(
                    _vpDictionary);

            IsReady = true;
        }

        private List<TranslationSegment>
            TranslateToSegments(
                string text,
                TranslationOptions options)
        {
            var result =
                new List<TranslationSegment>();

            foreach (var run in SplitRuns(text))
            {
                if (run.IsCjk)
                {
                    result.AddRange(
                        GlobalLongestMatch(
                            run.Text,
                            options));
                }
                else
                {
                    result.Add(
                        new TranslationSegment
                        {
                            Zh = run.Text,
                            Value = run.Text,
                            Alternatives =
                                new List<string>
                                {
                                    run.Text
                                },
                            Source = "TEXT"
                        });
                }
            }

            return result;
        }

        private List<TranslationSegment>
            GlobalLongestMatch(
                string text,
                TranslationOptions options)
        {
            var length =
                text.Length;

            var maxFromDictionary =
                Math.Max(
                    _nameIndex.MaxLength,
                    _vpIndex.MaxLength);

            var maxLength =
                options.MaxMatchLen.HasValue
                    ? Math.Min(
                        options.MaxMatchLen.Value,
                        maxFromDictionary)
                    : maxFromDictionary;

            var replaced =
                new bool[length];

            var slots =
                new MatchSlot?[length];

            for (var currentLength =
                     maxLength;
                 currentLength >= 1;
                 currentLength--)
            {
                _nameIndex.Buckets.TryGetValue(
                    currentLength,
                    out var nameBucket);

                _vpIndex.Buckets.TryGetValue(
                    currentLength,
                    out var vpBucket);

                if (nameBucket == null &&
                    vpBucket == null)
                {
                    continue;
                }

                for (var start = 0;
                     start + currentLength <= length;
                     start++)
                {
                    var overlaps = false;

                    for (var k = 0;
                         k < currentLength;
                         k++)
                    {
                        if (replaced[start + k])
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (overlaps)
                        continue;

                    var part =
                        text.Substring(
                            start,
                            currentLength);

                    DictionaryEntry? nameHit =
                        null;

                    DictionaryEntry? vpHit =
                        null;

                    nameBucket?.TryGetValue(
                        part,
                        out nameHit);

                    vpBucket?.TryGetValue(
                        part,
                        out vpHit);

                    if (nameHit == null &&
                        vpHit == null)
                    {
                        continue;
                    }

                    MatchSlot slot;

                    if (options.RiengChung)
                    {
                        if (nameHit?.Skip == true &&
                            vpHit?.Skip != true)
                        {
                            slot =
                                new MatchSlot(
                                    part,
                                    "",
                                    nameHit.Alternatives,
                                    "SKIP",
                                    currentLength);

                            SetSlot(
                                slots,
                                replaced,
                                start,
                                slot);

                            continue;
                        }

                        if (vpHit?.Skip == true &&
                            nameHit?.Skip != true)
                        {
                            slot =
                                new MatchSlot(
                                    part,
                                    "",
                                    vpHit.Alternatives,
                                    "SKIP",
                                    currentLength);

                            SetSlot(
                                slots,
                                replaced,
                                start,
                                slot);

                            continue;
                        }
                    }

                    if (nameHit != null &&
                        vpHit != null)
                    {
                        var useVp =
                            options.PreferLongVP;

                        var selected =
                            useVp
                                ? vpHit
                                : nameHit;

                        slot =
                            new MatchSlot(
                                part,
                                selected.Value,
                                selected.Alternatives,
                                useVp
                                    ? "VP"
                                    : "Name",
                                currentLength);
                    }
                    else if (nameHit != null)
                    {
                        slot =
                            new MatchSlot(
                                part,
                                nameHit.Skip
                                    ? ""
                                    : nameHit.Value,
                                nameHit.Alternatives,
                                nameHit.Skip
                                    ? "SKIP"
                                    : "Name",
                                currentLength);
                    }
                    else
                    {
                        slot =
                            new MatchSlot(
                                part,
                                vpHit!.Skip
                                    ? ""
                                    : vpHit.Value,
                                vpHit.Alternatives,
                                vpHit.Skip
                                    ? "SKIP"
                                    : "VP",
                                currentLength);
                    }

                    SetSlot(
                        slots,
                        replaced,
                        start,
                        slot);
                }
            }

            var result =
                new List<TranslationSegment>();

            for (var i = 0;
                 i < length;)
            {
                if (slots[i] != null)
                {
                    var slot =
                        slots[i]!;

                    result.Add(
                        new TranslationSegment
                        {
                            Zh = slot.Zh,
                            Value = slot.Value,
                            Alternatives =
                                slot.Alternatives,
                            Source = slot.Source
                        });

                    i += slot.Length;
                    continue;
                }

                var character =
                    text[i].ToString();

                if (_hanVietDictionary.TryGetValue(
                        character,
                        out var hanViet))
                {
                    result.Add(
                        new TranslationSegment
                        {
                            Zh = character,
                            Value = hanViet.Value,
                            Alternatives =
                                hanViet.Alternatives,
                            Source = "HanViet"
                        });
                }
                else
                {
                    result.Add(
                        new TranslationSegment
                        {
                            Zh = character,
                            Value = character,
                            Alternatives =
                                new List<string>
                                {
                                    character
                                },
                            Source = "HanViet"
                        });
                }

                i++;
            }

            return result;
        }

        private static void SetSlot(
            MatchSlot?[] slots,
            bool[] replaced,
            int start,
            MatchSlot slot)
        {
            slots[start] = slot;

            for (var i = 0;
                 i < slot.Length;
                 i++)
            {
                replaced[start + i] = true;
            }
        }

        private static TranslationSegment ToSegment(
            string zh,
            DictionaryEntry entry,
            string source)
        {
            return new TranslationSegment
            {
                Zh = zh,
                Value =
                    entry.Skip
                        ? ""
                        : entry.Value,

                Alternatives =
                    new List<string>(
                        entry.Alternatives),

                Source =
                    entry.Skip
                        ? "SKIP"
                        : source
            };
        }

        private static void ScanSuggestions(
            Dictionary<string, DictionaryEntry>
                dictionary,
            string term,
            string source,
            int max,
            List<TranslationSegment> result)
        {
            foreach (var pair in dictionary)
            {
                if (result.Count >= max)
                    break;

                if (!pair.Key.Contains(
                        term,
                        StringComparison.Ordinal) &&
                    !term.Contains(
                        pair.Key,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(
                    ToSegment(
                        pair.Key,
                        pair.Value,
                        source));
            }
        }

        private static Dictionary<string, DictionaryEntry>
            NormalizeDictionary(string json)
        {
            using var document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Dictionary JSON phải là object.");
            }

            var result =
                new Dictionary<string, DictionaryEntry>(
                    StringComparer.Ordinal);

            foreach (var property in
                     document.RootElement
                         .EnumerateObject())
            {
                if (string.IsNullOrEmpty(
                        property.Name))
                {
                    continue;
                }

                var entry =
                    NormalizeEntry(
                        property.Value);

                if (entry != null)
                    result[property.Name] = entry;
            }

            return result;
        }

        private static DictionaryEntry?
            NormalizeEntry(
                JsonElement element)
        {
            if (element.ValueKind ==
                    JsonValueKind.Null ||
                element.ValueKind ==
                    JsonValueKind.Undefined)
            {
                return null;
            }

            if (element.ValueKind ==
                JsonValueKind.String)
            {
                var raw =
                    element.GetString()
                    ?? "";

                var parts =
                    raw.Split('/')
                        .Select(x => x.Trim())
                        .ToList();

                var first =
                    parts.Count > 0
                        ? parts[0]
                        : "";

                return new DictionaryEntry
                {
                    Value = first,
                    Alternatives = parts,
                    Skip = first.Length == 0
                };
            }

            if (element.ValueKind ==
                    JsonValueKind.Object &&
                element.TryGetProperty(
                    "val",
                    out var valueElement))
            {
                var value =
                    valueElement.ValueKind ==
                        JsonValueKind.String
                        ? valueElement
                            .GetString()?
                            .Trim() ?? ""
                        : "";

                var alternatives =
                    new List<string>();

                if (element.TryGetProperty(
                        "alts",
                        out var altsElement) &&
                    altsElement.ValueKind ==
                        JsonValueKind.Array)
                {
                    foreach (var item in
                             altsElement
                                 .EnumerateArray())
                    {
                        if (item.ValueKind !=
                            JsonValueKind.String)
                        {
                            continue;
                        }

                        var alternative =
                            item.GetString()?
                                .Trim();

                        if (!string.IsNullOrEmpty(
                                alternative))
                        {
                            alternatives.Add(
                                alternative);
                        }
                    }
                }

                if (alternatives.Count == 0 &&
                    value.Length > 0)
                {
                    alternatives.Add(value);
                }

                string? tag = null;

                if (element.TryGetProperty(
                        "tag",
                        out var tagElement) &&
                    tagElement.ValueKind ==
                        JsonValueKind.String)
                {
                    tag =
                        tagElement.GetString();
                }

                return new DictionaryEntry
                {
                    Value = value,
                    Alternatives = alternatives,
                    Tag = tag,
                    Skip = value.Length == 0
                };
            }

            var fallback =
                element.ToString().Trim();

            return new DictionaryEntry
            {
                Value = fallback,
                Alternatives =
                    new List<string>
                    {
                        fallback
                    },
                Skip = fallback.Length == 0
            };
        }

        private static List<TextRun>
            SplitRuns(string text)
        {
            var result =
                new List<TextRun>();

            if (string.IsNullOrEmpty(text))
                return result;

            var builder =
                new StringBuilder();

            bool? currentIsCjk = null;

            foreach (var character in text)
            {
                var isCjk =
                    IsCjk(character);

                if (currentIsCjk == null)
                {
                    currentIsCjk = isCjk;
                    builder.Append(character);
                    continue;
                }

                if (currentIsCjk == isCjk)
                {
                    builder.Append(character);
                }
                else
                {
                    result.Add(
                        new TextRun(
                            currentIsCjk.Value,
                            builder.ToString()));

                    builder.Clear();
                    builder.Append(character);

                    currentIsCjk = isCjk;
                }
            }

            if (builder.Length > 0 &&
                currentIsCjk.HasValue)
            {
                result.Add(
                    new TextRun(
                        currentIsCjk.Value,
                        builder.ToString()));
            }

            return result;
        }

        private static bool IsCjk(
            char character)
        {
            return
                (character >= '\u3400' &&
                 character <= '\u4DBF')
                ||
                (character >= '\u4E00' &&
                 character <= '\u9FFF')
                ||
                (character >= '\uF900' &&
                 character <= '\uFAFF');
        }

        private static string JoinTokensMakePretty(
            IEnumerable<TranslationSegment> tokens)
        {
            var result =
                new StringBuilder();

            var noSpaceBefore =
                new HashSet<char>
                {
                    '.', ',', ':', ';',
                    '!', '?', '…', '%',
                    '»', '”', '』',
                    ')', ']', '}',
                    '，', '。', '、',
                    '：', '；', '？',
                    '！', '」', '》'
                };

            var noSpaceAfter =
                new HashSet<char>
                {
                    '(', '[', '{',
                    '«', '“',
                    '『', '「', '《'
                };

            var sentenceEnd =
                new HashSet<char>
                {
                    '.', '!', '?',
                    '\n',
                    '。', '！', '？'
                };

            foreach (var token in tokens)
            {
                var value =
                    token.Value ??
                    token.Zh ??
                    "";

                if (string.IsNullOrWhiteSpace(
                        value))
                {
                    continue;
                }

                value =
                    value.Trim();

                if (result.Length > 0)
                {
                    var last =
                        result[^1];

                    var first =
                        value[0];

                    if (!noSpaceBefore.Contains(first) &&
                        !noSpaceAfter.Contains(last))
                    {
                        result.Append(' ');
                    }
                }

                if (result.Length > 0 &&
                    sentenceEnd.Contains(
                        result[^1]) &&
                    value.Length > 0 &&
                    value[0] >= 'a' &&
                    value[0] <= 'z')
                {
                    value =
                        char.ToUpperInvariant(
                            value[0]) +
                        value.Substring(1);
                }

                result.Append(value);
            }

            return result
                .ToString()
                .Trim();
        }

        private static string MapPunctuation(
            string text,
            string style)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var vietnamese =
                string.Equals(
                    style,
                    "vietnamese",
                    StringComparison.OrdinalIgnoreCase);

            var map =
                new Dictionary<char, char>
                {
                    ['，'] = ',',
                    ['。'] = '.',
                    ['：'] = ':',
                    ['；'] = ';',
                    ['？'] = '?',
                    ['！'] = '!',
                    ['、'] = ',',
                    ['（'] = '(',
                    ['）'] = ')',
                    ['「'] = '“',
                    ['」'] = '”',
                    ['『'] = '“',
                    ['』'] = '”',
                    ['“'] = '“',
                    ['”'] = '”'
                };

            if (vietnamese)
            {
                map['《'] = '«';
                map['》'] = '»';
            }

            var builder =
                new StringBuilder(
                    text.Length);

            foreach (var character in text)
            {
                if (map.TryGetValue(
                        character,
                        out var mapped))
                {
                    builder.Append(mapped);
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private void EnsureReady()
        {
            if (!IsReady)
            {
                throw new InvalidOperationException(
                    "DictionaryTranslationProvider chưa được khởi tạo.");
            }
        }

        private sealed record TextRun(
            bool IsCjk,
            string Text);

        private sealed record MatchSlot(
            string Zh,
            string Value,
            List<string> Alternatives,
            string Source,
            int Length);
    }
}