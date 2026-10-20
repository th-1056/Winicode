using System.Text;
using System.Text.Json;

namespace Winicode;

public record CustomAlias(int CodePoint, string Keyword, string Description);

public sealed class AliasStore
{
    // Limits are applied to both imported and locally edited data, before any file changes.
    private const int MaximumCount = 1000;
    private const int MaximumKeywordScalars = 64;
    private const int MaximumDescriptionScalars = 256;
    private const int MaximumBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, MaxDepth = 16
    };
    private readonly AppPaths paths;

    public AliasStore(AppPaths paths) => this.paths = paths ?? throw new ArgumentNullException(nameof(paths), "用户数据路径不能为空。");
    public string? LastWarning {get;private set;}

    public IReadOnlyList<CustomAlias> Load()
    {
        lock (Gate)
        {
            LastWarning=null;
            var damaged = false;
            foreach (var file in new[] { paths.AliasesFile, paths.AliasesFile + ".bak" })
            {
                try
                {
                    var result=Read(file);
                    if(file!=paths.AliasesFile)LastWarning="词条文件无法读取，已从备份恢复。请先检查词条。";
                    return result;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception exception) when (IsReadFailure(exception)) { damaged = true; }
            }
            if (damaged) throw new InvalidDataException("自定义词条及备份无法读取。原文件已保留，请恢复有效的词条文件。");
            return Array.Empty<CustomAlias>();
        }
    }

    public void Save(IReadOnlyList<CustomAlias> aliases)
    {
        var bytes = Serialize(aliases);
        lock (Gate)
        {
            try
            {
                var keepBackup = true;
                if (File.Exists(paths.AliasesFile))
                {
                    try { Read(paths.AliasesFile); }
                    catch (Exception exception) when (IsReadFailure(exception))
                    {
                        AtomicFile.PreserveDamaged(paths.AliasesFile);
                        keepBackup = false;
                    }
                }
                AtomicFile.Write(paths.AliasesFile, bytes, keepBackup);
            }
            catch (Exception exception) when (AtomicFile.IsFileFailure(exception))
            {
                throw new IOException("自定义词条保存失败，原文件已保留。请检查目录权限和可用空间。", exception);
            }
        }
    }

    public IReadOnlyList<CustomAlias> Import(string file)
    {
        try { return Read(file); }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new InvalidDataException("无法导入词条。请检查 JSON 格式、Unicode 码位、重复触发词和文件大小。", exception);
        }
    }

    public void Export(string file, IReadOnlyList<CustomAlias> aliases)
    {
        var bytes = Serialize(aliases);
        try
        {
            var destination = Path.GetFullPath(file);
            foreach (var managed in new[] { paths.SettingsFile, paths.AliasesFile })
                if (string.Equals(destination, managed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(destination, managed + ".bak", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("请选择独立的导出文件，避免覆盖当前设置、词条或备份。");
            lock (Gate) AtomicFile.Write(destination, bytes);
        }
        catch (Exception exception) when (AtomicFile.IsFileFailure(exception) || exception is ArgumentException or NotSupportedException)
        {
            throw new IOException("词条导出失败，原文件已保留。请检查目标目录权限。", exception);
        }
    }

    private static IReadOnlyList<CustomAlias> Read(string file)
    {
        var bytes = AtomicFile.Read(file, MaximumBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("词条文件应为 JSON 数组。");
        if (document.RootElement.GetArrayLength() > MaximumCount) throw new InvalidDataException("自定义词条最多允许 1000 条。");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("词条格式无效。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in item.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if (name is not ("codepoint" or "keyword" or "description") || !names.Add(name))
                    throw new InvalidDataException("词条含未知字段或重复字段，请检查文件版本。");
            }
            if (names.Count != 3) throw new InvalidDataException("词条缺少码位、触发词或说明字段。");
        }
        var aliases = JsonSerializer.Deserialize<CustomAlias[]>(bytes, JsonOptions)
            ?? throw new InvalidDataException("词条文件内容为空。");
        return Validate(aliases);
    }

    private static byte[] Serialize(IReadOnlyList<CustomAlias> aliases)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Validate(aliases), JsonOptions);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("词条文件大小不能超过 1 MiB。");
        return bytes;
    }

    private static IReadOnlyList<CustomAlias> Validate(IReadOnlyList<CustomAlias> aliases)
    {
        if (aliases is null) throw new ArgumentNullException(nameof(aliases), "词条列表不能为空。");
        if (aliases.Count > MaximumCount) throw new InvalidDataException("自定义词条最多允许 1000 条。");
        var result = new List<CustomAlias>(aliases.Count);
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            if (alias is null || !Rune.IsValid(alias.CodePoint)) throw new InvalidDataException("词条码位必须为有效的 Unicode 标量。");
            var keyword = ValidateText(alias.Keyword, MaximumKeywordScalars, allowEmpty: false);
            var description = ValidateText(alias.Description, MaximumDescriptionScalars, allowEmpty: true);
            if (!keywords.Add(keyword)) throw new InvalidDataException("触发词重复，请为每条词条使用独立的触发词。");
            result.Add(new CustomAlias(alias.CodePoint, keyword, description));
        }
        return result.AsReadOnly();
    }

    private static string ValidateText(string? text, int maximumScalars, bool allowEmpty)
    {
        if (text is null) throw new InvalidDataException("词条字段不能为空值。");
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsControl(character)) throw new InvalidDataException("词条字段不能包含控制字符。");
            if (char.IsSurrogate(character))
            {
                if (!char.IsHighSurrogate(character) || index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    throw new InvalidDataException("词条字段含不完整的 Unicode 字符。");
                index++;
            }
            if (++count > maximumScalars) throw new InvalidDataException($"词条字段不能超过 {maximumScalars} 个 Unicode 字符。");
        }
        var normalized = text.Trim().Normalize(NormalizationForm.FormC);
        if (!allowEmpty && normalized.Length == 0) throw new InvalidDataException("触发词不能为空。");
        return normalized;
    }

    private static bool IsReadFailure(Exception exception) => AtomicFile.IsFileFailure(exception)
        || exception is JsonException or InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException;
}
