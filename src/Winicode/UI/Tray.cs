using System.Drawing;
using System.Windows;
using System.Windows.Threading;

namespace Winicode;

public sealed class Tray:IDisposable
{
    private readonly IAppController controller;
    private readonly Dispatcher dispatcher;
    private readonly System.Windows.Forms.NotifyIcon icon;
    private readonly System.Windows.Forms.ContextMenuStrip menu;
    private readonly System.Windows.Forms.ToolStripMenuItem pause;
    private readonly System.Windows.Forms.ToolStripMenuItem feedback;
    private readonly Icon ownedIcon;
    private HelpWindow? help;
    private bool busy;
    private bool disposed;

    public Tray(IAppController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);this.controller=controller;
        dispatcher=Application.Current?.Dispatcher??Dispatcher.CurrentDispatcher;
        dispatcher.VerifyAccess();
        menu=new System.Windows.Forms.ContextMenuStrip{Name="WinicodeTrayMenu",AccessibleName="Winicode"};
        menu.Items.Add(Item(UiText.SettingsTitle,"TraySettings",()=>controller.ShowSettings()));
        pause=Item(UiText.Pause,"TrayTogglePause",async()=>await controller.TogglePauseAsync());
        menu.Items.Add(pause);
        menu.Items.Add(Item(UiText.InputTest,"TrayInputTest",()=>controller.ShowInputTest()));
        menu.Items.Add(Item(UiText.Help,"TrayHelp",ShowHelp));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        feedback=new System.Windows.Forms.ToolStripMenuItem
        {
            Name="TrayFeedback",AccessibleName=UiText.TrayNotificationTitle,Enabled=false,Visible=false
        };
        menu.Items.Add(feedback);
        menu.Items.Add(Item(UiText.TrayExit,"TrayExit",()=>{controller.Exit();Dispose();}));
        ownedIcon=LoadIcon(controller.Paths.BaseDirectory);
        icon=new System.Windows.Forms.NotifyIcon
        {
            Icon=ownedIcon,Text=BuildInfo.Name,ContextMenuStrip=menu,
            BalloonTipTitle=UiText.TrayNotificationTitle
        };
        try
        {
            icon.DoubleClick+=DoubleClick;
            menu.Opening+=MenuOpening;
            controller.StateChanged+=ControllerChanged;
            RefreshState();icon.Visible=true;
        }
        catch
        {
            Dispose();throw;
        }
    }

    // The caller supplies a status-only message; notifications never activate a window.
    public void Notify(string message)
    {
        if(disposed||string.IsNullOrWhiteSpace(message)||dispatcher.HasShutdownStarted)return;
        if(!dispatcher.CheckAccess())
        {
            _=dispatcher.InvokeAsync(()=>Notify(message));return;
        }
        string text=message.Length>240?message[..240]:message;
        feedback.Text=text;feedback.AccessibleName=text;feedback.Visible=true;feedback.ToolTipText=text;
        icon.BalloonTipText=text;icon.BalloonTipIcon=System.Windows.Forms.ToolTipIcon.Warning;
        icon.ShowBalloonTip(8000);
    }

    private System.Windows.Forms.ToolStripMenuItem Item(string title,string id,Action action)=>Item(title,id,()=>
    {
        action();return Task.CompletedTask;
    });

    private System.Windows.Forms.ToolStripMenuItem Item(string title,string id,Func<Task> action)
    {
        var item=new System.Windows.Forms.ToolStripMenuItem(title){Name=id,AccessibleName=title};
        item.Click+=async(_,_)=>await PerformAsync(action);return item;
    }

    private async Task PerformAsync(Func<Task> action)
    {
        if(disposed||busy)return;
        busy=true;pause.Enabled=false;
        try{await action();}
        catch(Exception error){if(!disposed)Notify(UiText.Error(error));}
        finally
        {
            busy=false;
            if(!disposed){pause.Enabled=true;RefreshState();}
        }
    }

    private async void DoubleClick(object? sender,EventArgs args)=>await PerformAsync(()=>
    {
        controller.ShowSettings();return Task.CompletedTask;
    });

    private void MenuOpening(object? sender,System.ComponentModel.CancelEventArgs args)
    {
        if(disposed){args.Cancel=true;return;}
        RefreshState();
    }

    private void ControllerChanged(object? sender,EventArgs args)
    {
        if(disposed||dispatcher.HasShutdownStarted)return;
        if(dispatcher.CheckAccess())RefreshState();
        else _=dispatcher.InvokeAsync(()=>{if(!disposed)RefreshState();});
    }

    private void RefreshState()
    {
        if(disposed)return;
        pause.Text=controller.IsPaused?UiText.Resume:UiText.Pause;pause.AccessibleName=pause.Text;
        icon.Text=controller.IsPaused?BuildInfo.Name+" · "+UiText.Paused:BuildInfo.Name;
    }

    private void ShowHelp()
    {
        if(help==null){help=new HelpWindow(controller);help.Closed+=(_,_)=>help=null;}
        help.Show();help.Activate();
    }

    private static Icon LoadIcon(string baseDirectory)
    {
        try
        {
            using var stream=File.OpenRead(Path.Combine(baseDirectory,"Assets","Winicode.ico"));
            using var source=new Icon(stream);
            return (Icon)source.Clone();
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    public void Dispose()
    {
        if(disposed)return;
        if(!dispatcher.CheckAccess()&&!dispatcher.HasShutdownStarted){dispatcher.Invoke(Dispose);return;}
        disposed=true;controller.StateChanged-=ControllerChanged;
        icon.DoubleClick-=DoubleClick;menu.Opening-=MenuOpening;
        icon.Visible=false;icon.ContextMenuStrip=null;icon.Dispose();
        menu.Dispose();ownedIcon.Dispose();
        help?.Close();help=null;
        GC.SuppressFinalize(this);
    }
}
