using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Winicode;

internal sealed class InputController : IDisposable
{
    private readonly IAppController app;
    private readonly Func<string,int,IReadOnlyList<UnicodeEntry>> searchProvider;
    private readonly CandidateTracker tracker=new();
    private readonly DockWindow dock=new();
    private KeyboardTrigger? keyboard;
    private SearchWindow? search;
    private CandidateAnchor? latest;
    private string pendingQuery="";
    private bool opening,inserting,disposed;
    private readonly DispatcherTimer expiry=new(){Interval=TimeSpan.FromMilliseconds(100)};
    internal event Action<string>? Error;
    internal InputController(IAppController app,Func<string,int,IReadOnlyList<UnicodeEntry>> searchProvider)
    {
        this.app=app;this.searchProvider=searchProvider;
        dock.OpenRequested+=async()=>await Open(keyboard?.Snapshot());
        dock.ExitRequested+=app.ShowSettings;
        tracker.Changed+=anchor=>Application.Current.Dispatcher.BeginInvoke(()=>Update(anchor));
        expiry.Tick+=(_,_)=>{if(search==null&&!opening&&latest!=null&&!Fresh(latest)){latest=null;dock.Hide();}};
    }
    internal void Start()
    {
        keyboard=new KeyboardTrigger();keyboard.DockHandle=new WindowInteropHelper(dock).EnsureHandle();
        keyboard.InputObserved+=tracker.RequestScan;
        keyboard.Requested+=(query,anchor)=>Application.Current.Dispatcher.BeginInvoke(async()=>
        {
            if(disposed)return;
            latest=anchor;pendingQuery=query;
            if(!await Open(query))Error?.Invoke("未能打开字符选择器，输入目标可能已经改变。请通过 U+ 重试。");
        });
        tracker.Start();expiry.Start();ApplySettings();
    }
    internal void ApplySettings()
    {
        if(disposed)return;
        var settings=app.Settings;
        var active=!app.IsPaused;
        tracker.Configure(active,settings.ExcludedApplications);
        keyboard?.Configure(active,settings.EnableZeroKey);
        if(!active){search?.Close();latest=null;dock.Hide();}
        else if(!settings.ShowDockButton)dock.Hide();
        ThemeManager.Apply(dock,settings);
        if(search!=null)ThemeManager.Apply(search,settings);
    }
    private void Update(CandidateAnchor? anchor)
    {
        if(disposed)return;
        keyboard?.Observe(anchor);
        if(search!=null||opening)return;
        latest=anchor;
        if(anchor==null||!Fresh(anchor)||app.IsPaused||!app.Settings.ShowDockButton){dock.Hide();return;}
        dock.Place(anchor);if(!dock.IsVisible)dock.Show();
    }
    private static bool Fresh(CandidateAnchor anchor)=>Environment.TickCount64-anchor.ObservedAt<600&&Native.GetForegroundWindow()==anchor.TargetWindow;
    private async Task<bool> Open(string? initialQuery)
    {
        if(disposed||search!=null||opening||latest==null||!Fresh(latest)||app.IsPaused)return false;
        var target=latest.Target??InputTarget.Capture();
        if(target==null||target.Window!=latest.TargetWindow||!target.HasFocus())return false;
        opening=true;keyboard?.SetEnabled(false);
        var anchor=latest;
        var initial=app.Settings.CarryPreedit?(string.IsNullOrWhiteSpace(initialQuery)?pendingQuery:initialQuery):"";
        try
        {
            if(!target.HasFocus()||!Native.SendVirtualKey(0x1b))return false;
            await Task.Delay(65);
            if(disposed||!target.HasFocus())return false;
            dock.Hide();
            search=new SearchWindow(searchProvider,entry=>Insert(target,entry),()=>Cancel(target),initial??"",app.Settings);
            search.Closed+=(_,_)=>{search=null;keyboard?.Configure(!app.IsPaused,app.Settings.EnableZeroKey);};
            search.Place(anchor);ThemeManager.Apply(search,app.Settings);search.Show();search.Activate();
            pendingQuery="";return true;
        }
        finally{opening=false;if(search==null)keyboard?.Configure(!app.IsPaused,app.Settings.EnableZeroKey);}
    }
    private async void Insert(InputTarget target,UnicodeEntry entry)
    {
        if(search==null||inserting||disposed)return;
        var panel=search;
        if(!target.IsValid()){panel.ShowError("原输入窗口已关闭，请重新打开选择器。");return;}
        if(Native.GetForegroundWindow()!=new WindowInteropHelper(panel).Handle)return;
        inserting=true;panel.IsCommitting=true;panel.Hide();
        try
        {
            if(!target.Restore()){RestorePanel(panel,"无法返回原输入窗口。");return;}
            await Task.Delay(90);
            if(disposed)return;
            if(!target.HasFocus()){RestorePanel(panel,"输入目标发生变化。请关闭后重新打开选择器。");return;}
            var outcome=Native.SendUnicode(entry.Character);
            if(outcome.State==DeliveryState.Partial)
            {
                panel.SubmissionBlocked=true;
                RestorePanel(panel,"部分按键已发送，请检查原输入框。关闭后再操作，避免重复输入。");
                Error?.Invoke("字符发送未全部完成，请先检查输入结果。");return;
            }
            if(outcome.State==DeliveryState.None){RestorePanel(panel,"未能发送字符，请确认原窗口可编辑且权限相同。");return;}
            panel.Close();
        }
        finally{inserting=false;}
    }
    private static void RestorePanel(SearchWindow panel,string message)
    {panel.IsCommitting=false;panel.Show();panel.Activate();panel.ShowError(message);}
    private void Cancel(InputTarget target)
    {search?.Close();if(!disposed)target.Restore();}
    public void Dispose()
    {
        if(disposed)return;disposed=true;expiry.Stop();keyboard?.Dispose();tracker.Dispose();search?.Close();dock.Close();
    }
}
