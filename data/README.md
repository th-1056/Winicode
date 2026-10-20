# Unicode catalog data

The catalog bundles Unicode **18.0.0** final data for offline use. The versioned
[official UCD ReadMe](https://www.unicode.org/Public/18.0.0/ucd/ReadMe.txt) identifies
the files as final. `sources.json` records the original URLs, download time,
SHA-256 hashes and license. Official files are redistributed verbatim under the
included `LICENSE.txt` (Unicode License V3).

- `UnicodeData.txt`: general categories and First/Last category ranges, including private use.
- `DerivedName.txt`: complete Unicode Name property values, explicit Hangul syllable names,
  and wildcard patterns for CJK and other algorithmic names. The loader expands the patterns.
- `NameAliases.txt`: official correction, control, alternate and abbreviation aliases.
- `math-aliases.tsv`: project-curated Chinese mathematics aliases and descriptions.
  These labels are search aids and have no normative Unicode status.

The TSV columns are hexadecimal scalar, pipe-separated aliases, and Chinese
description, separated by tabs. `Category` uses the two-letter UCD general category.
`NamedCount` counts scalar values with an official Unicode Name, including expanded
ranges; control names supplied by aliases are excluded from that count and remain searchable.
Private-use, unassigned and noncharacter scalar values support direct lookup.
Surrogates and values outside 0..10FFFF are rejected.

`Character` is always the original scalar encoded as UTF-16. `DisplayCharacter`
uses a visible ASCII code-point placeholder for controls, formatting characters,
marks, separators, private-use and unassigned values, plus common blank fillers.
Glyph availability for other characters depends on the application's chosen font.

Search accepts prefixed hexadecimal, 4–6-digit bare hexadecimal, a single scalar,
English name tokens, official aliases and curated Chinese aliases. A known English
token consisting only of hexadecimal letters, such as `face`, keeps its name-search
meaning; use `U+FACE` for the scalar. English tokens are case-insensitive and order
independent; a final incomplete token is expanded when no exact token exists.
An empty/unmatched query or a nonpositive limit returns an empty list. A literal
single space or tab is a character query. `Lookup` throws `ArgumentOutOfRangeException`
for invalid scalar values. `CatalogTests.Run` returns failure messages; an empty
list means every regression check passed.

Refresh the pinned files with PowerShell:

```powershell
.\scripts\download-unicode.ps1
.\scripts\download-unicode.ps1 -VerifyOnly
```

The downloader verifies every staged file before replacing local official data.
It retains the curated aliases and requires a deliberate version/hash edit for
an upstream change. The catalog does not download data at runtime.
