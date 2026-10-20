using System.Text.Json;

namespace Winicode;

public sealed class SettingsStore
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, MaxDepth = 16
    };
    private readonly AppPaths paths;

    public SettingsStore(AppPaths paths) => this.paths = paths ?? throw new ArgumentNullException(nameof(paths), "用户数据路径不能为空。");
    public string? LastWarning { get; private set; }

    public AppSettings Load()
    {
        lock (Gate)
        {
            LastWarning = null;
            foreach (var file in new[] { paths.SettingsFile, paths.SettingsFile + ".bak" })
            {
                try
                {
                    var settings = Read(file);
                    if (file != paths.SettingsFile) LastWarning = "设置文件无法读取，已从备份恢复。请确认设置后保存。";
                    return settings;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception exception) when (IsReadFailure(exception))
                {
                    LastWarning = exception is NotSupportedException
                        ? "设置文件来自其他版本。已保留原文件，请使用兼容版本打开。"
                        : "设置文件无法读取。已保留原文件，暂时使用默认设置。";
                    // A future schema must never be silently downgraded to a backup.
                    if (exception is NotSupportedException) return new AppSettings();
                }
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings), "设置不能为空。");
        if (settings.SchemaVersion != 1) throw new NotSupportedException("当前版本无法保存此设置格式，请使用兼容版本。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings.Normalize(), JsonOptions);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("设置文件超过大小限制，请减少排除应用条目。");
        lock (Gate)
        {
            try
            {
                var keepBackup = true;
                if (File.Exists(paths.SettingsFile))
                {
                    try { Read(paths.SettingsFile); }
                    catch (Exception exception) when (IsReadFailure(exception) && exception is not NotSupportedException)
                    {
                        AtomicFile.PreserveDamaged(paths.SettingsFile);
                        keepBackup = false;
                    }
                }
                else if (File.Exists(paths.SettingsFile + ".bak"))
                {
                    // An orphaned backup may also belong to a newer version.
                    try { Read(paths.SettingsFile + ".bak"); }
                    catch (Exception exception) when (IsReadFailure(exception) && exception is not NotSupportedException) { }
                }
                AtomicFile.Write(paths.SettingsFile, bytes, keepBackup);
                LastWarning = null;
            }
            catch (Exception exception) when (AtomicFile.IsFileFailure(exception) || exception is JsonException)
            {
                LastWarning = "设置保存失败，原有文件已保留。请检查目录权限和可用空间。";
                throw new IOException(LastWarning, exception);
            }
        }
    }

    private static AppSettings Read(string file)
    {
        var bytes = AtomicFile.Read(file, MaximumBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("设置格式无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException("设置中存在重复字段。");
        var settings = JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions) ?? throw new JsonException("设置内容为空。");
        if (settings.SchemaVersion != 1) throw new NotSupportedException("设置格式版本不兼容。");
        return settings.Normalize();
    }

    private static bool IsReadFailure(Exception exception) => AtomicFile.IsFileFailure(exception)
        || exception is JsonException or InvalidDataException or NotSupportedException or ArgumentException;
}
