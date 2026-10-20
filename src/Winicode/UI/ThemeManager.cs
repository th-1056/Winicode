using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace Winicode;

public static class ThemeManager
{
    public const string BackgroundKey="Winicode.Background";
    public const string SurfaceKey="Winicode.Surface";
    public const string ForegroundKey="Winicode.Foreground";
    public const string MutedKey="Winicode.Muted";
    public const string BorderKey="Winicode.Border";
    public const string AccentKey="Winicode.Accent";
    public const string ErrorKey="Winicode.Error";
    public const string SelectionKey="Winicode.Selection";
    private static readonly ConditionalWeakTable<Window,ThemeSubscription> subscriptions=new();

    public static double FontScale(AppSettings settings)=>settings.FontPreset switch
    {
        "small"=>0.9,"large"=>1.15,_=>1.0
    };

    public static double FontSize(AppSettings settings,double defaultSize=14)=>defaultSize*FontScale(settings);

    public static void Apply(Window window,AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(window);ArgumentNullException.ThrowIfNull(settings);
        window.Dispatcher.VerifyAccess();
        if(!subscriptions.TryGetValue(window,out var subscription))
        {
            subscription=new ThemeSubscription(window,settings);subscriptions.Add(window,subscription);
        }
        subscription.Settings=settings;
        ApplyPalette(window,settings);
    }

    private static void ApplyPalette(Window window,AppSettings settings)
    {
        bool contrast=SystemParameters.HighContrast;
        bool dark=!contrast&&(settings.Theme=="dark"||(settings.Theme!="light"&&SystemIsDark()));
        Brush background=contrast?SystemColors.WindowBrush:Brush(dark?0x202020u:0xF5F5F5u);
        Brush surface=contrast?SystemColors.WindowBrush:Brush(dark?0x2C2C2Cu:0xFFFFFFu);
        Brush foreground=contrast?SystemColors.WindowTextBrush:Brush(dark?0xF4F4F4u:0x202020u);
        Brush muted=contrast?SystemColors.WindowTextBrush:Brush(dark?0xCCCCCCu:0x555555u);
        Brush border=contrast?SystemColors.WindowTextBrush:Brush(dark?0x767676u:0x888888u);
        Brush accent=contrast?SystemColors.HotTrackBrush:Brush(dark?0x90C8FFu:0x005A9Eu);
        Brush error=contrast?SystemColors.WindowTextBrush:Brush(dark?0xFFB4ABu:0xA4262Cu);
        Brush selection=contrast?SystemColors.HighlightBrush:Brush(dark?0x174A70u:0xD9ECFFu);
        var resources=window.Resources;
        resources[BackgroundKey]=background;resources[SurfaceKey]=surface;resources[ForegroundKey]=foreground;
        resources[MutedKey]=muted;resources[BorderKey]=border;resources[AccentKey]=accent;
        resources[ErrorKey]=error;resources[SelectionKey]=selection;
        resources[SystemColors.WindowBrushKey]=surface;resources[SystemColors.WindowTextBrushKey]=foreground;
        resources[SystemColors.ControlBrushKey]=background;resources[SystemColors.ControlTextBrushKey]=foreground;
        resources[SystemColors.ControlLightBrushKey]=surface;resources[SystemColors.ControlDarkBrushKey]=border;
        resources[SystemColors.GrayTextBrushKey]=muted;resources[SystemColors.HighlightBrushKey]=selection;
        resources[SystemColors.HighlightTextBrushKey]=contrast?SystemColors.HighlightTextBrush:foreground;
        resources[SystemColors.InactiveSelectionHighlightBrushKey]=selection;
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey]=foreground;
        window.Background=background;window.Foreground=foreground;
        window.FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");window.FontSize=FontSize(settings);
        // Native WPF templates keep keyboard focus, high contrast, and automation behavior.
        SetStyle<TextBlock>(resources,(TextBlock.ForegroundProperty,foreground));
        SetStyle<Label>(resources,(Control.ForegroundProperty,foreground));
        SetStyle<Button>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground),(Control.BorderBrushProperty,border));
        SetStyle<CheckBox>(resources,(Control.ForegroundProperty,foreground));
        SetStyle<TextBox>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground),
            (Control.BorderBrushProperty,border),(TextBoxBase.CaretBrushProperty,foreground),(TextBoxBase.SelectionBrushProperty,selection));
        SetStyle<ComboBox>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground),(Control.BorderBrushProperty,border));
        SetStyle<ComboBoxItem>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground));
        SetStyle<TabControl>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground),(Control.BorderBrushProperty,border));
        SetStyle<TabItem>(resources,(Control.BackgroundProperty,background),(Control.ForegroundProperty,foreground),(Control.PaddingProperty,new Thickness(14,8,14,8)));
        SetStyle<DataGrid>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground),(Control.BorderBrushProperty,border));
        SetStyle<DataGridRow>(resources,(Control.BackgroundProperty,surface),(Control.ForegroundProperty,foreground));
        SetStyle<DataGridCell>(resources,(Control.ForegroundProperty,foreground),(Control.BorderBrushProperty,border));
        SetStyle<DataGridColumnHeader>(resources,(Control.BackgroundProperty,background),(Control.ForegroundProperty,foreground),
            (Control.PaddingProperty,new Thickness(6)));
        SetStyle<ScrollViewer>(resources,(Control.BackgroundProperty,background),(Control.ForegroundProperty,foreground));
        window.InvalidateMeasure();
    }

    private static void SetStyle<T>(ResourceDictionary resources,params (DependencyProperty Property,object Value)[] setters) where T:FrameworkElement
    {
        var style=new Style(typeof(T));
        foreach(var (property,value) in setters)style.Setters.Add(new Setter(property,value));
        resources[typeof(T)]=style;
    }

    private static SolidColorBrush Brush(uint value)
    {
        var brush=new SolidColorBrush(Color.FromRgb((byte)(value>>16),(byte)(value>>8),(byte)value));brush.Freeze();return brush;
    }

    private static bool SystemIsDark()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value&&value==0;
        }
        catch(Exception error) when(error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private sealed class ThemeSubscription
    {
        private readonly Window window;
        private bool closed;
        internal AppSettings Settings { get; set; }

        internal ThemeSubscription(Window window,AppSettings settings)
        {
            this.window=window;Settings=settings;
            SystemParameters.StaticPropertyChanged+=SystemChanged;
            SystemEvents.UserPreferenceChanged+=PreferenceChanged;
            window.Closed+=Closed;
        }

        private void SystemChanged(object? sender,PropertyChangedEventArgs args)=>Refresh();
        private void PreferenceChanged(object sender,UserPreferenceChangedEventArgs args)=>Refresh();
        private void Refresh()
        {
            if(closed||window.Dispatcher.HasShutdownStarted)return;
            _=window.Dispatcher.InvokeAsync(()=>{if(!closed)ApplyPalette(window,Settings);});
        }

        private void Closed(object? sender,EventArgs args)
        {
            closed=true;
            SystemParameters.StaticPropertyChanged-=SystemChanged;
            SystemEvents.UserPreferenceChanged-=PreferenceChanged;
            window.Closed-=Closed;subscriptions.Remove(window);
        }
    }
}
