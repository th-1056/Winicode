using System.Windows;
using System.Windows.Controls;

namespace Winicode;

internal sealed class HelpWindow:Window
{
    private readonly IAppController controller;
    private readonly UiStatus status=new("HelpStatus");
    private bool closed;

    internal HelpWindow(IAppController controller)
    {
        this.controller=controller;
        Title=UiText.Help;Width=580;Height=500;MinWidth=400;MinHeight=320;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;UseLayoutRounding=true;
        UiElements.Identify(this,"HelpWindow",UiText.Help);
        UiElements.SetIcon(this,controller.Paths.BaseDirectory);
        var content=new StackPanel();content.Children.Add(UiElements.Heading(UiText.Help));
        foreach(string text in new[]{UiText.HelpIntro,UiText.HelpSearch,UiText.HelpKeys,UiText.HelpLimit,UiText.HelpTray,UiText.InputTestHelp})
            content.Children.Add(UiElements.Text(text));
        var buttons=new WrapPanel();
        var test=UiElements.Button(UiText.InputTest,"HelpInputTest");
        test.Click+=(_,_)=>
        {
            try{controller.ShowInputTest();status.Show(UiText.InputTestHelp);}
            catch(Exception error){status.Show(UiText.Error(error),true);}
        };
        buttons.Children.Add(test);
        var close=UiElements.Button(UiText.Close,"CloseHelp");close.Click+=(_,_)=>Close();buttons.Children.Add(close);
        content.Children.Add(buttons);content.Children.Add(status);Content=UiElements.Scroll(content);
        ThemeManager.Apply(this,controller.Settings);controller.StateChanged+=ControllerChanged;
        Closed+=(_,_)=>{closed=true;controller.StateChanged-=ControllerChanged;};
    }

    private void ControllerChanged(object? sender,EventArgs args)
    {
        if(closed||Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.InvokeAsync(()=>
        {
            if(closed)return;
            try{ThemeManager.Apply(this,controller.Settings);}
            catch(Exception error){status.Show(UiText.Error(error),true);}
        });
    }
}
