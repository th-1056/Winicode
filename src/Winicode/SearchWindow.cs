using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Winicode;

internal sealed class SearchWindow : Window
{
    private const int PageSize=5;
    private const double PanelWidth=590;
    private const double PanelHeight=260;
    private const string Help="1–5 选字 · PgUp/PgDn 翻页 · Enter 确认 · Esc 关闭";
    private readonly Func<string,int,IReadOnlyList<UnicodeEntry>> searchProvider;
    private readonly AppSettings settings;
    private readonly double sizeScale;
    private readonly Action<UnicodeEntry> insert;
    private readonly Action cancel;
    private readonly TextBox query=new();
    private readonly ListBox results=new();
    private readonly TextBlock status=new();
    private readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private IReadOnlyList<UnicodeEntry> matches=Array.Empty<UnicodeEntry>();
    private Task refreshTask=Task.CompletedTask;
    private int queryVersion;
    private int refreshingVersion=-1;
    private int renderedVersion=-1;
    private int pageIndex;
    private bool closed;
    private string renderedQuery="";
    internal bool IsCommitting {get;set;}
    internal bool SubmissionBlocked {get;set;}
    internal SearchWindow(Func<string,int,IReadOnlyList<UnicodeEntry>> searchProvider,Action<UnicodeEntry> insert,Action cancel,string initialQuery,AppSettings settings)
    {
        this.searchProvider=searchProvider;this.insert=insert;this.cancel=cancel;this.settings=settings;
        sizeScale=settings.FontPreset=="small"?0.9:settings.FontPreset=="large"?1.15:1;
        Title="Winicode 字符搜索";Width=PanelWidth*sizeScale;Height=PanelHeight*sizeScale;MinWidth=320;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;
        SetResourceReference(BackgroundProperty,ThemeManager.BackgroundKey);
        FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
        var grid=new Grid{Margin=new Thickness(8)};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel {LastChildFill=true,Margin=new Thickness(0,0,0,6)};
        var close=new Button{Content="×",Width=28,Height=28,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0),ToolTip="关闭 (Esc)"};close.Click+=(_,_)=>cancel();DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var label=new TextBlock{Text="U+",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,8,0),FontSize=15};label.SetResourceReference(TextBlock.ForegroundProperty,ThemeManager.AccentKey);header.Children.Add(label);
        query.FontSize=15;query.Height=32;query.VerticalContentAlignment=VerticalAlignment.Center;query.Padding=new Thickness(6,3,6,3);query.Text=initialQuery;
        query.ToolTip="输入 U+03B1、alpha、阿尔法或字符";
        InputMethod.SetIsInputMethodEnabled(query,false);
        AutomationProperties.SetAutomationId(query,"UnicodeQuery");AutomationProperties.SetName(query,"码位、名称或中文别名");
        header.Children.Add(query);grid.Children.Add(header);
        results.BorderThickness=new Thickness(0);results.SetResourceReference(Control.BackgroundProperty,ThemeManager.BackgroundKey);results.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(results,"UnicodeResults");
        ScrollViewer.SetHorizontalScrollBarVisibility(results,ScrollBarVisibility.Disabled);
        Grid.SetRow(results,1);grid.Children.Add(results);
        status.Text=Help;status.FontSize=11;status.Foreground=Brushes.DimGray;status.Margin=new Thickness(4,6,4,0);status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;
        status.SetResourceReference(TextBlock.ForegroundProperty,ThemeManager.MutedKey);
        status.ToolTip="↑↓ 切换候选；码位用 U+03B1 / 0x03B1 / 03B1，数字照常输入。";
        Grid.SetRow(status,2);grid.Children.Add(status);
        Content=new Border{BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(215,215,215)),CornerRadius=new CornerRadius(8),Child=grid,LayoutTransform=new ScaleTransform(sizeScale,sizeScale)};
        ((Border)Content).SetResourceReference(Border.BorderBrushProperty,ThemeManager.BorderKey);
        query.TextChanged+=(_,_)=>
        {
            queryVersion++;renderedVersion=-1;pageIndex=0;matches=Array.Empty<UnicodeEntry>();results.Items.Clear();
            status.Text="搜索中 · "+Help;debounce.Stop();debounce.Start();
        };
        debounce.Tick+=(_,_)=>{_ = Refresh();};
        PreviewKeyDown+=HandleKey;
        Loaded+=(_,_)=>{_ = Refresh();query.Focus();query.SelectAll();};
        Closing+=(_,_)=>{closed=true;queryVersion++;debounce.Stop();};
        Deactivated+=(_,_)=>
        {
            if(closed||IsCommitting)return;
            Dispatcher.BeginInvoke(()=>{if(!closed&&!IsCommitting&&!IsActive)Close();});
        };
    }
    private bool HasCurrentResults=>!closed&&renderedVersion==queryVersion&&renderedQuery==query.Text;
    private bool CanChoose=>HasCurrentResults&&IsActive&&!IsCommitting&&!SubmissionBlocked;
    private bool IsCodeQuery()
    {
        var text=query.Text.TrimStart();
        return text.StartsWith("U+",StringComparison.OrdinalIgnoreCase)||text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)
            ||(text.Length>0&&char.IsAsciiDigit(text[0]));
    }
    private async void HandleKey(object sender,KeyEventArgs e)
    {
        // Keep IME candidate keys and application shortcuts on their normal input path.
        if(e.Handled||e.Key==Key.ImeProcessed||(Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Alt|ModifierKeys.Windows))!=0
            ||closed||!IsActive||IsCommitting)return;
        if(e.Key==Key.Escape){e.Handled=true;cancel();return;}
        if(!query.IsKeyboardFocusWithin&&!results.IsKeyboardFocusWithin)return;
        int number=e.Key>=Key.D1&&e.Key<=Key.D5?(int)e.Key-(int)Key.D1:
            e.Key>=Key.NumPad1&&e.Key<=Key.NumPad5?(int)e.Key-(int)Key.NumPad1:-1;
        if(settings.NumericSelection&&number>=0&&Keyboard.Modifiers==ModifierKeys.None&&query.Text.Trim().Length>0&&!IsCodeQuery())
        {
            e.Handled=true;await Choose(number);return;
        }
        if(e.Key==Key.Enter){e.Handled=true;await Choose();return;}
        if(e.Key!=Key.PageDown&&e.Key!=Key.PageUp&&e.Key!=Key.Down&&e.Key!=Key.Up)return;
        e.Handled=true;
        int version=queryVersion;
        await Refresh();
        if(!CanChoose||version!=queryVersion)return;
        if(e.Key==Key.PageDown||e.Key==Key.PageUp)
        {
            int nextPage=Math.Clamp(pageIndex+(e.Key==Key.PageDown?1:-1),0,Math.Max(0,(matches.Count-1)/PageSize));
            if(nextPage!=pageIndex){pageIndex=nextPage;RenderPage();}
        }
        else if(results.Items.Count>0)
            results.SelectedIndex=Math.Clamp(results.SelectedIndex+(e.Key==Key.Down?1:-1),0,results.Items.Count-1);
        if(results.SelectedItem!=null)results.ScrollIntoView(results.SelectedItem);
    }
    private Task Refresh()
    {
        debounce.Stop();
        if(closed||HasCurrentResults)return Task.CompletedTask;
        if(refreshingVersion==queryVersion)return refreshTask;
        refreshingVersion=queryVersion;
        return refreshTask=SearchAsync(query.Text,queryVersion);
    }
    private async Task SearchAsync(string text,int version)
    {
        try
        {
            var found=await Task.Run(()=>searchProvider(text,180));
            if(closed||version!=queryVersion||text!=query.Text)return;
            matches=found;renderedQuery=text;renderedVersion=version;pageIndex=0;RenderPage();
        }
        catch(Exception e){if(!closed&&version==queryVersion){results.Items.Clear();renderedVersion=-1;ShowError(e.Message);}}
        finally{if(refreshingVersion==version)refreshingVersion=-1;}
    }
    private void RenderPage()
    {
        results.Items.Clear();
        int start=pageIndex*PageSize;
        for(int index=start;index<Math.Min(start+PageSize,matches.Count);index++)
        {
            var entry=matches[index];int number=index-start+1;
            var row=new Grid{Margin=new Thickness(4,2,4,2)};
            row.ColumnDefinitions.Add(new(){Width=new GridLength(20)});row.ColumnDefinitions.Add(new(){Width=new GridLength(42)});row.ColumnDefinitions.Add(new(){Width=new GridLength(84)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            row.Children.Add(new TextBlock{Text=number.ToString(),FontSize=12,Foreground=Brushes.DimGray,VerticalAlignment=VerticalAlignment.Center});
            var glyph=new TextBlock{Text=entry.DisplayCharacter,FontSize=24,VerticalAlignment=VerticalAlignment.Center,FontFamily=new FontFamily("Cambria Math, Segoe UI Symbol, Segoe UI Emoji, Microsoft YaHei UI"),TextTrimming=TextTrimming.CharacterEllipsis};Grid.SetColumn(glyph,1);row.Children.Add(glyph);
            var code=new TextBlock{Text=entry.CodeLabel,FontSize=12,Foreground=Brushes.DimGray,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(code,2);row.Children.Add(code);
            var detail=new StackPanel{VerticalAlignment=VerticalAlignment.Center};detail.Children.Add(new TextBlock{Text=entry.Name,FontSize=12,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis});
            if(entry.Description.Length>0)detail.Children.Add(new TextBlock{Text=entry.Description,FontSize=11,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis});Grid.SetColumn(detail,3);row.Children.Add(detail);
            var item=new ListBoxItem{Content=row,Tag=entry,Height=36,Padding=new Thickness(0),BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=entry.CodeLabel+"  "+entry.Name+"\n"+entry.Description};
            foreach(var child in row.Children.OfType<TextBlock>().Where(x=>x.Foreground==Brushes.DimGray))child.SetResourceReference(TextBlock.ForegroundProperty,ThemeManager.MutedKey);
            foreach(var child in detail.Children.OfType<TextBlock>().Where(x=>x.Foreground==Brushes.DimGray))child.SetResourceReference(TextBlock.ForegroundProperty,ThemeManager.MutedKey);
            AutomationProperties.SetName(item,number+" "+entry.CodeLabel+" "+entry.Name);
            item.PreviewMouseLeftButtonUp+=(_,e)=>{if(e.ClickCount==1){ChooseItem(item);e.Handled=true;}};
            results.Items.Add(item);
        }
        if(results.Items.Count>0){results.SelectedIndex=0;results.ScrollIntoView(results.SelectedItem);}
        status.Text=(matches.Count==0?"无匹配":$"{pageIndex+1}/{(matches.Count+PageSize-1)/PageSize} 页")+" · "+Help;
    }
    private async Task Choose(int index=-1)
    {
        int version=queryVersion;
        await Refresh();
        if(!CanChoose||version!=queryVersion)return;
        if(index>=0)
        {
            if(index>=results.Items.Count)return;
            results.SelectedIndex=index;
        }
        if(results.SelectedItem is ListBoxItem item)ChooseItem(item);
    }
    private void ChooseItem(ListBoxItem item)
    {
        // Detached rows and list whitespace must never reuse an earlier selection.
        if(CanChoose&&results.Items.Contains(item)&&item.Tag is UnicodeEntry entry)insert(entry);
    }
    internal void ShowError(string message){status.Text=message;status.ToolTip=message;}
    internal void Place(CandidateAnchor anchor)
    {
        var scale=Native.GetDpiForWindow(anchor.Window)/96.0;if(scale<=0)scale=1;
        var r=anchor.Bounds;var work=Native.WorkArea(r);
        Width=Math.Min(PanelWidth*sizeScale,work.Width/scale-16);Height=Math.Min(PanelHeight*sizeScale,work.Height/scale-16);
        var x=Math.Clamp(r.Left,work.Left,Math.Max(work.Left,work.Right-Width*scale));
        var y=r.Bottom+4*scale;if(y+Height*scale>work.Bottom)y=Math.Max(work.Top,r.Top-Height*scale-4*scale);
        Native.SetWindowPos(new WindowInteropHelper(this).EnsureHandle(),new nint(-1),(int)x,(int)y,(int)(Width*scale),(int)(Height*scale),0x10);
    }
}
