namespace Winicode;

public sealed class AppPaths
{
    private AppPaths(string baseDirectory, string userDataDirectory, bool isPortable)
    {
        BaseDirectory = baseDirectory;
        DataDirectory = Path.Combine(baseDirectory, "data");
        UserDataDirectory = userDataDirectory;
        SettingsFile = Path.Combine(userDataDirectory, "settings.json");
        AliasesFile = Path.Combine(userDataDirectory, "aliases.json");
        LogsDirectory = Path.Combine(userDataDirectory, "logs");
        IsPortable = isPortable;
    }

    public string BaseDirectory { get; }
    public string DataDirectory { get; }
    public string UserDataDirectory { get; }
    public string SettingsFile { get; }
    public string AliasesFile { get; }
    public string LogsDirectory { get; }
    public bool IsPortable { get; }

    public static AppPaths Resolve(string? baseDirectory = null, string? userDataOverride = null)
    {
        if (baseDirectory is not null && string.IsNullOrWhiteSpace(baseDirectory)
            || userDataOverride is not null && string.IsNullOrWhiteSpace(userDataOverride))
            throw new ArgumentException("程序目录和用户数据目录不能为空。");

        var root = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        var portable = File.Exists(Path.Combine(root, "portable.flag"));
        var userData = userDataOverride ?? (portable ? Path.Combine(root, "userdata")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winicode"));
        return new AppPaths(root, Path.GetFullPath(userData), portable);
    }
}
