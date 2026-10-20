using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

internal static class Program
{
    [STAThread] public static void Main()
    {
        var app=new Application();
        var edit=new TextBox{AcceptsReturn=true,FontSize=24,Margin=new Thickness(14)};
        var other=new TextBox{FontSize=20,Height=40,Margin=new Thickness(14,2,14,2)};
        AutomationProperties.SetAutomationId(edit,"TestText");AutomationProperties.SetAutomationId(other,"TestOther");
        InputMethod.SetPreferredImeState(edit,InputMethodState.On);
        var prepare=new Button{Content="开始拼音测试",Margin=new Thickness(12),Padding=new Thickness(8)};
        var layout=new DockPanel();DockPanel.SetDock(prepare,Dock.Bottom);layout.Children.Add(prepare);DockPanel.SetDock(other,Dock.Bottom);layout.Children.Add(other);layout.Children.Add(edit);
        var window=new Window{Title="Winicode verification target",Width=850,Height=450,Content=layout,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        void Focus(){edit.Focus();try{InputLanguageManager.Current.CurrentInputLanguage=CultureInfo.GetCultureInfo("zh-CN");}catch{}InputMethod.Current.ImeState=InputMethodState.On;}
        prepare.Click+=(_,_)=>Focus();window.Loaded+=(_,_)=>{window.Activate();Focus();};
        app.Run(window);
    }
}
