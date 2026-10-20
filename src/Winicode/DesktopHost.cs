using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Winicode;

internal sealed class DesktopHost : IAppController,IDisposable
{
    private readonly SettingsStore settingsStore;
    private readonly StartupService startup;
    private readonly AliasStore aliasStore;
    private readonly DiagnosticsService diagnostics;
    private readonly ReleaseChecker releases;
    private readonly UnicodeCatalog catalog;
    private readonly SemaphoreSlim saveGate=new(1,1);
    private CustomAlias[] aliases=[];
    private SettingsWindow? settingsWindow;
    private FirstRunWindow? firstRun;
    private Window? practice;
    private Tray? tray;
    private InputController? input;
    private bool exiting;
    private string? aliasWarning;
    public bool AliasesNeedRecovery {get;private set;}
    public AppSettings Settings {get;private set;}
    public AppPaths Paths {get;}
    public bool IsPaused {get;private set;}
    public string StatusText {get;private set;}="正在启动";
    public event EventHandler? StateChanged;

    internal DesktopHost(AppPaths paths)
    {
        Paths=paths;settingsStore=new(paths);Settings=settingsStore.Load();
        startup=new(paths);aliasStore=new(paths);diagnostics=new(paths);
        releases=new(BuildInfo.RepositoryUrl,BuildInfo.Version);
        catalog=UnicodeCatalog.Load(paths.DataDirectory);
        try{aliases=aliasStore.Load().ToArray();aliasWarning=aliasStore.LastWarning;}
        catch(Exception e){diagnostics.LogException(e,"load-aliases");AliasesNeedRecovery=true;aliasWarning="自定义词条与备份无法读取，已保留原文件。请在词条页导入有效备份恢复。";}
    }
    internal void Start(bool background)
    {
        tray=new Tray(this);
        input=new InputController(this,Search);
        input.Error+=message=>Report(message,true);
        try{input.Start();Report("运行中");}
        catch(Exception e){diagnostics.LogException(e,"start-input");IsPaused=true;Report("输入增强暂不可用，请在设置中查看诊断。",true);}
        if(!background)
        {
            if(!Settings.FirstRunCompleted){firstRun=new FirstRunWindow(this);firstRun.Closed+=(_,_)=>firstRun=null;firstRun.Show();}
            else ShowSettings();
        }
        if(settingsStore.LastWarning!=null)Report("设置已从备份恢复，或使用了默认值。请检查设置。",true);
        if(aliasWarning!=null)Report(aliasWarning,true);
    }
    private IReadOnlyList<UnicodeEntry> Search(string query,int limit)
    {
        var current=Volatile.Read(ref aliases);
        var selected=new List<UnicodeEntry>();
        if(!string.IsNullOrWhiteSpace(query))
        {
            foreach(var alias in current.Where(x=>x.Keyword.Contains(query.Trim(),StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.Keyword.Equals(query.Trim(),StringComparison.OrdinalIgnoreCase)))
            {
                var item=catalog.Lookup(alias.CodePoint) with {Description=alias.Description};
                if(selected.All(x=>x.CodePoint!=item.CodePoint))selected.Add(item);
            }
        }
        foreach(var item in catalog.Search(query,limit))
            if(selected.All(x=>x.CodePoint!=item.CodePoint))selected.Add(item);
        return selected.Take(limit).ToArray();
    }
    public StartupStatus GetStartupStatus()=>startup.GetStatus();
    public IReadOnlyList<CustomAlias> GetAliases()=>Volatile.Read(ref aliases).ToArray();
    public async Task ApplySettingsAsync(AppSettings settings)
    {
        if(exiting)throw new InvalidOperationException("程序正在退出，请稍后重新打开。");
        await saveGate.WaitAsync();
        try
        {
            if(exiting)throw new InvalidOperationException("程序正在退出。");
            var normalized=settings.Normalize();
            await Task.Run(()=>settingsStore.Save(normalized));
            Settings=normalized;
            input?.ApplySettings();
            foreach(Window window in Application.Current.Windows)ThemeManager.Apply(window,Settings);
            StateChanged?.Invoke(this,EventArgs.Empty);
        }
        finally{saveGate.Release();}
    }
    public Task SetStartupEnabledAsync(bool enabled)
    {
        startup.SetEnabled(enabled);StateChanged?.Invoke(this,EventArgs.Empty);return Task.CompletedTask;
    }
    public async Task SaveAliasesAsync(IReadOnlyList<CustomAlias> updated)
    {
        if(AliasesNeedRecovery)throw new InvalidOperationException("原词条文件损坏。请使用“导入”恢复有效备份，防止覆盖未恢复的内容。");
        await PersistAliases(updated);
    }
    public async Task RecoverAliasesAsync(IReadOnlyList<CustomAlias> updated)
    {
        await PersistAliases(updated);AliasesNeedRecovery=false;aliasWarning=null;Report("自定义词条已恢复。");
    }
    private async Task PersistAliases(IReadOnlyList<CustomAlias> updated)
    {
        if(exiting)throw new InvalidOperationException("程序正在退出。");
        await saveGate.WaitAsync();
        try
        {
            if(exiting)throw new InvalidOperationException("程序正在退出。");
            await Task.Run(()=>aliasStore.Save(updated));
            Volatile.Write(ref aliases,updated.ToArray());StateChanged?.Invoke(this,EventArgs.Empty);
        }
        finally{saveGate.Release();}
    }
    public Task<ReleaseCheckResult> CheckUpdatesAsync(CancellationToken cancellationToken=default)=>releases.CheckAsync(cancellationToken);
    public string BuildDiagnosticPreview()=>diagnostics.Preview(Settings,StatusText);
    public Task<string> ExportDiagnosticsAsync()=>Task.Run(()=>diagnostics.Export(Settings,StatusText));
    public Task TogglePauseAsync()
    {
        IsPaused=!IsPaused;input?.ApplySettings();Report(IsPaused?"输入增强已暂停":"运行中");return Task.CompletedTask;
    }
    public void ShowSettings()
    {
        if(exiting)return;
        if(settingsWindow==null){settingsWindow=new SettingsWindow(this);settingsWindow.Closed+=(_,_)=>settingsWindow=null;}
        settingsWindow.Show();if(settingsWindow.WindowState==WindowState.Minimized)settingsWindow.WindowState=WindowState.Normal;
        settingsWindow.Activate();
    }
    public void ShowInputTest()
    {
        if(practice!=null){practice.Activate();return;}
        var text=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=22,Margin=new Thickness(12),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        InputMethod.SetPreferredImeState(text,InputMethodState.On);
        System.Windows.Automation.AutomationProperties.SetAutomationId(text,"PracticeText");
        var label=new TextBlock{Text="在下方输入 ni，等 U+ 出现后按 0。此处内容只保留在内存中。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)};
        var panel=new DockPanel();DockPanel.SetDock(label,Dock.Top);panel.Children.Add(label);panel.Children.Add(text);
        practice=new Window{Title="Winicode 输入练习",Width=760,Height=410,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        ThemeManager.Apply(practice,Settings);
        var handle=new WindowInteropHelper(practice).EnsureHandle();Native.RegisterPracticeWindow(handle);
        practice.Closed+=(_,_)=>{Native.UnregisterPracticeWindow(handle);practice=null;};
        practice.Loaded+=(_,_)=>text.Focus();practice.Show();practice.Activate();
    }
    internal void Report(string message,bool notify=false)
    {
        StatusText=message;StateChanged?.Invoke(this,EventArgs.Empty);
        if(notify)tray?.Notify(message);
    }
    internal Task HandleCommandAsync(string command)
    {
        return Application.Current.Dispatcher.InvokeAsync(async()=>
        {
            if(command=="settings")ShowSettings();
            else if(command=="pause"&&!IsPaused)await TogglePauseAsync();
            else if(command=="resume"&&IsPaused)await TogglePauseAsync();
            else if(command=="exit")_ = Task.Delay(200).ContinueWith(_=>Application.Current.Dispatcher.BeginInvoke(Exit));
        }).Task.Unwrap();
    }
    public void Exit()
    {
        if(exiting)return;exiting=true;
        _=ExitAfterSaves();
    }
    private async Task ExitAfterSaves()
    {
        input?.Dispose();input=null;
        await saveGate.WaitAsync();
        try{Dispose();Application.Current.Shutdown();}
        finally{saveGate.Release();}
    }
    public void Dispose()
    {
        input?.Dispose();input=null;tray?.Dispose();tray=null;
        settingsWindow?.Close();settingsWindow=null;firstRun?.Close();firstRun=null;practice?.Close();practice=null;
    }
}
