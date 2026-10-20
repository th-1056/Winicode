using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Winicode;

public sealed record UnicodeEntry(int CodePoint, string Character, string Name, string Description, string Category)
{
    public string CodeLabel => $"U+{CodePoint:X4}";

    // Character remains the exact insertion value. Never put an isolated bidi control or mark in a label.
    public string DisplayCharacter => Category is "Cc" or "Cf" or "Cs" or "Cn" or "Co" or "Mn" or "Mc" or "Me" or "Zs" or "Zl" or "Zp"
        || CodePoint is 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x2800
            ? $"[{CodeLabel}]" : Character;
}

public sealed class UnicodeCatalog
{
    private readonly Dictionary<int, UnicodeEntry> entries;
    private readonly string[] categories;
    private readonly Dictionary<int, string[]> aliases;
    private readonly Dictionary<string, int[]> tokenIndex;
    private readonly Dictionary<int, string> normalizedNames;

    public int NamedCount { get; }
    public string Version { get; }

    private UnicodeCatalog(string version, int namedCount, Dictionary<int, UnicodeEntry> entries,
        string[] categories, Dictionary<int, string[]> aliases)
    {
        Version = version;
        NamedCount = namedCount;
        this.entries = entries;
        this.categories = categories;
        this.aliases = aliases;
        normalizedNames = entries.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value.Name));

        var postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (codePoint, name) in normalizedNames)
        {
            var words = new HashSet<string>(name.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            if (aliases.TryGetValue(codePoint, out var entryAliases))
                foreach (var alias in entryAliases)
                    words.UnionWith(alias.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            foreach (var word in words)
            {
                if (!postings.TryGetValue(word, out var points)) postings[word] = points = new List<int>();
                points.Add(codePoint);
            }
        }
        tokenIndex = postings.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    public static UnicodeCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "sources.json")));
        var version = metadata.RootElement.GetProperty("version").GetString();
        if (version is null || !System.Version.TryParse(version, out _))
            throw new InvalidDataException("The Unicode data version is missing or invalid.");

        var categories = new string[0x110000];
        Array.Fill(categories, "Cn");
        var entries = new Dictionary<int, UnicodeEntry>();
        var aliases = new Dictionary<int, List<string>>();
        var descriptions = new Dictionary<int, string>();
        int rangeStart = -1;
        string? rangeName = null;
        string? rangeCategory = null;

        foreach (var line in File.ReadLines(Path.Combine(dataDirectory, "UnicodeData.txt")))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split(';');
            if (fields.Length != 15) throw new InvalidDataException("Invalid UnicodeData row.");
            int codePoint = ParseHex(fields[0]);
            if (codePoint is < 0 or > 0x10FFFF) throw new InvalidDataException("Invalid UnicodeData code point.");
            string name = fields[1];
            string category = fields[2];
            if (name.EndsWith(", First>", StringComparison.Ordinal))
            {
                if (rangeStart >= 0) throw new InvalidDataException("Nested UnicodeData range.");
                rangeStart = codePoint;
                rangeName = name[..^8];
                rangeCategory = category;
                continue;
            }
            if (name.EndsWith(", Last>", StringComparison.Ordinal))
            {
                if (rangeStart < 0 || codePoint < rangeStart || name[..^7] != rangeName || category != rangeCategory)
                    throw new InvalidDataException("Unmatched UnicodeData range.");
                Array.Fill(categories, category, rangeStart, codePoint - rangeStart + 1);
                rangeStart = -1;
                continue;
            }
            if (rangeStart >= 0) throw new InvalidDataException("Unclosed UnicodeData range.");
            categories[codePoint] = category;
            if (!IsScalar(codePoint)) continue;
            if (name == "<control>") name = string.IsNullOrEmpty(fields[10]) ? "CONTROL" : fields[10];
            if (!name.StartsWith('<')) entries[codePoint] = MakeEntry(codePoint, name, category);
        }
        if (rangeStart >= 0) throw new InvalidDataException("Unclosed UnicodeData range.");

        // DerivedName includes official Hangul names and wildcard ranges for all algorithmic scripts.
        string derivedPath = Path.Combine(dataDirectory, "DerivedName.txt");
        using (var reader = File.OpenText(derivedPath))
            if (reader.ReadLine() != $"# DerivedName-{version}.txt")
                throw new InvalidDataException("DerivedName and sources.json have different Unicode versions.");
        var namedPoints = new HashSet<int>();
        foreach (var row in DataRows(derivedPath))
        {
            var fields = row.Split(';', 2, StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || fields[1].Length == 0) throw new InvalidDataException("Invalid DerivedName row.");
            var bounds = fields[0].Split("..", StringSplitOptions.None);
            int start = ParseHex(bounds[0]);
            int end = bounds.Length == 2 ? ParseHex(bounds[1]) : start;
            if (bounds.Length > 2 || start > end || !IsScalar(start) || !IsScalar(end))
                throw new InvalidDataException("Invalid DerivedName range.");
            for (int codePoint = start; codePoint <= end; codePoint++)
            {
                if (!IsScalar(codePoint) || !namedPoints.Add(codePoint))
                    throw new InvalidDataException("Duplicate or invalid DerivedName code point.");
                entries[codePoint] = MakeEntry(codePoint,
                    fields[1].Replace("*", codePoint.ToString("X4", CultureInfo.InvariantCulture), StringComparison.Ordinal),
                    categories[codePoint]);
            }
        }

        var controlNames = new HashSet<int>();
        foreach (var row in DataRows(Path.Combine(dataDirectory, "NameAliases.txt")))
        {
            var fields = row.Split(';', StringSplitOptions.TrimEntries);
            if (fields.Length != 3) throw new InvalidDataException("Invalid NameAliases row.");
            int codePoint = ParseHex(fields[0]);
            if (!IsScalar(codePoint)) throw new InvalidDataException("Invalid alias code point.");
            AddAlias(codePoint, fields[1]);
            if (categories[codePoint] == "Cc" && fields[2] == "control" && controlNames.Add(codePoint))
                entries[codePoint] = MakeEntry(codePoint, fields[1], categories[codePoint]);
        }

        foreach (var row in DataRows(Path.Combine(dataDirectory, "math-aliases.tsv")))
        {
            var fields = row.Split('\t');
            if (fields.Length != 3) throw new InvalidDataException("Invalid math alias row.");
            int codePoint = ParseHex(fields[0]);
            if (!entries.ContainsKey(codePoint)) throw new InvalidDataException("Math alias refers to an unnamed scalar.");
            foreach (var alias in fields[1].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                AddAlias(codePoint, alias);
            descriptions[codePoint] = fields[2];
        }
        foreach (var (codePoint, description) in descriptions)
            entries[codePoint] = entries[codePoint] with { Description = $"{description} · {CategoryDescription(categories[codePoint])}" };

        return new UnicodeCatalog(version, namedPoints.Count, entries, categories,
            aliases.ToDictionary(pair => pair.Key, pair => pair.Value.Distinct(StringComparer.Ordinal).ToArray()));

        void AddAlias(int codePoint, string alias)
        {
            if (!aliases.TryGetValue(codePoint, out var list)) aliases[codePoint] = list = new List<string>();
            var normalized = Normalize(alias);
            if (normalized.Length > 0) list.Add(normalized);
        }
    }

    public UnicodeEntry Lookup(int codePoint)
    {
        if (!IsScalar(codePoint))
            throw new ArgumentOutOfRangeException(nameof(codePoint), "A Unicode scalar is in 0..10FFFF and excludes D800..DFFF.");
        if (entries.TryGetValue(codePoint, out var entry)) return entry;
        string category = categories[codePoint];
        string name = category == "Co" ? "PRIVATE USE" : IsNoncharacter(codePoint) ? "NONCHARACTER" : "UNASSIGNED";
        return MakeEntry(codePoint, $"{name}-{codePoint:X4}", category);
    }

    public IReadOnlyList<UnicodeEntry> Search(string query, int limit = 100)
    {
        if (limit <= 0 || string.IsNullOrEmpty(query)) return Array.Empty<UnicodeEntry>();
        for (int index = 0; index < query.Length; index++)
        {
            if (!char.IsSurrogate(query[index])) continue;
            if (!char.IsSurrogatePair(query, index)) return Array.Empty<UnicodeEntry>();
            index++;
        }

        // Check the original input first so a literal space, tab or supplementary scalar survives trimming.
        if (query.Length == 1 && !char.IsSurrogate(query[0])) return new[] { Lookup(query[0]) };
        if (query.Length == 2 && char.IsSurrogatePair(query, 0)) return new[] { Lookup(char.ConvertToUtf32(query, 0)) };
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<UnicodeEntry>();
        if (query.Length == 1 && !char.IsSurrogate(query[0])) return new[] { Lookup(query[0]) };
        if (query.Length == 2 && char.IsSurrogatePair(query, 0)) return new[] { Lookup(char.ConvertToUtf32(query, 0)) };

        bool prefixed = query.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        string hex = prefixed ? query[2..] : query;
        bool allHex = hex.Length is >= 1 and <= 6 && hex.All(char.IsAsciiHexDigit);
        string normalized = Normalize(query);
        // A known name token such as FACE stays a word; its explicit U+FACE form is always hexadecimal.
        bool nameWord = !prefixed && query.All(char.IsAsciiLetter) && tokenIndex.ContainsKey(normalized);
        if (prefixed || (query.Length is >= 4 and <= 6 && allHex && !nameWord))
            return allHex && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int scalar) && IsScalar(scalar)
                ? new[] { Lookup(scalar) } : Array.Empty<UnicodeEntry>();

        if (normalized.Length == 0) return Array.Empty<UnicodeEntry>();
        string[] words = normalized.Split(' ');
        HashSet<int>? candidates = null;
        for (int index = 0; index < words.Length; index++)
        {
            var word = words[index];
            IEnumerable<int> matches;
            if (tokenIndex.TryGetValue(word, out var points)) matches = points;
            else if (index == words.Length - 1 && word.Length >= 2)
                matches = tokenIndex.Where(pair => pair.Key.StartsWith(word, StringComparison.Ordinal)).SelectMany(pair => pair.Value);
            else return Array.Empty<UnicodeEntry>();
            if (candidates is null) candidates = new HashSet<int>(matches);
            else candidates.IntersectWith(matches);
            if (candidates.Count == 0) return Array.Empty<UnicodeEntry>();
        }

        return candidates!.Select(codePoint => entries[codePoint])
            .OrderBy(entry => MatchRank(entry, normalized))
            .ThenBy(entry => CommonRank(entry.CodePoint))
            .ThenBy(entry => normalizedNames[entry.CodePoint].Length)
            .ThenBy(entry => entry.CodePoint)
            .Take(limit).ToArray();
    }

    private int MatchRank(UnicodeEntry entry, string query)
    {
        string name = normalizedNames[entry.CodePoint];
        if (name == query) return 0;
        if (aliases.TryGetValue(entry.CodePoint, out var values) && values.Contains(query, StringComparer.Ordinal)) return 10;
        if (name == "GREEK SMALL LETTER " + query) return 20;
        if (name == "GREEK CAPITAL LETTER " + query) return 21;
        if (name == "MATHEMATICAL ITALIC SMALL " + query) return 22;
        return name.Contains(query, StringComparison.Ordinal) ? 50 : 60;
    }

    private static int CommonRank(int codePoint) => codePoint switch
    {
        >= 0x03B1 and <= 0x03C9 => 0,
        0x222B or 0x221E or 0x2200 or 0x2203 or 0x2192 or 0x2211 or 0x2202 => 0,
        >= 0x0391 and <= 0x03A9 => 1,
        _ => 10
    };

    private static IEnumerable<string> DataRows(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string row = line.Split('#', 2)[0].Trim();
            if (row.Length > 0) yield return row;
        }
    }

    private static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char character in text)
            if (char.IsLetterOrDigit(character)) result.Append(char.ToUpperInvariant(character));
            else if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
        return result.ToString().TrimEnd();
    }

    private static int ParseHex(string text) => int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static bool IsScalar(int value) => value is >= 0 and <= 0x10FFFF && value is not (>= 0xD800 and <= 0xDFFF);
    private static bool IsNoncharacter(int value) => value is >= 0xFDD0 and <= 0xFDEF || (value & 0xFFFF) is 0xFFFE or 0xFFFF;
    private static UnicodeEntry MakeEntry(int codePoint, string name, string category) =>
        new(codePoint, char.ConvertFromUtf32(codePoint), name, CategoryDescription(category), category);

    private static string CategoryDescription(string category) => category switch
    {
        "Lu" => "大写字母", "Ll" => "小写字母", "Lt" => "标题字母", "Lm" => "修饰字母", "Lo" => "其他字母",
        "Mn" => "非间距组合标记", "Mc" => "间距组合标记", "Me" => "包围组合标记",
        "Nd" => "十进制数字", "Nl" => "字母数字", "No" => "其他数字",
        "Pc" => "连接标点", "Pd" => "连字符标点", "Ps" => "起始标点", "Pe" => "结束标点",
        "Pi" => "起始引号", "Pf" => "结束引号", "Po" => "其他标点",
        "Sm" => "数学符号", "Sc" => "货币符号", "Sk" => "修饰符号", "So" => "其他符号",
        "Zs" => "空格分隔符", "Zl" => "行分隔符", "Zp" => "段落分隔符",
        "Cc" => "控制字符", "Cf" => "格式字符", "Co" => "私用字符，字形取决于字体", "Cs" => "代理项",
        _ => "未分配或非字符码位，字体可能无字形"
    };
}
