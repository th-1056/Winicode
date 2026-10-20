# Winicode

**Winicode 让你能在微软拼音输入法方便地查找并输入任意unicode字符。**

只要点击输入框后的U+，或输入内容后按下0，就可以打开检索面板查找并选择你想要任何Unicode字符。支持英文名称、码位和常用中文数学别名。

## 获取与运行
[项目 Releases](https://github.com/th-1056/Winicode/releases）
同时提供安装版本和便携版本，两种包均附带 .NET 运行库。

安装后打开 Winicode，按首次引导进入输入测试。使用微软拼音输入时，点击候选栏右侧的 U+，或按 0 打开检索；输入名称或 `U+2200` 等码位后选择字符。
关闭主界面后应用将最小化到托盘，托盘可以打开设置、暂停和退出。

随电脑启动和桌面快捷方式默认关闭。便携版配置存放在自身目录的 `userdata/` 中，并关闭自启动入口。安装版配置位于 `%LOCALAPPDATA%\Winicode`，升级保留设置和自定义词条；卸载默认保留个人数据。

## 从源码构建

使用 Windows x64、PowerShell 7 和稳定版 .NET 10 SDK。CI 固定 SDK **10.0.402**。脚本优先查找 `.tools/dotnet/dotnet.exe`，随后查找当前进程 PATH 中的 `dotnet.exe`。应用和测试控制台无需额外 NuGet 包；首次 restore/publish 可能需要联网获取框架包。

```powershell
pwsh -NoProfile -File .\scripts\test-packaging.ps1
pwsh -NoProfile -File .\scripts\build.ps1
```

`build.ps1` 校验 Unicode 哈希、构建应用及测试控制台，并执行测试控制台。项目的测试入口使用 `dotnet run`，执行失败会中止脚本。

打包还需要 Inno Setup：首选 **7.1.0 x64**，本地兼容 **6.7+**。可运行 `scripts/bootstrap-inno.ps1`，校验固定 SHA-256 和签名后，使用官方便携模式准备编译器。构建脚本支持 `WINICODE_DOTNET` / `WINICODE_ISCC` 显式工具路径、项目内工具或当前进程 PATH；不会修改机器 PATH。

```powershell
pwsh -NoProfile -File .\scripts\bootstrap-inno.ps1
pwsh -NoProfile -File .\scripts\package.ps1
pwsh -NoProfile -File .\scripts\verify-release.ps1
```

打包脚本执行构建和控制台测试，然后发布 `win-x64` 自包含目录，显式关闭 trimming、单文件发布和 AOT，生成独立安装版与便携版标记、文件清单、EXE、ZIP 和 `SHA256SUMS.txt`。最终文件位于 `dist/`，暂存目录保留在 `artifacts/` 供核对。存在同名产物时脚本会停止，请先将旧产物移到独立位置。打包前应结束并行源码编辑。

发布包保留原始 Unicode 许可及 .NET 许可。请使用含 `LICENSE.txt`、`ThirdPartyNotices.txt` 的官方 SDK 目录。工具的确切版本、提交、数据版本和未签名状态记录在包内 `release-manifest.json`。

## 文档与参与

- [使用说明](docs/user-guide.md)与[故障排查](docs/troubleshooting.md)
- [隐私说明](docs/privacy.md)与[安全问题报告](SECURITY.md)
- [架构与维护](docs/architecture.md)、[贡献指南](CONTRIBUTING.md)和[发布检查表](docs/release-checklist.md)
- [更新日志](CHANGELOG.md)与[第三方声明](THIRD-PARTY-NOTICES.md)
- [本版验证记录与已知限制](docs/validation-0.1.0-beta.1.md)

应用源码采用 [MIT 许可证](LICENSE)，版权署名为 Winicode contributors。Unicode 数据与分发运行时遵循各自许可。
