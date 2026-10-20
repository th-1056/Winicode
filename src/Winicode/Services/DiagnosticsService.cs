using System.Globalization;
using System.Text;

namespace Winicode;

public sealed class DiagnosticsService
{
    private const long MaximumLogBytes = 256 * 1024;
    private static readonly object Gate = new();
    private readonly AppPaths paths;

    public DiagnosticsService(AppPaths paths) => this.paths = paths ?? throw new ArgumentNullException(nameof(paths), "用户数据路径不能为空。");

    public void LogException(Exception exception, string context)
    {
        // Callers may pass input or a path as context/message. Only fixed categories enter logs.
        try
        {
            var category = exception switch
            {
                UnauthorizedAccessException => "访问受限",
                IOException => "文件访问异常",
                System.Text.Json.JsonException => "数据格式异常",
                System.Net.Http.HttpRequestException => "网络异常",
                OperationCanceledException => "操作取消",
                ArgumentException => "参数异常",
                InvalidOperationException => "操作状态异常",
                _ => "运行异常"
            };
            var area = context switch
            {
                "startup" => "启动", "settings" => "设置", "aliases" => "自定义词条",
                "release-check" => "更新检查", "shutdown" => "退出", _ => "运行"
            };
            var line = $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}\t{area}\t{category}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetBytes(line);
            lock (Gate)
            {
                Directory.CreateDirectory(paths.LogsDirectory);
                var log = Path.Combine(paths.LogsDirectory, "diagnostics.log");
                if (File.Exists(log) && new FileInfo(log).Length + bytes.Length > MaximumLogBytes)
                {
                    File.Delete(log + ".2");
                    if (File.Exists(log + ".1")) File.Move(log + ".1", log + ".2");
                    File.Move(log, log + ".1");
                }
                using var stream = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
        }
        catch (Exception) { /* Logging must never prevent startup, recovery, or shutdown. */ }
    }

    public string Preview(AppSettings settings, string status)
    {
        settings = (settings ?? new AppSettings()).Normalize();
        var state = status switch
        {
            "running" or "运行中" or "正在运行" => "运行中",
            "paused" or "已暂停" or "暂停" => "已暂停",
            _ => "状态摘要已隐藏"
        };
        return string.Join(Environment.NewLine,
            "Winicode 脱敏诊断报告",
            "软件版本：" + BuildInfo.Version,
            "系统版本：" + Environment.OSVersion.Version,
            "进程架构：" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            "运行环境：.NET " + Environment.Version,
            "运行模式：" + (paths.IsPortable ? "便携版" : "安装版"),
            "运行状态：" + state,
            "设置格式：1",
            "U+ 按钮：" + OnOff(settings.ShowDockButton),
            "0 键唤起：" + OnOff(settings.EnableZeroKey),
            "带入临时输入：" + OnOff(settings.CarryPreedit),
            "数字选字：" + OnOff(settings.NumericSelection),
            "主题：" + settings.Theme,
            "字号：" + settings.FontPreset,
            "排除应用数量：" + settings.ExcludedApplications.Length.ToString(CultureInfo.InvariantCulture),
            "首次引导完成：" + OnOff(settings.FirstRunCompleted),
            "报告仅含版本、固定状态和设置开关；用户数据路径、输入内容及异常详情均已省略。",
            "");
    }

    public string Export(AppSettings settings, string status)
    {
        var file = Path.Combine(paths.UserDataDirectory, $"Winicode-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
        try
        {
            AtomicFile.Write(file, Encoding.UTF8.GetBytes(Preview(settings, status)), keepBackup: false);
            return file;
        }
        catch (Exception exception) when (AtomicFile.IsFileFailure(exception))
        {
            throw new IOException("诊断报告导出失败，请检查用户数据目录的写入权限。", exception);
        }
    }

    private static string OnOff(bool enabled) => enabled ? "开启" : "关闭";
}
