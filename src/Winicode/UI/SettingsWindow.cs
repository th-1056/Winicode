using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace Winicode;

public sealed class SettingsWindow:Window
{
    private readonly IAppController controller;
    private readonly CancellationTokenSource lifetime=new();
    private readonly TabControl tabs=new();
    private readonly TextBlock runtime=UiElements.Text("");
    private readonly UiStatus generalStatus=new("GeneralStatus");
    private readonly UiStatus inputStatus=new("InputDisplayStatus");
    private readonly UiStatus aliasStatus=new("AliasStatus");
    private readonly UiStatus aboutStatus=new("AboutStatus");
    private readonly TextBlock startupMessage=UiElements.Text("");
    private readonly CheckBox startup=UiElements.Check(UiText.Startup,"StartupEnabled");
    private readonly Button saveStartup=UiElements.Button(UiText.SaveStartup,"ApplyStartup");
    private readonly Button pause=UiElements.Button(UiText.Pause,"TogglePause");
    private readonly CheckBox showDock=UiElements.Check(UiText.DockButton,"ShowDockButton");
    private readonly CheckBox zeroKey=UiElements.Check(UiText.ZeroKey,"EnableZeroKey");
    private readonly CheckBox carryPreedit=UiElements.Check(UiText.CarryPreedit,"CarryPreedit");
    private readonly CheckBox numeric=UiElements.Check(UiText.NumericSelection,"NumericSelection");
    private readonly ComboBox theme=new(){MinWidth=180,Margin=new Thickness(0,0,0,12)};
    private readonly ComboBox font=new(){MinWidth=180,Margin=new Thickness(0,0,0,12)};
    private readonly TextBox exclusions=UiElements.Input("ExcludedApplications",UiText.Exclusions,true);
    private readonly DataGrid aliasesGrid=new();
    private readonly TextBox aliasFilter=UiElements.Input("AliasFilter",UiText.AliasFilter);
    private readonly TextBlock aliasCount=UiElements.Text("");
    private readonly TextBox character=UiElements.Input("AliasCharacter",UiText.Character);
    private readonly TextBox keyword=UiElements.Input("AliasKeyword",UiText.Keyword);
    private readonly TextBox description=UiElements.Input("AliasDescription",UiText.Description);
    private readonly Button updateAlias=UiElements.Button(UiText.UpdateAlias,"UpdateAlias");
    private readonly Button deleteAlias=UiElements.Button(UiText.DeleteAlias,"DeleteAlias");
    private readonly TextBox diagnosticPreview=UiElements.Input("DiagnosticPreview",UiText.Diagnostics,true);
    private readonly TextBlock previewState=UiElements.Text(UiText.PreviewEmpty);
    private readonly Button exportDiagnostics=UiElements.Button(UiText.ExportDiagnostics,"ExportDiagnostics");
    private readonly Button openRelease=UiElements.Button(UiText.OpenRelease,"OpenRelease");
    private readonly Button closeButton=UiElements.Button(UiText.Close,"CloseSettings");
    private IReadOnlyList<CustomAlias> savedAliases=Array.Empty<CustomAlias>();
    private CustomAlias? editingAlias;
    private HelpWindow? help;
    private bool loading;
    private bool inputDirty;
    private bool startupDirty;
    private bool busy;
    private bool closed;
    private bool previewReady;
    private string? releaseUrl;

    public SettingsWindow(IAppController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);this.controller=controller;
        Title=UiText.SettingsTitle;Width=820;Height=760;MinWidth=640;MinHeight=480;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        UseLayoutRounding=true;SnapsToDevicePixels=true;
        UiElements.Identify(this,"SettingsWindow",UiText.SettingsTitle);
        UiElements.SetIcon(this,controller.Paths.BaseDirectory);

        var layout=new DockPanel{Margin=new Thickness(16)};
        var footer=new DockPanel{LastChildFill=true,Margin=new Thickness(0,12,0,0)};
        closeButton.HorizontalAlignment=HorizontalAlignment.Right;closeButton.VerticalAlignment=VerticalAlignment.Center;closeButton.Margin=new Thickness(12,0,0,0);
        closeButton.Click+=(_,_)=>Close();DockPanel.SetDock(closeButton,Dock.Right);footer.Children.Add(closeButton);
        var footerText=new StackPanel();
        runtime.Margin=new Thickness(0,0,0,4);UiElements.Identify(runtime,"RuntimeStatus","运行状态");
        AutomationProperties.SetLiveSetting(runtime,AutomationLiveSetting.Polite);
        footerText.Children.Add(runtime);footerText.Children.Add(UiElements.Text(UiText.CloseHint));
        footer.Children.Add(footerText);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        UiElements.Identify(tabs,"SettingsTabs",UiText.SettingsTitle);
        AddTab(UiText.General,"GeneralTab",BuildGeneral());
        AddTab(UiText.InputDisplay,"InputDisplayTab",BuildInputDisplay());
        AddTab(UiText.Aliases,"AliasesTab",BuildAliases());
        AddTab(UiText.About,"AboutTab",BuildAbout());
        layout.Children.Add(tabs);Content=layout;

        foreach(var check in new[]{showDock,zeroKey,carryPreedit,numeric})
        {
            check.Checked+=(_,_)=>InputChanged();check.Unchecked+=(_,_)=>InputChanged();
        }
        theme.SelectionChanged+=(_,_)=>InputChanged();font.SelectionChanged+=(_,_)=>InputChanged();
        exclusions.TextChanged+=(_,_)=>InputChanged();
        startup.Checked+=(_,_)=>{if(!loading)startupDirty=true;};
        startup.Unchecked+=(_,_)=>{if(!loading)startupDirty=true;};
        aliasFilter.TextChanged+=(_,_)=>RefreshAliasRows();
        aliasesGrid.SelectionChanged+=(_,_)=>LoadAliasEditor();
        controller.StateChanged+=ControllerChanged;
        Closed+=OnClosed;
        Loaded+=(_,_)=>
        {
            Height=Math.Min(Height,Math.Max(MinHeight,SystemParameters.WorkArea.Height-40));
            Width=Math.Min(Width,Math.Max(MinWidth,SystemParameters.WorkArea.Width-40));
            RefreshController();
        };
        PreviewKeyDown+=(_,args)=>
        {
            if(args.Key==Key.Escape&&!busy){Close();args.Handled=true;}
        };
        RefreshController();
    }

    private void AddTab(string title,string id,UIElement content)=>tabs.Items.Add(
        UiElements.Identify(new TabItem{Header=title,Content=UiElements.Scroll(content)},id,title));

    private UIElement BuildGeneral()
    {
        var panel=new StackPanel();
        panel.Children.Add(UiElements.Heading(UiText.General));
        pause.Click+=async(_,_)=>await RunAsync(generalStatus,async()=>
        {
            await controller.TogglePauseAsync();
            generalStatus.Show(controller.IsPaused?UiText.Paused:UiText.Running);
        });
        panel.Children.Add(pause);panel.Children.Add(UiElements.Text(UiText.PauseHelp));
        panel.Children.Add(UiElements.Heading(UiText.Startup));panel.Children.Add(startup);
        panel.Children.Add(startupMessage);panel.Children.Add(UiElements.Text(UiText.StartupHelp));
        saveStartup.Click+=async(_,_)=>await RunAsync(generalStatus,async()=>
        {
            bool requested=startup.IsChecked==true;
            await controller.SetStartupEnabledAsync(requested);
            startupDirty=false;
            var actual=controller.GetStartupStatus();
            loading=true;try{startup.IsChecked=actual.Enabled;}finally{loading=false;}
            startupMessage.Text=actual.Message;
            generalStatus.Show(actual.Message,actual.Enabled!=requested);
        });
        panel.Children.Add(saveStartup);
        panel.Children.Add(UiElements.Heading(UiText.InputTest));
        var test=UiElements.Button(UiText.InputTest,"OpenInputTest");
        test.Click+=async(_,_)=>await RunAsync(generalStatus,()=>
        {
            controller.ShowInputTest();generalStatus.Show(UiText.InputTestHelp);return Task.CompletedTask;
        });
        panel.Children.Add(test);panel.Children.Add(UiElements.Text(UiText.InputTestHelp));
        panel.Children.Add(generalStatus);return panel;
    }

    private UIElement BuildInputDisplay()
    {
        var panel=new StackPanel();
        panel.Children.Add(UiElements.Heading(UiText.InputOptions));
        foreach(var check in new[]{showDock,zeroKey,carryPreedit,numeric})panel.Children.Add(check);
        panel.Children.Add(UiElements.Text(UiText.NumericHelp));
        theme.Items.Add(new Choice("system",UiText.ThemeSystem));
        theme.Items.Add(new Choice("light",UiText.ThemeLight));theme.Items.Add(new Choice("dark",UiText.ThemeDark));
        font.Items.Add(new Choice("small",UiText.FontSmall));font.Items.Add(new Choice("default",UiText.FontDefault));
        font.Items.Add(new Choice("large",UiText.FontLarge));
        UiElements.Identify(theme,"Theme",UiText.Theme);UiElements.Identify(font,"FontPreset",UiText.Font);
        theme.HorizontalAlignment=HorizontalAlignment.Left;font.HorizontalAlignment=HorizontalAlignment.Left;
        panel.Children.Add(UiElements.Label(UiText.Theme,theme));panel.Children.Add(theme);
        panel.Children.Add(UiElements.Label(UiText.Font,font));panel.Children.Add(font);
        panel.Children.Add(UiElements.Text(UiText.LayoutHelp));
        panel.Children.Add(UiElements.Heading(UiText.Exclusions));
        panel.Children.Add(UiElements.Text(UiText.ExclusionHelp));
        exclusions.Height=100;panel.Children.Add(exclusions);
        var current=UiElements.Button(UiText.AddCurrent,"AddCurrentApplication");
        current.Click+=async(_,_)=>await RunAsync(inputStatus,async()=>
        {
            inputStatus.Show(UiText.SwitchApplication);
            await Task.Delay(TimeSpan.FromSeconds(5),lifetime.Token);
            var foreground=Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(foreground,out uint processId);
            if(foreground==0||processId==0||processId==Environment.ProcessId)
            {inputStatus.Show(UiText.InvalidApplication,true);return;}
            using var process=Process.GetProcessById((int)processId);
            string name=process.ProcessName+".exe";
            var values=ReadExclusions().ToList();
            if(!values.Contains(name,StringComparer.OrdinalIgnoreCase))values.Add(name);
            exclusions.Text=string.Join(Environment.NewLine,values);
            inputStatus.Show(UiText.ApplicationAdded);
        });
        panel.Children.Add(current);
        var apply=UiElements.Button(UiText.Apply,"ApplySettings");apply.IsDefault=false;
        apply.Click+=async(_,_)=>await RunAsync(inputStatus,async()=>
        {
            var draft=controller.Settings with
            {
                ShowDockButton=showDock.IsChecked==true,EnableZeroKey=zeroKey.IsChecked==true,
                CarryPreedit=carryPreedit.IsChecked==true,NumericSelection=numeric.IsChecked==true,
                Theme=Selected(theme,"system"),FontPreset=Selected(font,"default"),
                ExcludedApplications=ReadExclusions()
            };
            if(!draft.ShowDockButton&&!draft.EnableZeroKey)throw new InvalidOperationException("请保留 U+ 按钮或 0 键中的至少一个入口。需要停用时可使用暂停功能。");
            await controller.ApplySettingsAsync(draft);
            inputDirty=false;LoadInputSettings();ThemeManager.Apply(this,controller.Settings);
            inputStatus.Show(UiText.Saved);
        });
        panel.Children.Add(apply);panel.Children.Add(inputStatus);return panel;
    }

    private UIElement BuildAliases()
    {
        var panel=new StackPanel();
        panel.Children.Add(UiElements.Text(UiText.AliasHelp));
        if(controller.AliasesNeedRecovery)panel.Children.Add(UiElements.Text("原词条文件无法读取，普通保存已暂停。请使用导入恢复有效备份，原文件会保留。"));
        panel.Children.Add(UiElements.Label(UiText.AliasFilter,aliasFilter));panel.Children.Add(aliasFilter);
        aliasesGrid.AutoGenerateColumns=false;aliasesGrid.IsReadOnly=true;aliasesGrid.CanUserAddRows=false;
        aliasesGrid.CanUserDeleteRows=false;aliasesGrid.CanUserReorderColumns=false;
        aliasesGrid.SelectionMode=DataGridSelectionMode.Single;
        aliasesGrid.SelectionUnit=DataGridSelectionUnit.FullRow;aliasesGrid.HeadersVisibility=DataGridHeadersVisibility.Column;
        aliasesGrid.Height=220;aliasesGrid.MinHeight=140;aliasesGrid.RowHeaderWidth=0;
        aliasesGrid.Margin=new Thickness(0,0,0,8);aliasesGrid.EnableRowVirtualization=true;
        UiElements.Identify(aliasesGrid,"AliasesGrid",UiText.Aliases);
        aliasesGrid.Columns.Add(new DataGridTextColumn{Header=UiText.Glyph,Binding=new Binding(nameof(AliasRow.Glyph)),Width=60});
        aliasesGrid.Columns.Add(new DataGridTextColumn{Header=UiText.CodePoint,Binding=new Binding(nameof(AliasRow.CodeLabel)),Width=90});
        aliasesGrid.Columns.Add(new DataGridTextColumn{Header=UiText.Keyword,Binding=new Binding(nameof(AliasRow.Keyword)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        aliasesGrid.Columns.Add(new DataGridTextColumn{Header=UiText.Description,Binding=new Binding(nameof(AliasRow.Description)),Width=new DataGridLength(1.5,DataGridLengthUnitType.Star)});
        panel.Children.Add(aliasesGrid);panel.Children.Add(aliasCount);
        foreach(var (label,input) in new[]{(UiText.Character,character),(UiText.Keyword,keyword),(UiText.Description,description)})
        {
            panel.Children.Add(UiElements.Label(label,input));panel.Children.Add(input);
        }
        var buttons=new WrapPanel();
        var clear=UiElements.Button(UiText.NewAlias,"NewAlias");clear.Click+=(_,_)=>ResetAliasEditor();buttons.Children.Add(clear);
        var add=UiElements.Button(UiText.AddAlias,"AddAlias");
        add.Click+=async(_,_)=>await RunAsync(aliasStatus,()=>SaveAliasAsync(false));buttons.Children.Add(add);
        updateAlias.Click+=async(_,_)=>await RunAsync(aliasStatus,()=>SaveAliasAsync(true));buttons.Children.Add(updateAlias);
        deleteAlias.Click+=async(_,_)=>await RunAsync(aliasStatus,async()=>
        {
            var selected=editingAlias;
            if(selected==null){aliasStatus.Show(UiText.SelectAlias,true);return;}
            await controller.SaveAliasesAsync(savedAliases.Where(item=>!SameAlias(item,selected)).ToArray());
            ReloadAliases();ResetAliasEditor();aliasStatus.Show(UiText.AliasesSaved);
        });
        buttons.Children.Add(deleteAlias);panel.Children.Add(buttons);
        var files=new WrapPanel();
        var import=UiElements.Button(UiText.ImportAliases,"ImportAliases");import.ToolTip=UiText.ImportHelp;
        import.Click+=async(_,_)=>await RunAsync(aliasStatus,ImportAliasesAsync);files.Children.Add(import);
        var export=UiElements.Button(UiText.ExportAliases,"ExportAliases");
        export.Click+=async(_,_)=>await RunAsync(aliasStatus,ExportAliasesAsync);files.Children.Add(export);
        panel.Children.Add(files);panel.Children.Add(UiElements.Text(UiText.ImportHelp));panel.Children.Add(aliasStatus);
        updateAlias.IsEnabled=false;deleteAlias.IsEnabled=false;return panel;
    }

    private UIElement BuildAbout()
    {
        var panel=new StackPanel();
        panel.Children.Add(UiElements.Heading(BuildInfo.Name));
        panel.Children.Add(UiElements.Text(UiText.SoftwareVersion+BuildInfo.Version));
        panel.Children.Add(UiElements.Text(UiText.UnicodeVersion+ReadUnicodeVersion()));
        panel.Children.Add(UiElements.Text(BuildInfo.Copyright));panel.Children.Add(UiElements.Text(UiText.LicenseName));
        var links=new WrapPanel();
        AddAction(links,UiText.Repository,"OpenRepository",()=>
        {
            if(UiElements.RepositoryUri()==null){aboutStatus.Show(UiText.RepositoryUnavailable,true);return;}
            UiElements.OpenRepository();
        });
        AddAction(links,UiText.License,"OpenLicense",()=>
        {
            try{UiElements.OpenDocument(controller,"LICENSE","LICENSE.txt","LICENSE.md");}
            catch(FileNotFoundException){aboutStatus.Show(UiText.FileUnavailable,true);}
        });
        AddAction(links,UiText.ChangeLog,"OpenChangeLog",()=>
        {
            if(UiElements.RepositoryUri()!=null)UiElements.OpenRepository("/releases");
            else aboutStatus.Show(UiText.RepositoryUnavailable,true);
        });
        AddAction(links,UiText.Help,"OpenHelp",ShowHelp);panel.Children.Add(links);
        var updates=new WrapPanel();var check=UiElements.Button(UiText.CheckUpdates,"CheckUpdates");
        check.Click+=async(_,_)=>await RunAsync(aboutStatus,async()=>
        {
            releaseUrl=null;openRelease.IsEnabled=false;
            var result=await controller.CheckUpdatesAsync(lifetime.Token);
            if(closed)return;
            aboutStatus.Show(result.Message);
            if(result.UpdateAvailable&&!string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {releaseUrl=result.ReleaseUrl;openRelease.IsEnabled=true;}
        });
        updates.Children.Add(check);openRelease.IsEnabled=false;
        openRelease.Click+=async(_,_)=>await RunAsync(aboutStatus,()=>
        {
            if(releaseUrl!=null)UiElements.OpenRelease(releaseUrl);return Task.CompletedTask;
        });
        updates.Children.Add(openRelease);panel.Children.Add(updates);panel.Children.Add(UiElements.Text(UiText.UpdateHelp));
        panel.Children.Add(UiElements.Heading(UiText.Diagnostics));panel.Children.Add(UiElements.Text(UiText.DiagnosticsHelp));
        diagnosticPreview.Height=200;diagnosticPreview.IsReadOnly=true;diagnosticPreview.IsUndoEnabled=false;
        diagnosticPreview.FontFamily=new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");
        panel.Children.Add(diagnosticPreview);panel.Children.Add(previewState);
        var diagnostics=new WrapPanel();
        var preview=UiElements.Button(UiText.PreviewDiagnostics,"PreviewDiagnostics");
        preview.Click+=async(_,_)=>await RunAsync(aboutStatus,()=>
        {
            diagnosticPreview.Text=controller.BuildDiagnosticPreview();previewReady=true;
            exportDiagnostics.IsEnabled=true;previewState.Text=UiText.PreviewReady;return Task.CompletedTask;
        });
        diagnostics.Children.Add(preview);exportDiagnostics.IsEnabled=false;
        exportDiagnostics.Click+=async(_,_)=>await RunAsync(aboutStatus,async()=>
        {
            string current=controller.BuildDiagnosticPreview();
            if(!previewReady||current!=diagnosticPreview.Text)
            {
                InvalidatePreview();aboutStatus.Show(UiText.PreviewStale,true);return;
            }
            string report=await controller.ExportDiagnosticsAsync();
            if(!closed)aboutStatus.Show(UiText.DiagnosticExported+report);
        });
        diagnostics.Children.Add(exportDiagnostics);
        AddAction(diagnostics,UiText.OpenLogs,"OpenLogs",()=>
        {
            if(!Directory.Exists(controller.Paths.LogsDirectory)){aboutStatus.Show(UiText.LogsUnavailable);return;}
            UiElements.OpenFolder(controller.Paths.LogsDirectory);
        });
        panel.Children.Add(diagnostics);panel.Children.Add(aboutStatus);return panel;
    }

    private void AddAction(Panel panel,string title,string id,Action action)
    {
        var button=UiElements.Button(title,id);
        button.Click+=async(_,_)=>await RunAsync(aboutStatus,()=>{action();return Task.CompletedTask;});
        panel.Children.Add(button);
    }

    private async Task RunAsync(UiStatus status,Func<Task> action)
    {
        if(closed||busy)return;
        busy=true;tabs.IsEnabled=false;closeButton.IsEnabled=false;status.Show(UiText.Busy);
        try{await action();}
        catch(OperationCanceledException) when(closed){ }
        catch(Exception error){if(!closed)status.Show(UiText.Error(error),true);}
        finally
        {
            busy=false;
            if(!closed){tabs.IsEnabled=true;closeButton.IsEnabled=true;RefreshRuntime();}
        }
    }

    private void InputChanged()
    {
        if(loading||closed)return;
        inputDirty=true;inputStatus.Show(UiText.Unsaved);
        ThemeManager.Apply(this,controller.Settings with{Theme=Selected(theme,"system"),FontPreset=Selected(font,"default")});
    }

    private void LoadInputSettings()
    {
        loading=true;
        try
        {
            var settings=controller.Settings;
            showDock.IsChecked=settings.ShowDockButton;zeroKey.IsChecked=settings.EnableZeroKey;
            carryPreedit.IsChecked=settings.CarryPreedit;numeric.IsChecked=settings.NumericSelection;
            Select(theme,settings.Theme,"system");Select(font,settings.FontPreset,"default");
            exclusions.Text=string.Join(Environment.NewLine,settings.ExcludedApplications);
        }
        finally{loading=false;}
    }

    private string[] ReadExclusions()
    {
        var names=exclusions.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
            .Select(item=>item.Trim()).Where(item=>item.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(names.Length>200)throw new ArgumentException(UiText.TooManyProcesses);
        if(names.Any(name=>name.Length>128||name.Any(ch=>char.IsControl(ch)||"\\/:*?\"<>|".Contains(ch))||name is "." or ".."))
            throw new ArgumentException(UiText.InvalidProcess);
        return names;
    }

    private void ControllerChanged(object? sender,EventArgs args)
    {
        if(closed||Dispatcher.HasShutdownStarted)return;
        if(Dispatcher.CheckAccess())RefreshController();
        else _=Dispatcher.InvokeAsync(()=>{if(!closed)RefreshController();});
    }

    private void RefreshController()
    {
        if(closed)return;
        try
        {
            RefreshRuntime();
            var status=controller.GetStartupStatus();
            loading=true;
            try
            {
                if(!startupDirty)startup.IsChecked=status.Enabled;
                bool supported=status.Supported&&!controller.Paths.IsPortable;
                startup.IsEnabled=supported;saveStartup.IsEnabled=supported;
                startupMessage.Text=controller.Paths.IsPortable?UiText.PortableStartup:status.Message;
            }
            finally{loading=false;}
        }
        catch(Exception error){generalStatus.Show(UiText.Error(error),true);}
        try
        {
            if(!inputDirty)LoadInputSettings();
            ThemeManager.Apply(this,inputDirty?controller.Settings with{Theme=Selected(theme,"system"),FontPreset=Selected(font,"default")}:controller.Settings);
        }
        catch(Exception error){inputStatus.Show(UiText.Error(error),true);}
        try
        {
            var current=controller.GetAliases();
            if(!savedAliases.SequenceEqual(current)){savedAliases=current.ToArray();RefreshAliasRows();}
            else if(aliasesGrid.ItemsSource==null)RefreshAliasRows();
        }
        catch(Exception error){aliasStatus.Show(UiText.Error(error),true);}
        InvalidatePreview();
    }

    private void RefreshRuntime()
    {
        if(closed)return;
        pause.Content=controller.IsPaused?UiText.Resume:UiText.Pause;
        AutomationProperties.SetName(pause,controller.IsPaused?UiText.Resume:UiText.Pause);
        runtime.Text=(controller.IsPaused?UiText.Paused:UiText.Running)+"\n"+controller.StatusText;
        AutomationProperties.SetName(runtime,runtime.Text);
    }

    private void InvalidatePreview()
    {
        if(!previewReady)return;
        previewReady=false;exportDiagnostics.IsEnabled=false;previewState.Text=UiText.PreviewStale;
    }

    private void ReloadAliases()
    {
        savedAliases=controller.GetAliases().ToArray();RefreshAliasRows();
    }

    private void RefreshAliasRows()
    {
        string filter=aliasFilter.Text.Trim();
        var selected=editingAlias;
        var rows=savedAliases.Select(item=>new AliasRow(item)).Where(row=>filter.Length==0
            ||row.CodeLabel.Contains(filter,StringComparison.OrdinalIgnoreCase)
            ||row.Glyph.Contains(filter,StringComparison.OrdinalIgnoreCase)
            ||row.Keyword.Contains(filter,StringComparison.OrdinalIgnoreCase)
            ||row.Description.Contains(filter,StringComparison.OrdinalIgnoreCase)).ToArray();
        aliasesGrid.ItemsSource=rows;
        if(selected!=null)aliasesGrid.SelectedItem=rows.FirstOrDefault(row=>SameAlias(row.Alias,selected));
        aliasCount.Text=UiText.AliasCount(rows.Length,savedAliases.Count);
    }

    private void LoadAliasEditor()
    {
        editingAlias=(aliasesGrid.SelectedItem as AliasRow)?.Alias;
        updateAlias.IsEnabled=editingAlias!=null;deleteAlias.IsEnabled=editingAlias!=null;
        if(editingAlias==null)return;
        character.Text=$"U+{editingAlias.CodePoint:X4}";keyword.Text=editingAlias.Keyword;description.Text=editingAlias.Description;
    }

    private void ResetAliasEditor()
    {
        aliasesGrid.SelectedItem=null;editingAlias=null;
        character.Clear();keyword.Clear();description.Clear();updateAlias.IsEnabled=false;deleteAlias.IsEnabled=false;
        character.Focus();
    }

    private async Task SaveAliasAsync(bool update)
    {
        var original=editingAlias;
        if(update&&original==null){aliasStatus.Show(UiText.SelectAlias,true);return;}
        if(!TryCodePoint(character.Text,out int codePoint)){aliasStatus.Show(UiText.InvalidScalar,true);return;}
        string word=keyword.Text.Trim();
        if(word.Length==0){aliasStatus.Show(UiText.EmptyKeyword,true);return;}
        var candidate=new CustomAlias(codePoint,word,description.Text.Trim());
        var next=savedAliases.Where(item=>!update||!SameAlias(item,original!)).ToList();
        if(next.Any(item=>SameAlias(item,candidate))){aliasStatus.Show(UiText.DuplicateAlias,true);return;}
        next.Add(candidate);await controller.SaveAliasesAsync(next);
        ReloadAliases();ResetAliasEditor();aliasStatus.Show(UiText.AliasesSaved);
    }

    private async Task ImportAliasesAsync()
    {
        var dialog=new OpenFileDialog{Title=UiText.ImportAliases,Filter=UiText.JsonFilter,CheckFileExists=true,Multiselect=false};
        if(dialog.ShowDialog(this)!=true){aliasStatus.Show("");return;}
        var incoming=await Task.Run(()=>new AliasStore(controller.Paths).Import(dialog.FileName));
        if(closed)return;
        var merged=savedAliases.ToList();
        foreach(var item in incoming)
        {
            int index=merged.FindIndex(existing=>SameAlias(existing,item));
            if(index>=0)merged[index]=item;else merged.Add(item);
        }
        if(controller.AliasesNeedRecovery)
        {
            if(MessageBox.Show(this,"将使用导入的有效词条恢复列表，损坏的原文件会保留。继续吗？","恢复词条",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            await controller.RecoverAliasesAsync(merged);
        }
        else await controller.SaveAliasesAsync(merged);
        ReloadAliases();ResetAliasEditor();aliasStatus.Show(UiText.AliasesImported);
    }

    private async Task ExportAliasesAsync()
    {
        var dialog=new SaveFileDialog{Title=UiText.ExportAliases,Filter=UiText.JsonFilter,
            FileName="Winicode-aliases.json",DefaultExt=".json",AddExtension=true,OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true){aliasStatus.Show("");return;}
        var snapshot=savedAliases.ToArray();
        await Task.Run(()=>new AliasStore(controller.Paths).Export(dialog.FileName,snapshot));
        if(!closed)aliasStatus.Show(UiText.AliasesExported);
    }

    internal static bool TryCodePoint(string value,out int codePoint)
    {
        codePoint=0;string text=value.Trim();
        bool explicitCode=text.StartsWith("U+",StringComparison.OrdinalIgnoreCase)||text.StartsWith("0x",StringComparison.OrdinalIgnoreCase);
        if(explicitCode)text=text[2..];
        else if(Rune.TryGetRuneAt(text,0,out var rune)&&rune.Utf16SequenceLength==text.Length)
        {codePoint=rune.Value;return true;}
        if(text.Length==0||text.Length>6||(!explicitCode&&text.Length<4)
            ||!int.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out codePoint))return false;
        return Rune.IsValid(codePoint);
    }

    private static bool SameAlias(CustomAlias left,CustomAlias right)=>left.CodePoint==right.CodePoint
        &&string.Equals(left.Keyword,right.Keyword,StringComparison.OrdinalIgnoreCase);

    private static string Selected(ComboBox box,string fallback)=>(box.SelectedItem as Choice)?.Value??fallback;
    private static void Select(ComboBox box,string value,string fallback)=>box.SelectedItem=
        box.Items.Cast<Choice>().FirstOrDefault(item=>item.Value==value)??box.Items.Cast<Choice>().First(item=>item.Value==fallback);

    private string ReadUnicodeVersion()
    {
        try
        {
            string path=Path.Combine(controller.Paths.DataDirectory,"sources.json");
            if(new FileInfo(path).Length>65536)return UiText.MetadataUnavailable;
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("version",out var version)&&version.ValueKind==JsonValueKind.String
                ?version.GetString()??UiText.MetadataUnavailable:UiText.MetadataUnavailable;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException)
        {return UiText.MetadataUnavailable;}
    }

    private void ShowHelp()
    {
        if(help==null){help=new HelpWindow(controller){Owner=this};help.Closed+=(_,_)=>help=null;}
        help.Show();help.Activate();
    }

    private void OnClosed(object? sender,EventArgs args)
    {
        closed=true;controller.StateChanged-=ControllerChanged;lifetime.Cancel();lifetime.Dispose();
        diagnosticPreview.Clear();
    }

    private sealed record Choice(string Value,string Label)
    {
        public override string ToString()=>Label;
    }

    private sealed record AliasRow(CustomAlias Alias)
    {
        public string CodeLabel=>$"U+{Alias.CodePoint:X4}";
        public string Keyword=>Alias.Keyword;
        public string Description=>Alias.Description;
        public string Glyph=>Rune.IsValid(Alias.CodePoint)&&Rune.GetUnicodeCategory(new Rune(Alias.CodePoint)) is not
            (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator)
            ?char.ConvertFromUtf32(Alias.CodePoint):$"[{CodeLabel}]";
    }
}
