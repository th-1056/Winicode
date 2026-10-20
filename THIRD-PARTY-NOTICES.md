# Third-party notices

The MIT license at the repository root covers original Winicode code and curated project data. Third-party data, runtime components and installer components retain their own notices and terms.

## Unicode data

Winicode bundles Unicode **18.0.0 final** data from the [Unicode Character Database](https://www.unicode.org/Public/18.0.0/ucd/). The official files are `UnicodeData.txt`, `DerivedName.txt`, `NameAliases.txt` and `ReadMe.txt`. The original URLs and SHA-256 values are recorded in `data/sources.json`.

The complete **Unicode License V3**, including Unicode, Inc.'s copyright and permission notice, is retained verbatim in [data/LICENSE.txt](data/LICENSE.txt) and shipped in both binary packages. Project-curated Chinese aliases in `math-aliases.tsv` are search aids maintained by Winicode contributors. Unicode does not supply or endorse those translations.

## .NET runtime

The self-contained packages redistribute the .NET 10 runtime and Windows Desktop runtime from Microsoft. The [.NET runtime](https://github.com/dotnet/runtime) and [Windows Desktop](https://github.com/dotnet/wpf) repositories provide upstream licensing information. Bundled components may carry additional third-party terms.

The packaging script copies `LICENSE.txt` and `ThirdPartyNotices.txt` from the official SDK distribution into `licenses/dotnet-LICENSE.txt` and `licenses/dotnet-THIRD-PARTY-NOTICES.txt`. Packaging fails if either notice is missing. Preserve these files when redistributing a binary package. The exact SDK used is recorded in `release-manifest.json`.

## Inno Setup

The Windows installer uses [Inno Setup](https://jrsoftware.org/), by Jordan Russell and Martijn Laan. CI release packaging requires **7.1.0 x64**; local scripts accept existing **6.7+** compilers and record the actual compiler version. The [upstream license](https://github.com/jrsoftware/issrc/blob/is-7_1_0/license.txt) is retained in the source at `installer/licenses/InnoSetup-LICENSE.txt` and distributed in `licenses/InnoSetup-LICENSE.txt`. Upstream components retain the notices embedded in their original binaries.

These acknowledgments do not imply endorsement of Winicode by the upstream projects. Build tools themselves are excluded from the source export and application payload.
