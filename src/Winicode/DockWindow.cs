using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Winicode;

internal sealed class DockWindow : Window
{
    internal event Action? OpenRequested;
    internal event Action? ExitRequested;
    internal DockWindow()
    {
        Title="Winicode U+"; Width=42;Height=34;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowActivated=false;ShowInTaskbar=false;Topmost=true;
        Background=new SolidColorBrush(Color.FromRgb(248,248,248));
        var button=new Button {Content="U+",FontFamily=new FontFamily("Segoe UI"),FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(0,85,165)),Background=Background,BorderThickness=new Thickness(0),ToolTip="搜索 Unicode 字符 · 右键退出"};
        SetResourceReference(BackgroundProperty,ThemeManager.BackgroundKey);
        button.SetResourceReference(Control.BackgroundProperty,ThemeManager.BackgroundKey);
        button.SetResourceReference(Control.ForegroundProperty,ThemeManager.AccentKey);
        AutomationProperties.SetAutomationId(button,"WinicodeButton");
        AutomationProperties.SetName(button,"搜索 Unicode 字符");
        button.Click+=(_,_)=>OpenRequested?.Invoke();
        var menu=new ContextMenu();var exit=new MenuItem {Header="设置 Winicode"};exit.Click+=(_,_)=>ExitRequested?.Invoke();menu.Items.Add(exit);button.ContextMenu=menu;
        Content=new Border {BorderBrush=new SolidColorBrush(Color.FromRgb(220,220,220)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Child=button};
        ((Border)Content).SetResourceReference(Border.BorderBrushProperty,ThemeManager.BorderKey);
        SourceInitialized+=(_,_)=>Native.MakeNoActivate(this);
    }
    internal void Place(CandidateAnchor anchor)
    {
        var scale=Native.GetDpiForWindow(anchor.Window)/96.0;if(scale<=0)scale=1;
        var r=anchor.Bounds;var work=Native.WorkArea(r);
        var width=42*scale; var height=Math.Clamp(r.Height,28*scale,36*scale);
        var x=r.Right+3*scale; var y=anchor.ButtonBounds.IsEmpty?r.Top:anchor.ButtonBounds.Top+(anchor.ButtonBounds.Height-height)/2;
        if(x+width>work.Right) {x=Math.Max(work.Left,r.Right-width);y=r.Bottom+3*scale;}
        y=Math.Clamp(y,work.Top,Math.Max(work.Top,work.Bottom-height));
        Native.SetWindowPos(new WindowInteropHelper(this).EnsureHandle(),new nint(-1),(int)x,(int)y,(int)width,(int)height,0x10);
    }
}
