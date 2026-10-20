namespace Winicode;

public interface IAppController
{
    AppSettings Settings { get; }
    AppPaths Paths { get; }
    bool IsPaused { get; }
    string StatusText { get; }
    event EventHandler? StateChanged;
    StartupStatus GetStartupStatus();
    IReadOnlyList<CustomAlias> GetAliases();
    bool AliasesNeedRecovery {get;}
    Task ApplySettingsAsync(AppSettings settings);
    Task SetStartupEnabledAsync(bool enabled);
    Task SaveAliasesAsync(IReadOnlyList<CustomAlias> aliases);
    Task RecoverAliasesAsync(IReadOnlyList<CustomAlias> aliases);
    Task<ReleaseCheckResult> CheckUpdatesAsync(CancellationToken cancellationToken=default);
    string BuildDiagnosticPreview();
    Task<string> ExportDiagnosticsAsync();
    Task TogglePauseAsync();
    void ShowSettings();
    void ShowInputTest();
    void Exit();
}

public static class BuildInfo
{
    public const string Name="Winicode";
    public const string Version="0.1.0-beta.1";
    public const string RepositoryUrl="https://github.com/th-1056/Winicode";
    public const string Copyright="Copyright (c) 2026 Winicode contributors";
}
