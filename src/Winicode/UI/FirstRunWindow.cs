using System.Windows;
using System.Windows.Controls;

namespace Winicode;

public sealed class FirstRunWindow:Window
{
    private readonly IAppController controller;
    private readonly StackPanel actions=new();
    private readonly UiStatus status=new("FirstRunStatus");
    private bool busy;
    private bool closed;

    public FirstRunWindow(IAppController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);this.controller=controller;
        Title=UiText.Welcome;Width=620;Height=620;MinWidth=440;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;UseLayoutRounding=true;
        UiElements.Identify(this,"FirstRunWindow",UiText.Welcome);
        UiElements.SetIcon(this,controller.Paths.BaseDirectory);
        var content=new StackPanel();
        content.Children.Add(UiElements.Heading(UiText.Welcome));
        content.Children.Add(UiElements.Text(UiText.WelcomeIntro));
        content.Children.Add(UiElements.Text(UiText.WelcomeKeys));
        content.Children.Add(UiElements.Heading(UiText.InputTest));
        content.Children.Add(UiElements.Text(UiText.WelcomeTest));
        var test=UiElements.Button(UiText.InputTest,"FirstRunInputTest");
        test.Click+=(_,_)=>
        {
            try{controller.ShowInputTest();status.Show(UiText.InputTestHelp);}
            catch(Exception error){status.Show(UiText.Error(error),true);}
        };
        actions.Children.Add(test);
        content.Children.Add(UiElements.Heading(UiText.Diagnostics));
        content.Children.Add(UiElements.Text(UiText.Privacy));
        var finish=UiElements.Button(UiText.FinishFirstRun,"CompleteFirstRun");finish.IsDefault=true;
        finish.Click+=async(_,_)=>await CompleteAsync(false);
        var settings=UiElements.Button(UiText.FinishAndSettings,"CompleteFirstRunAndSettings");
        settings.Click+=async(_,_)=>await CompleteAsync(true);
        var later=UiElements.Button(UiText.Later,"DeferFirstRun");later.Click+=(_,_)=>Close();
        var buttons=new WrapPanel();buttons.Children.Add(finish);buttons.Children.Add(settings);buttons.Children.Add(later);
        actions.Children.Add(buttons);content.Children.Add(actions);
        content.Children.Add(UiElements.Text(UiText.FirstRunLater));content.Children.Add(status);
        Content=UiElements.Scroll(content);
        ThemeManager.Apply(this,controller.Settings);
        controller.StateChanged+=ControllerChanged;
        Closed+=(_,_)=>{closed=true;controller.StateChanged-=ControllerChanged;};
        Loaded+=(_,_)=>Height=Math.Min(Height,Math.Max(MinHeight,SystemParameters.WorkArea.Height-40));
    }

    private async Task CompleteAsync(bool showSettings)
    {
        if(busy||closed)return;
        busy=true;actions.IsEnabled=false;status.Show(UiText.Busy);
        try
        {
            await controller.ApplySettingsAsync(controller.Settings with{FirstRunCompleted=true});
            if(closed)return;
            if(showSettings)controller.ShowSettings();
            Close();
        }
        catch(Exception error){if(!closed)status.Show(UiText.Error(error),true);}
        finally{busy=false;if(!closed)actions.IsEnabled=true;}
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
