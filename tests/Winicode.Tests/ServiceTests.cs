using Microsoft.Win32;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Winicode;

/// <summary>Offline checks; all writes use disposable directories or a unique test registry subkey.</summary>
public static class ServiceTests
{
    public static IReadOnlyList<string> Run()
    {
        var failures = new List<string>();
        void Check(string name, Action<Fixture> assertion)
        {
            try { using var fixture = new Fixture(); assertion(fixture); }
            catch (Exception exception) { failures.Add($"{name}：{exception.Message}"); }
        }

        Check("设置契约默认值与规范化", fixture =>
        {
            var settings = new AppSettings();
            Require(settings.SchemaVersion == 1 && settings.ShowDockButton && settings.EnableZeroKey
                && settings.CarryPreedit && settings.NumericSelection && settings.Theme == "system"
                && settings.FontPreset == "default" && settings.ExcludedApplications.Length == 0
                && !settings.CheckUpdatesAutomatically && !settings.FirstRunCompleted, "设置默认值不符合契约。");
            var normalized = (settings with
            {
                Theme = " DARK ", FontPreset = "LARGE", ExcludedApplications =
                    [" Code.exe ", "code.EXE", "", "C:\\Users\\private\\editor.exe", "bad\nname.exe"]
            }).Normalize();
            Require(normalized.Theme == "dark" && normalized.FontPreset == "large"
                && normalized.ExcludedApplications.SequenceEqual(["Code.exe"]), "设置规范化失败。");
            Require((settings with { Theme = null!, FontPreset = "unknown", ExcludedApplications = null! }).Normalize()
                is { Theme: "system", FontPreset: "default", ExcludedApplications.Length: 0 }, "无效设置未回退默认值。");
            Require((settings with { ShowDockButton = false, EnableZeroKey = false }).Normalize()
                is { ShowDockButton: false, EnableZeroKey: false }, "规范化意外启用了关闭的入口。");
        });

        Check("安装与便携路径无读取副作用", fixture =>
        {
            Require(!fixture.Paths.IsPortable && fixture.Paths.DataDirectory == Path.Combine(fixture.Base, "data"), "安装路径解析失败。");
            var installed = AppPaths.Resolve(fixture.Base);
            Require(installed.UserDataDirectory == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winicode"), "默认用户目录错误。");
            File.WriteAllText(Path.Combine(fixture.Base, "portable.flag"), "");
            var portable = AppPaths.Resolve(fixture.Base);
            Require(portable.IsPortable && portable.UserDataDirectory == Path.Combine(fixture.Base, "userdata"), "便携路径错误。");
            Require(!Directory.Exists(portable.UserDataDirectory), "路径解析创建了用户数据目录。");
            Require(AppPaths.Resolve(fixture.Base, fixture.UserData).UserDataDirectory == fixture.UserData, "数据目录覆盖失效。");
        });

        Check("设置往返与原子备份", fixture =>
        {
            var store = new SettingsStore(fixture.Paths);
            Require(store.Load().Theme == "system" && store.LastWarning is null, "新目录未使用默认设置。");
            store.Save(new AppSettings { Theme = "dark", FirstRunCompleted = true, ExcludedApplications = ["code.exe"] });
            var oldBytes = File.ReadAllBytes(fixture.Paths.SettingsFile);
            store.Save(store.Load() with { Theme = "light", EnableZeroKey = false });
            var settings = new SettingsStore(fixture.Paths).Load();
            Require(settings is { Theme: "light", EnableZeroKey: false, FirstRunCompleted: true }
                && settings.ExcludedApplications.SequenceEqual(["code.exe"]), "设置未持久化。");
            Require(File.ReadAllBytes(fixture.Paths.SettingsFile + ".bak").SequenceEqual(oldBytes), "原设置备份不一致。");
            Require(!Directory.EnumerateFiles(fixture.UserData, "*.tmp").Any(), "遗留了临时文件。");
        });

        Check("损坏设置恢复保留原件与有效备份", fixture =>
        {
            var store = new SettingsStore(fixture.Paths);
            store.Save(new AppSettings { Theme = "dark" });
            store.Save(new AppSettings { Theme = "light" });
            var backup = File.ReadAllBytes(fixture.Paths.SettingsFile + ".bak");
            File.WriteAllText(fixture.Paths.SettingsFile, "{broken-json");
            Require(store.Load().Theme == "dark" && store.LastWarning is not null, "损坏设置未从备份恢复。");
            Require(File.ReadAllText(fixture.Paths.SettingsFile) == "{broken-json", "读取覆盖了损坏原件。");
            store.Save(store.Load());
            Require(File.ReadAllBytes(fixture.Paths.SettingsFile + ".bak").SequenceEqual(backup), "恢复保存覆盖了有效备份。");
            Require(Directory.EnumerateFiles(fixture.UserData, "settings.json.damaged.*")
                .Any(file => File.ReadAllText(file) == "{broken-json"), "恢复保存丢失了损坏原件。");
        });

        Check("孤立备份与双重损坏", fixture =>
        {
            Directory.CreateDirectory(fixture.UserData);
            File.WriteAllText(fixture.Paths.SettingsFile + ".bak", "{\"theme\":\"dark\"}");
            var store = new SettingsStore(fixture.Paths);
            Require(store.Load().Theme == "dark" && store.LastWarning is not null, "孤立备份未恢复。");
            File.WriteAllText(fixture.Paths.SettingsFile, "null");
            File.WriteAllText(fixture.Paths.SettingsFile + ".bak", "[]");
            Require(store.Load().Theme == "system" && store.LastWarning is not null, "双重损坏未提供默认值和警告。");
            Require(File.ReadAllText(fixture.Paths.SettingsFile) == "null", "加载破坏了原文件。");
        });

        Check("未来设置格式阻止降级覆盖", fixture =>
        {
            Directory.CreateDirectory(fixture.UserData);
            const string future = "{\"schemaVersion\":2,\"futureValue\":\"keep-me\"}";
            File.WriteAllText(fixture.Paths.SettingsFile, future);
            File.WriteAllText(fixture.Paths.SettingsFile + ".bak", "{}");
            var store = new SettingsStore(fixture.Paths);
            Require(store.Load().SchemaVersion == 1 && store.LastWarning is not null, "未来格式缺少警告。");
            Throws<NotSupportedException>(() => store.Save(new AppSettings()));
            Require(File.ReadAllText(fixture.Paths.SettingsFile) == future, "未来格式被覆盖。");
            File.Delete(fixture.Paths.SettingsFile);
            File.WriteAllText(fixture.Paths.SettingsFile + ".bak", future);
            Throws<NotSupportedException>(() => store.Save(new AppSettings()));
            Require(!File.Exists(fixture.Paths.SettingsFile), "未来孤立备份被静默降级。");
        });

        Check("设置文件大小与重复字段", fixture =>
        {
            Directory.CreateDirectory(fixture.UserData);
            var store = new SettingsStore(fixture.Paths);
            File.WriteAllBytes(fixture.Paths.SettingsFile, new byte[256 * 1024 + 1]);
            Require(store.Load().Theme == "system" && store.LastWarning is not null, "超大设置未受限。");
            File.WriteAllText(fixture.Paths.SettingsFile, "{\"theme\":\"light\",\"Theme\":\"dark\"}");
            Require(store.Load().Theme == "system" && store.LastWarning is not null, "重复字段被接受。");
        });

        Check("设置写入被占用保持原件", fixture =>
        {
            var store = new SettingsStore(fixture.Paths);
            store.Save(new AppSettings { Theme = "dark" });
            var original = File.ReadAllBytes(fixture.Paths.SettingsFile);
            using (File.Open(fixture.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                Throws<IOException>(() => store.Save(new AppSettings { Theme = "light" }));
            Require(File.ReadAllBytes(fixture.Paths.SettingsFile).SequenceEqual(original), "失败保存修改了原设置。");
            Require(store.LastWarning is not null && !Directory.EnumerateFiles(fixture.UserData, "*.tmp").Any(), "失败保存未警告或遗留临时文件。");
        });

        Check("数据目录不可写的文件冲突", fixture =>
        {
            File.WriteAllText(fixture.UserData, "directory-blocker");
            var store = new SettingsStore(fixture.Paths);
            Throws<IOException>(() => store.Save(new AppSettings()));
            Require(File.ReadAllText(fixture.UserData) == "directory-blocker", "覆盖了阻塞数据目录的文件。");
        });

        Check("词条往返与补充平面", fixture =>
        {
            var store = new AliasStore(fixture.Paths);
            Require(store.Load().Count == 0, "新目录词条应为空。");
            var aliases = new[] { new CustomAlias(0x1D6FC, " styled-alpha ", " 数学斜体阿尔法 "), new CustomAlias(0x10FFFF, "last-scalar", "") };
            store.Save(aliases);
            var loaded = store.Load();
            Require(loaded.Count == 2 && loaded[0] == new CustomAlias(0x1D6FC, "styled-alpha", "数学斜体阿尔法")
                && loaded[1].CodePoint == 0x10FFFF, "词条往返丢失数据。");
            var exported = Path.Combine(fixture.Root, "export.json");
            store.Export(exported, loaded);
            Require(store.Import(exported).SequenceEqual(loaded), "词条导入导出不一致。");
            store.Save([new CustomAlias(0, "null-scalar", "合法标量")]);
            Require(store.Load().Single().CodePoint == 0 && File.Exists(fixture.Paths.AliasesFile + ".bak"), "标量零或词条备份失效。");
        });

        Check("词条验证失败保持原数据", fixture =>
        {
            var store = new AliasStore(fixture.Paths);
            store.Save([new CustomAlias(0x03B1, "alpha", "阿尔法")]);
            var original = File.ReadAllBytes(fixture.Paths.AliasesFile);
            foreach (var scalar in new[] { -1, 0xD800, 0xDFFF, 0x110000 })
                Throws<InvalidDataException>(() => store.Save([new CustomAlias(scalar, "invalid", "")]));
            foreach (var keyword in new[] { "", "  ", new string('a', 65), "\uD800", "bad\nword" })
                Throws<InvalidDataException>(() => store.Save([new CustomAlias(1, keyword, "")]));
            Throws<InvalidDataException>(() => store.Save([new CustomAlias(1, "ok", new string('a', 257))]));
            Throws<InvalidDataException>(() => store.Save([new CustomAlias(1, "ok", "\uDC00")]));
            Throws<InvalidDataException>(() => store.Save([null!]));
            Require(File.ReadAllBytes(fixture.Paths.AliasesFile).SequenceEqual(original), "校验失败仍修改了词条文件。");
        });

        Check("词条 Unicode 长度与重复冲突", fixture =>
        {
            var store = new AliasStore(fixture.Paths);
            var keyword = string.Concat(Enumerable.Repeat("\U0001F600", 64));
            store.Save([new CustomAlias(0x1F600, keyword, new string('a', 256))]);
            Require(store.Load().Single().Keyword == keyword, "字符长度错误使用了 UTF-16 单元计数。");
            Throws<InvalidDataException>(() => store.Save([new CustomAlias(1, "Alpha", ""), new CustomAlias(2, " alpha ", "")]));
            Throws<InvalidDataException>(() => store.Save([new CustomAlias(1, "é", ""), new CustomAlias(2, "e\u0301", "")]));
            var aliases = Enumerable.Range(0, 1000).Select(index => new CustomAlias(index, "key" + index, "")).ToArray();
            store.Save(aliases);
            Require(store.Load().Count == 1000, "合法数量边界被拒绝。");
            Throws<InvalidDataException>(() => store.Save(aliases.Append(new CustomAlias(1000, "overflow", "")).ToArray()));
            Require(store.Load().Count == 1000, "数量限制失败破坏了现有数据。");
        });

        Check("导入整体校验且不隐式保存", fixture =>
        {
            var store = new AliasStore(fixture.Paths);
            store.Save([new CustomAlias(1, "old", "")]);
            var original = File.ReadAllBytes(fixture.Paths.AliasesFile);
            var file = Path.Combine(fixture.Root, "import.json");
            File.WriteAllText(file, "[{\"codePoint\":2,\"keyword\":\"new\",\"description\":\"\"}]");
            Require(store.Import(file).Single().Keyword == "new", "有效导入失败。");
            foreach (var json in new[]
            {
                "null", "{}", "[null]", "[{}]", "[{\"codePoint\":1,\"keyword\":\"a\",\"description\":null}]",
                "[{\"codePoint\":1,\"keyword\":\"a\",\"Keyword\":\"b\",\"description\":\"\"}]",
                "[{\"codePoint\":1,\"keyword\":\"a\",\"description\":\"\",\"future\":true}]",
                "[{\"codePoint\":1,\"keyword\":\"\\uD800\",\"description\":\"\"}]",
                "[{\"codePoint\":1,\"keyword\":\"a\",\"description\":\"\"},{\"codePoint\":55296,\"keyword\":\"b\",\"description\":\"\"}]"
            })
            {
                File.WriteAllText(file, json);
                Throws<InvalidDataException>(() => store.Import(file));
            }
            File.WriteAllBytes(file, new byte[1024 * 1024 + 1]);
            Throws<InvalidDataException>(() => store.Import(file));
            Require(File.ReadAllBytes(fixture.Paths.AliasesFile).SequenceEqual(original), "导入操作修改了已保存的词条。");
        });

        Check("损坏词条恢复与双重损坏保护", fixture =>
        {
            var store = new AliasStore(fixture.Paths);
            store.Save([new CustomAlias(1, "first", "")]);
            store.Save([new CustomAlias(2, "second", "")]);
            var backup = File.ReadAllBytes(fixture.Paths.AliasesFile + ".bak");
            File.WriteAllText(fixture.Paths.AliasesFile, "broken");
            Require(store.Load().Single().Keyword == "first", "词条未从有效备份恢复。");
            store.Save(store.Load());
            Require(File.ReadAllBytes(fixture.Paths.AliasesFile + ".bak").SequenceEqual(backup), "恢复时覆盖了有效词条备份。");
            Require(Directory.EnumerateFiles(fixture.UserData, "aliases.json.damaged.*").Any(), "未保留损坏词条原件。");
            File.WriteAllText(fixture.Paths.AliasesFile, "broken");
            File.WriteAllText(fixture.Paths.AliasesFile + ".bak", "also-broken");
            Throws<InvalidDataException>(() => store.Load());
        });

        Check("词条保存导出故障与受管文件保护", fixture =>
        {
            var store = new AliasStore(fixture.Paths);
            var aliases = new[] { new CustomAlias(1, "first", "") };
            store.Save(aliases);
            var original = File.ReadAllBytes(fixture.Paths.AliasesFile);
            using (File.Open(fixture.Paths.AliasesFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                Throws<IOException>(() => store.Save([new CustomAlias(2, "second", "")]));
            Require(File.ReadAllBytes(fixture.Paths.AliasesFile).SequenceEqual(original), "失败保存破坏了词条。");
            var file = Path.Combine(fixture.Root, "existing.json");
            File.WriteAllText(file, "keep-export");
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                Throws<IOException>(() => store.Export(file, aliases));
            Require(File.ReadAllText(file) == "keep-export", "失败导出破坏了目标文件。");
            new SettingsStore(fixture.Paths).Save(new AppSettings());
            Throws<InvalidDataException>(() => store.Export(fixture.Paths.SettingsFile, aliases));
            Throws<InvalidDataException>(() => store.Export(fixture.Paths.AliasesFile, aliases));
            Require(!Directory.EnumerateFiles(fixture.Root, "*.tmp", SearchOption.AllDirectories).Any(), "失败操作遗留临时文件。");
        });

        Check("便携版拒绝全部自启动写入", fixture =>
        {
            File.WriteAllText(Path.Combine(fixture.Base, "portable.flag"), "");
            var subKey = TestSubKey();
            var startup = new StartupService(AppPaths.Resolve(fixture.Base, fixture.UserData), Path.Combine(fixture.Base, "Winicode.exe"), subKey);
            Require(!startup.GetStatus().Supported, "便携版错误支持自启动。");
            Throws<InvalidOperationException>(() => startup.SetEnabled(true));
            Throws<InvalidOperationException>(() => startup.SetEnabled(false));
            startup.RemoveOwnedEntry();
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            Require(key is null, "便携版写入了注册表。");
        });

        Check("隔离注册表自启动所有权与引用", fixture =>
        {
            var subKey = TestSubKey();
            try
            {
                var executable = Path.Combine(fixture.Base, "folder with spaces", "Winicode.exe");
                var startup = new StartupService(fixture.Paths, executable, subKey);
                Require(!startup.GetStatus().Enabled && startup.GetStatus().Supported, "初始状态错误。");
                startup.SetEnabled(true);
                using (var key = Registry.CurrentUser.OpenSubKey(subKey))
                    Require((string?)key?.GetValue("Winicode") == $"\"{executable}\" --background", "自启动命令引用错误。");
                Require(startup.GetStatus().Enabled, "注册后未启用。");
                startup.SetEnabled(false);
                Require(!startup.GetStatus().Enabled, "关闭未删除条目。");
                startup.SetEnabled(true);
                startup.RemoveOwnedEntry();
                Require(!startup.GetStatus().Enabled, "清理未删除自有条目。");
                const string foreign = "\"C:\\Other Install\\Winicode.exe\" --background";
                using (var key = Registry.CurrentUser.CreateSubKey(subKey)) key.SetValue("Winicode", foreign);
                Require(!startup.GetStatus().Enabled, "其他安装条目被误判为自有。");
                Throws<InvalidOperationException>(() => startup.SetEnabled(true));
                Throws<InvalidOperationException>(() => startup.SetEnabled(false));
                startup.RemoveOwnedEntry();
                using (var key = Registry.CurrentUser.OpenSubKey(subKey))
                    Require((string?)key?.GetValue("Winicode") == foreign, "修改了其他安装的条目。");
                using (var key = Registry.CurrentUser.CreateSubKey(subKey)) key.SetValue("Winicode", $"\"{executable}.foreign\" --background");
                startup.RemoveOwnedEntry();
                using (var key = Registry.CurrentUser.OpenSubKey(subKey)) Require(key?.GetValue("Winicode") is not null, "前缀相似的路径被删除。");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); }
        });

        Check("诊断脱敏预览与导出一致", fixture =>
        {
            var service = new DiagnosticsService(fixture.Paths);
            const string secret = "SECRET-query-window-clipboard";
            service.LogException(new IOException(secret + fixture.Root, new Exception(secret)), secret);
            service.LogException(new SecretException(secret), "settings");
            var settings = new AppSettings { Theme = secret, ExcludedApplications = [secret + ".exe"] };
            var preview = service.Preview(settings, secret + fixture.Root);
            var report = service.Export(settings, secret + fixture.Root);
            Require(File.ReadAllText(report) == preview, "导出内容与预览不同。");
            foreach (var text in Directory.EnumerateFiles(fixture.UserData, "*", SearchOption.AllDirectories).Select(File.ReadAllText))
                Require(!text.Contains(secret) && !text.Contains(fixture.Root) && !text.Contains(nameof(SecretException)), "诊断含原始异常、路径、应用名或任意状态内容。");
            Require(preview.Contains("排除应用数量：1") && preview.Contains(BuildInfo.Version), "诊断缺少安全的版本或数量信息。");
        });

        Check("诊断日志轮转上限与不可写故障", fixture =>
        {
            Directory.CreateDirectory(fixture.Paths.LogsDirectory);
            var log = Path.Combine(fixture.Paths.LogsDirectory, "diagnostics.log");
            var service = new DiagnosticsService(fixture.Paths);
            for (var index = 0; index < 5; index++)
            {
                File.WriteAllBytes(log, new byte[256 * 1024]);
                service.LogException(new Exception("private"), "startup");
            }
            var logs = Directory.GetFiles(fixture.Paths.LogsDirectory);
            Require(logs.Length == 3 && logs.All(file => new FileInfo(file).Length <= 256 * 1024), "日志轮转未限制到三份且每份 256 KiB。");
            using (File.Open(log, FileMode.Open, FileAccess.Read, FileShare.None))
                service.LogException(new IOException("private"), "startup");
            Directory.Delete(fixture.Paths.LogsDirectory, recursive: true);
            File.WriteAllText(fixture.Paths.LogsDirectory, "blocker");
            service.LogException(new Exception("private"), "startup");
            Require(File.ReadAllText(fixture.Paths.LogsDirectory) == "blocker", "日志故障破坏了已有文件。");
        });

        Check("更新地址白名单无效输入不联网", fixture =>
        {
            using var handler = new StubHandler(_ => throw new InvalidOperationException("无效地址触发了网络请求。"));
            using var client = new HttpClient(handler);
            foreach (var repository in new[]
            {
                "", "https://github.com", "http://github.com/t8/Winicode", "https://github.com.evil.example/t8/Winicode",
                "https://user@github.com/t8/Winicode", "https://github.com:444/t8/Winicode", "https://github.com/t8/Winicode/issues",
                "https://github.com/t8/Winicode?x=1", "https://github.com/t8/Winicode#x", "https://github.com/t8/%57inicode",
                "https://github.com/t8/../Winicode", "https://api.github.com/t8/Winicode", "file:///C:/Winicode"
            }) Require(!new ReleaseChecker(repository, "0.1.0-beta.1", client).CheckAsync().GetAwaiter().GetResult().UpdateAvailable, "接受了无效仓库。");
            Require(!new ReleaseChecker("https://github.com/t8/Winicode", "invalid", client).CheckAsync().GetAwaiter().GetResult().UpdateAvailable, "接受了无效当前版本。");
            Require(handler.Requests == 0, "无效仓库或版本触发了网络请求。");
        });

        Check("手动更新 HTTPS 请求与语义版本", fixture =>
        {
            foreach (var example in new[]
            {
                (Current: "0.1.0-beta.2", Latest: "v0.1.0-beta.10", Update: true),
                (Current: "0.1.0-rc.1", Latest: "v0.1.0", Update: true),
                (Current: "1.9.0", Latest: "v1.10.0", Update: true),
                (Current: "1.0.0", Latest: "v1.0.0+build.2", Update: false),
                (Current: "2.0.0", Latest: "v1.0.0", Update: false),
                (Current: "1.0.0", Latest: "v2.0.0-beta.1", Update: false),
                (Current: "1.0.0", Latest: "v01.2.3", Update: false),
                (Current: "1.0.0", Latest: "v1.2.3-beta.01", Update: false)
            })
            {
                using var handler = new StubHandler(request =>
                {
                    Require(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri
                        == "https://api.github.com/repos/t8/Winicode/releases?per_page=20", "请求未限定到官方仓库元数据。");
                    Require(request.Headers.UserAgent.Any() && request.Headers.Authorization is null, "请求头错误。");
                    return JsonResponse(Releases(example.Latest));
                });
                using var client = new HttpClient(handler);
                var result = new ReleaseChecker("https://github.com/t8/Winicode/", example.Current, client).CheckAsync().GetAwaiter().GetResult();
                Require(result.UpdateAvailable == example.Update, $"版本比较错误：{example.Current} / {example.Latest}。");
                if (result.UpdateAvailable) Require(result.ReleaseUrl == ReleaseUrl(example.Latest) && result.Version == example.Latest, "发布链接或版本丢失。");
                Require(handler.Requests == 1, "检查请求数量错误。");
            }
        });

        Check("更新按版本选择且过滤草稿", fixture =>
        {
            var releases = JsonSerializer.Serialize(new[]
            {
                new { tag_name = "v1.2.0", html_url = ReleaseUrl("v1.2.0"), draft = false, prerelease = false },
                new { tag_name = "v1.10.0", html_url = ReleaseUrl("v1.10.0"), draft = false, prerelease = false },
                new { tag_name = "v9.0.0", html_url = ReleaseUrl("v9.0.0"), draft = true, prerelease = false }
            });
            var result = CheckRelease(releases, "1.0.0");
            Require(result.UpdateAvailable && result.Version == "v1.10.0", "版本选择依赖响应顺序或接受了草稿。");
        });

        Check("更新发布页拒绝跨仓库与重定向", fixture =>
        {
            foreach (var url in new[]
            {
                "https://evil.example/t8/Winicode/releases/tag/v2.0.0", "http://github.com/t8/Winicode/releases/tag/v2.0.0",
                "https://github.com/other/Winicode/releases/tag/v2.0.0", "https://github.com/t8/other/releases/tag/v2.0.0",
                "https://github.com/t8/Winicode/releases/tag/v3.0.0", "https://github.com/t8/Winicode/releases/tag/v2.0.0?download=1",
                "https://github.com/t8/Winicode/releases/tag/v2.0.0/extra", "https://user@github.com/t8/Winicode/releases/tag/v2.0.0"
            })
            {
                var result = CheckRelease(Releases("v2.0.0", url));
                Require(!result.UpdateAvailable && result.ReleaseUrl is null, "接受了不可信发布页。");
            }
            using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
                { Headers = { Location = new Uri("https://evil.example") } });
            using var client = new HttpClient(handler);
            Require(!new ReleaseChecker("https://github.com/t8/Winicode", "1.0.0", client).CheckAsync().GetAwaiter().GetResult().UpdateAvailable, "接受了重定向。");
            using var redirectedHandler = new StubHandler(_ =>
            {
                var response = JsonResponse(Releases("v2.0.0"));
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/releases");
                return response;
            });
            using var redirectedClient = new HttpClient(redirectedHandler);
            Require(!new ReleaseChecker("https://github.com/t8/Winicode", "1.0.0", redirectedClient).CheckAsync().GetAwaiter().GetResult().UpdateAvailable, "接受了地址不符的响应。");
        });

        Check("更新 HTTP 与响应故障", fixture =>
        {
            foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
            {
                using var handler = new StubHandler(_ => new HttpResponseMessage(status));
                using var client = new HttpClient(handler);
                var result = new ReleaseChecker("https://github.com/t8/Winicode", "1.0.0", client).CheckAsync().GetAwaiter().GetResult();
                Require(!result.UpdateAvailable && result.ReleaseUrl is null && result.Message.Length > 0, "HTTP 故障未给出安全结果。");
            }
            foreach (var content in new[] { "{broken", "{}", "null", "[]", "[null]", "[{\"tag_name\":\"v2.0.0\"}]", new string('x', 1024 * 1024 + 1) })
                Require(!CheckRelease(content).UpdateAvailable, "无效更新响应被接受。");
            using var streamHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new UnknownLengthContent(new byte[1024 * 1024 + 1]) });
            using var streamClient = new HttpClient(streamHandler);
            Require(!new ReleaseChecker("https://github.com/t8/Winicode", "1.0.0", streamClient).CheckAsync().GetAwaiter().GetResult().UpdateAvailable, "无长度响应未受大小限制。");
            using var faultHandler = new StubHandler(_ => throw new HttpRequestException("SECRET-private-proxy-path"));
            using var faultClient = new HttpClient(faultHandler);
            var fault = new ReleaseChecker("https://github.com/t8/Winicode", "1.0.0", faultClient).CheckAsync().GetAwaiter().GetResult();
            Require(!fault.UpdateAvailable && !fault.Message.Contains("SECRET"), "网络错误泄露了异常信息。");
        });

        Check("更新取消与超时", fixture =>
        {
            using var handler = new StubHandler(_ => throw new TaskCanceledException("private"));
            using var client = new HttpClient(handler);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var checker = new ReleaseChecker("https://github.com/t8/Winicode", "1.0.0", client);
            Require(checker.CheckAsync(cancelled.Token).GetAwaiter().GetResult().Message.Contains("取消") && handler.Requests == 0, "预取消仍发起了请求。");
            Require(checker.CheckAsync().GetAwaiter().GetResult().Message.Contains("超时"), "超时未给出提示。");
        });

        return failures.AsReadOnly();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("预期异常未发生：" + typeof(T).Name);
    }

    private static string TestSubKey() => @"Software\Winicode.ServiceTests\" + Guid.NewGuid().ToString("N");
    private static string ReleaseUrl(string tag) => "https://github.com/t8/Winicode/releases/tag/" + Uri.EscapeDataString(tag);
    private static string Releases(string tag, string? url = null) => JsonSerializer.Serialize(new[]
    {
        new { tag_name = tag, html_url = url ?? ReleaseUrl(tag), draft = false, prerelease = tag.Contains('-') }
    });
    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private static ReleaseCheckResult CheckRelease(string json, string version = "1.0.0")
    {
        using var handler = new StubHandler(_ => JsonResponse(json));
        using var client = new HttpClient(handler);
        return new ReleaseChecker("https://github.com/t8/Winicode", version, client).CheckAsync().GetAwaiter().GetResult();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private sealed class SecretException(string message) : Exception(message) { }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Winicode-ServiceTests-" + Guid.NewGuid().ToString("N"));
        internal string Base => Path.Combine(Root, "app");
        internal string UserData => Path.Combine(Root, "user");
        internal AppPaths Paths { get; }
        internal Fixture() { Directory.CreateDirectory(Base); Paths = AppPaths.Resolve(Base, UserData); }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
