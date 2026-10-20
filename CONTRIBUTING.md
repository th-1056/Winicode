# 参与贡献

感谢帮助修复问题、完善字符别名和测试 Windows 输入行为。请先阅读[使用说明](docs/user-guide.md)和[架构说明](docs/architecture.md)，在[项目 Issues](https://github.com/th-1056/Winicode/issues)描述问题或建议。报告中请删除个人输入、完整窗口标题、用户名和私人文件路径。

## 开发环境

使用 Windows x64、PowerShell 7 和 .NET 10 SDK；CI 固定 10.0.402。执行：

```powershell
pwsh -NoProfile -File .\scripts\test-packaging.ps1
pwsh -NoProfile -File .\scripts\build.ps1
```

构建脚本会校验官方 Unicode 数据，并执行依赖为空的测试控制台。新增逻辑请补充能复现实际问题的定向测试。改动输入、焦点或候选面板后，还需在有中文输入法的交互桌面中验证；托管 CI 无法完成全部交互验收。

打包使用 `scripts/package.ps1`，要求预先准备 Inno Setup 7.1.0 x64（本地接受 6.7+）。工具发现顺序及文件输出见 [README](README.md)。请等待并行源码编辑结束后再构建和打包。

## 提交范围

保持修改聚焦，说明触发条件、预期结果和验证方法。主题/字号改动请提供干净的局部界面截图，并避免暴露输入文本或其他应用内容。输入发送失败、结果不确定时的恢复路径也应有测试说明。

Unicode 官方文件应原样保存。`data/sources.json` 记录版本、来源、许可与哈希；调整版本时需一起复核，随后运行 `scripts/verify-unicode.ps1`。`scripts/download-unicode.ps1` 只刷新已经审核的固定来源，并在替换前验证下载文件。自定义中文数学别名应附带明确码位和含义。

发布源码前执行：

```powershell
pwsh -NoProfile -File .\scripts\export-source.ps1 -ListOnly
pwsh -NoProfile -File .\scripts\export-source.ps1
```

该脚本按 `installer/source-allowlist.json` 导出源码到新的 `artifacts/source-*` 目录，排除本机工具、用户配置、构建结果、内部计划和测试快照，并检查符号链接与常见凭据模式。请人工复核导出目录及 Git 暂存区，检查工具无法识别的敏感内容。新增公开文件时同步更新白名单。脚本不会提交或推送 Git。

贡献原始代码遵循项目 MIT 许可；第三方文件须保留来源和完整许可。请勿提交签名证书、密钥、浏览器资料、日志、原始整屏截图或个人测试数据。安全问题按 [SECURITY.md](SECURITY.md) 处理。
