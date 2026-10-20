using Microsoft.Win32;
using System.Security;

namespace Winicode;

public record StartupStatus(bool Enabled, bool Supported, string Message);

public sealed class StartupService
{
    private const string DefaultSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly AppPaths paths;
    private readonly string executablePath;
    private readonly string subKey;
    private static readonly object Gate = new();

    public StartupService(AppPaths paths, string? executablePath = null, string? registrySubKeyOverride = null)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths), "用户数据路径不能为空。");
        var executable = executablePath ?? Environment.ProcessPath ?? Path.Combine(paths.BaseDirectory, "Winicode.exe");
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('"') || executable.Any(char.IsControl))
            throw new ArgumentException("启动程序路径无效。", nameof(executablePath));
        this.executablePath = Path.GetFullPath(executable);
        subKey = registrySubKeyOverride ?? DefaultSubKey;
        if (string.IsNullOrWhiteSpace(subKey) || subKey.StartsWith('\\') || subKey.Contains('/')
            || subKey.Split('\\').Any(part => part is "" or "." or ".." || part.Any(char.IsControl)))
            throw new ArgumentException("自启动注册表子项无效。", nameof(registrySubKeyOverride));
    }

    public StartupStatus GetStatus()
    {
        if (paths.IsPortable) return new(false, false, "便携版不提供登录自启动。可安装正式版后启用。");
        if (!OperatingSystem.IsWindows()) return new(false, false, "当前系统不支持 Windows 登录自启动。");
        lock (Gate)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
                var entry = key?.GetValue("Winicode", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (entry is null) return new(false, true, "登录自启动已关闭。");
                if (!Owns(entry)) return new(false, true, "自启动项属于其他安装。请先处理原安装的自启动设置。");
                return new(true, true, "已注册登录自启动。实际启用状态请在 Windows 启动应用中确认。");
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                return new(false, false, "无法读取自启动状态，请检查注册表访问权限。");
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        EnsureSupported();
        lock (Gate)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
                var entry = key.GetValue("Winicode", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (entry is not null && !Owns(entry))
                    throw new InvalidOperationException("自启动项属于其他安装，已保留该条目。请先处理原安装的自启动设置。");
                if (enabled) key.SetValue("Winicode", $"\"{executablePath}\" --background", RegistryValueKind.String);
                else key.DeleteValue("Winicode", throwOnMissingValue: false);
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                throw new InvalidOperationException("无法更改自启动设置，请检查注册表访问权限。", exception);
            }
        }
    }

    public void RemoveOwnedEntry()
    {
        if (paths.IsPortable || !OperatingSystem.IsWindows()) return;
        lock (Gate)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
                if (Owns(key?.GetValue("Winicode", null, RegistryValueOptions.DoNotExpandEnvironmentNames)))
                    key!.DeleteValue("Winicode", throwOnMissingValue: false);
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                throw new InvalidOperationException("无法清理本安装的自启动项，请检查注册表访问权限。", exception);
            }
        }
    }

    private bool Owns(object? entry)
    {
        if (entry is not string command || string.IsNullOrWhiteSpace(command)) return false;
        command = command.Trim();
        string candidate;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end <= 1 || end + 1 < command.Length && !char.IsWhiteSpace(command[end + 1])) return false;
            candidate = command[1..end];
        }
        else
        {
            var end = command.IndexOfAny([' ', '\t']);
            candidate = end < 0 ? command : command[..end];
        }
        try
        {
            return Path.IsPathFullyQualified(candidate) && string.Equals(Path.GetFullPath(candidate),
                executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private void EnsureSupported()
    {
        if (paths.IsPortable) throw new InvalidOperationException("便携版不提供登录自启动。可安装正式版后启用。");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("当前系统不支持 Windows 登录自启动。");
    }

    private static bool IsRegistryFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException or SecurityException;
}
