using System;
using System.Collections.Generic;
using System.Linq;

namespace Winicode;

/// <summary>Dependency-free regression checks. Run returns failures; an empty list means success.</summary>
public static class CatalogTests
{
    public static IReadOnlyList<string> Run(UnicodeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var failures = new List<string>();

        void Check(string name, Action assertion)
        {
            try { assertion(); }
            catch (Exception exception) { failures.Add($"{name}: {exception.Message}"); }
        }

        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        void First(string query, int expected)
        {
            var results = catalog.Search(query);
            Require(results.Count > 0 && results[0].CodePoint == expected,
                $"Search '{query}' must start with U+{expected:X4}.");
        }

        Check("Pinned full name data", () =>
        {
            Require(catalog.Version == "18.0.0", "Expected the bundled Unicode 18.0.0 data.");
            Require(catalog.NamedCount > 170_000, "Full names, including expanded ranges, must be loaded.");
        });
        Check("Non-BMP scalar and UTF-16", () =>
        {
            var entry = catalog.Lookup(0x1D6FC);
            Require(entry.Character == "\uD835\uDEFC" && entry.Character.Length == 2,
                "Mathematical italic alpha must retain its exact surrogate pair.");
            Require(entry.Name == "MATHEMATICAL ITALIC SMALL ALPHA" && entry.Category == "Ll",
                "Wrong non-BMP name or category.");
            Require(entry.CodeLabel == "U+1D6FC" && entry.DisplayCharacter == entry.Character,
                "Visible non-BMP letters must keep their display glyph.");
        });
        Check("Hexadecimal queries", () =>
        {
            foreach (var query in new[] { "U+1D6FC", "u+1d6fc", "0x1D6FC", "1d6fc", "  U+1D6FC  " })
                First(query, 0x1D6FC);
            First("03B1", 0x03B1);
            First("abcd", 0xABCD);
            First("U+FACE", 0xFACE);
            First("U+A", 0x000A);
        });
        Check("Character literals", () =>
        {
            First("α", 0x03B1);
            First("  α  ", 0x03B1);
            First("\uD835\uDEFC", 0x1D6FC);
            First("\uD83D\uDE00", 0x1F600);
            First("A", 0x0041);
            First(" ", 0x0020);
            First("\t", 0x0009);
            First("\u202E", 0x202E);
        });
        Check("Ordinary hex-shaped English word", () =>
        {
            var results = catalog.Search("face");
            Require(results.Count > 0 && results[0].CodePoint != 0xFACE,
                "The word face must remain a name search; U+FACE selects the code point.");
            Require(results.All(entry => entry.Name.Contains("FACE", StringComparison.Ordinal)),
                "Face results must match a name token.");
        });
        Check("Greek alpha ranking and exact name", () =>
        {
            First("alpha", 0x03B1);
            First("ALPHA", 0x03B1);
            First("greek small letter alpha", 0x03B1);
            First("MATHEMATICAL ITALIC SMALL ALPHA", 0x1D6FC);
            Require(catalog.Search("alpha").Any(entry => entry.CodePoint == 0x1D6FC),
                "Alpha must also find styled mathematical forms.");
        });
        Check("English tokens, prefix and official aliases", () =>
        {
            First("INFINITY", 0x221E);
            First("rightwards arrow", 0x2192);
            First("arrow rightwards", 0x2192);
            First("greek_small-letter-alpha", 0x03B1);
            First("alph", 0x03B1);
            First("LINE FEED", 0x000A);
            Require(catalog.Lookup(0x000A).Name == "LINE FEED", "Use the first official control name.");
            First("ZWJ", 0x200D);
        });
        Check("Chinese mathematics aliases", () =>
        {
            foreach (var (query, scalar) in new (string, int)[]
            {
                ("阿尔法", 0x03B1), ("积分", 0x222B), ("无穷", 0x221E),
                ("全称", 0x2200), ("存在", 0x2203), ("箭头", 0x2192),
                ("贝塔", 0x03B2), ("求和", 0x2211), ("偏导", 0x2202)
            }) First(query, scalar);
            Require(catalog.Search("积分").Any(entry => entry.CodePoint == 0x222E),
                "Integral alias should include contour integrals.");
        });
        Check("Algorithmic CJK and Hangul ranges", () =>
        {
            foreach (var scalar in new[] { 0x3400, 0x4E00, 0x9FFF, 0x20000, 0x2A6DF, 0x33479 })
                Require(catalog.Lookup(scalar).Name == $"CJK UNIFIED IDEOGRAPH-{scalar:X4}",
                    $"Missing algorithmic CJK name at U+{scalar:X}.");
            Require(catalog.Lookup(0xAC00).Name == "HANGUL SYLLABLE GA", "Wrong first Hangul name.");
            Require(catalog.Lookup(0xAC01).Name == "HANGUL SYLLABLE GAG", "Wrong interior Hangul name.");
            Require(catalog.Lookup(0xD7A3).Name == "HANGUL SYLLABLE HIH", "Wrong final Hangul name.");
            First("CJK UNIFIED IDEOGRAPH-20000", 0x20000);
            First("HANGUL SYLLABLE HIH", 0xD7A3);
            Require(catalog.Lookup(0x17000).Name == "TANGUT IDEOGRAPH-17000", "Missing Tangut range.");
        });
        Check("Categories from the pinned UCD", () =>
        {
            Require(catalog.Lookup(0x18E00).Category == "Lo", "Unicode 18 Jurchen must be assigned.");
            Require(catalog.Lookup(0x3D000).Name == "SMALL SEAL CHARACTER-3D000",
                "Unicode 18 Small Seal names must be present.");
        });
        Check("Unassigned, private-use and noncharacter scalars", () =>
        {
            Require(catalog.Lookup(0x0378).Category == "Cn", "Unassigned scalars must be addressable.");
            Require(catalog.Lookup(0xE000).Category == "Co", "Wrong BMP private-use category.");
            Require(catalog.Lookup(0xF0000).Category == "Co", "Wrong non-BMP private-use category.");
            Require(catalog.Lookup(0x10FFFF).Character == "\uDBFF\uDFFF", "Last scalar must be valid.");
            Require(catalog.Lookup(0x10FFFF).Category == "Cn", "Noncharacters have category Cn.");
            Require(catalog.Lookup(0).CodeLabel == "U+0000", "Code labels require at least four hex digits.");
            First("U+0378", 0x0378);
            First("10FFFF", 0x10FFFF);
        });
        Check("Reject invalid scalars", () =>
        {
            foreach (var scalar in new[] { -1, 0xD800, 0xDBFF, 0xDC00, 0xDFFF, 0x110000 })
            {
                try { catalog.Lookup(scalar); }
                catch (ArgumentOutOfRangeException) { continue; }
                throw new InvalidOperationException($"Lookup accepted invalid scalar {scalar:X}.");
            }
            foreach (var query in new[] { "U+D800", "0xDFFF", "D800", "U+110000", "U+-1", "0x", "U+XYZ", "\uD800", "\uDC00", "\uD800A", "A\uDC00", "\uD800\uD800" })
                Require(catalog.Search(query).Count == 0, "Invalid queries must not return replacement characters.");
        });
        Check("Safe display for controls, formats, marks and spaces", () =>
        {
            foreach (var scalar in new[] { 0, 0x000A, 0x202E, 0x2066, 0x200D, 0x0301, 0x0903, 0x20DD, 0xFE0F, 0xE0100, 0x0020, 0x0378 })
            {
                var entry = catalog.Lookup(scalar);
                Require(entry.Character == char.ConvertFromUtf32(scalar), "Copy value must remain exact.");
                Require(entry.DisplayCharacter.Contains(entry.CodeLabel, StringComparison.Ordinal),
                    $"U+{scalar:X4} needs a visible code-point placeholder.");
                Require(!entry.DisplayCharacter.Contains(entry.Character, StringComparison.Ordinal),
                    $"U+{scalar:X4} leaked its invisible character into the display.");
            }
        });
        Check("Empty and unmatched queries", () =>
        {
            foreach (var query in new[] { "", "   ", "this-name-does-not-exist-xyz", "不存在的数学名称" })
                Require(catalog.Search(query).Count == 0, "An empty or missing name must return an empty list.");
            Require(catalog.Search(null!).Count == 0, "Null query should behave like an empty query.");
        });
        Check("Limits and deterministic unique results", () =>
        {
            Require(catalog.Search("alpha", 0).Count == 0, "Zero limit must return no results.");
            Require(catalog.Search("alpha", -1).Count == 0, "Negative limit must return no results.");
            var results = catalog.Search("letter", 7);
            Require(results.Count == 7, "Result limit must be respected.");
            Require(results.Select(entry => entry.CodePoint).Distinct().Count() == 7, "Results must be unique.");
            Require(results.SequenceEqual(catalog.Search("letter", 7)), "Ranking must be deterministic.");
        });

        return failures.AsReadOnly();
    }
}
