using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Winicode;

internal static class UiElements
{
    internal static T Identify<T>(T element,string id,string name) where T:DependencyObject
    {
        AutomationProperties.SetAutomationId(element,id);
        AutomationProperties.SetName(element,name);
        return element;
    }

    internal static TextBlock Text(string text)=>new()
    {
        Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
    };

    internal static TextBlock Heading(string text)=>new()
    {
        Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,8),
        TextWrapping=TextWrapping.Wrap
    };

    internal static Button Button(string text,string id)=>Identify(new Button
    {
        Content=text,Padding=new Thickness(12,5,12,5),MinHeight=32,
        Margin=new Thickness(0,0,8,8),HorizontalAlignment=HorizontalAlignment.Left
    },id,text);

    internal static CheckBox Check(string text,string id)=>Identify(new CheckBox
    {
        Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap},
        Margin=new Thickness(0,0,0,10),VerticalContentAlignment=VerticalAlignment.Center
    },id,text);

    internal static TextBox Input(string id,string name,bool multiline=false)=>Identify(new TextBox
    {
        MinHeight=30,Padding=new Thickness(6,4,6,4),Margin=new Thickness(0,0,0,10),
        AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,
        VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden
    },id,name);

    internal static Label Label(string text,UIElement target)=>new()
    {
        Content=text,Target=target,Padding=new Thickness(0,0,0,4)
    };

    internal static ScrollViewer Scroll(UIElement child)=>new()
    {
        Content=child,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(16)
    };

    internal static void SetIcon(Window window,string baseDirectory)
    {
        try
        {
            string file=Path.Combine(baseDirectory,"Assets","Winicode.ico");
            if(!File.Exists(file))return;
            var image=new BitmapImage();
            image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
            image.UriSource=new Uri(file,UriKind.Absolute);image.EndInit();image.Freeze();
            window.Icon=image;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // A missing icon must not prevent access to settings or recovery tools.
        }
    }

    internal static void OpenFolder(string directory)
    {
        if(!Directory.Exists(directory))throw new DirectoryNotFoundException();
        Process.Start(new ProcessStartInfo(directory){UseShellExecute=true});
    }

    internal static void OpenDocument(IAppController controller,params string[] relativePaths)
    {
        foreach(string relative in relativePaths)
        {
            string path=Path.Combine(controller.Paths.BaseDirectory,relative);
            if(!File.Exists(path))continue;
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return;
        }
        throw new FileNotFoundException();
    }

    internal static Uri? RepositoryUri()
    {
        if(!Uri.TryCreate(BuildInfo.RepositoryUrl,UriKind.Absolute,out var uri)
            ||uri.Scheme!=Uri.UriSchemeHttps||!uri.IsDefaultPort||uri.UserInfo.Length!=0
            ||!uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase))return null;
        return uri.AbsolutePath.Trim('/').Split('/').Length==2?uri:null;
    }

    internal static void OpenRepository(string suffix="")
    {
        var repository=RepositoryUri()??throw new InvalidOperationException(UiText.RepositoryUnavailable);
        OpenHttps(new Uri(repository.AbsoluteUri.TrimEnd('/')+suffix));
    }

    internal static void OpenRelease(string releaseUrl)
    {
        var repository=RepositoryUri();
        if(repository==null||!Uri.TryCreate(releaseUrl,UriKind.Absolute,out var release)
            ||release.Scheme!=Uri.UriSchemeHttps||!release.IsDefaultPort||release.UserInfo.Length!=0
            ||!release.Host.Equals(repository.Host,StringComparison.OrdinalIgnoreCase)
            ||!release.AbsolutePath.StartsWith(repository.AbsolutePath.TrimEnd('/')+"/releases",StringComparison.OrdinalIgnoreCase)
            ||(release.AbsolutePath.Length>repository.AbsolutePath.TrimEnd('/').Length+9
                &&release.AbsolutePath[repository.AbsolutePath.TrimEnd('/').Length+9]!='/'))
            throw new InvalidOperationException(UiText.InvalidLink);
        OpenHttps(release);
    }

    private static void OpenHttps(Uri uri)=>Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
}

internal sealed class UiStatus:TextBlock
{
    internal UiStatus(string id)
    {
        UiElements.Identify(this,id,"操作结果");
        AutomationProperties.SetLiveSetting(this,AutomationLiveSetting.Polite);
        TextWrapping=TextWrapping.Wrap;Margin=new Thickness(0,8,0,8);
        Focusable=true;
    }

    internal void Show(string message,bool error=false)
    {
        Text=message;
        SetResourceReference(ForegroundProperty,error?ThemeManager.ErrorKey:ThemeManager.ForegroundKey);
        AutomationProperties.SetName(this,message);
        ToolTip=message;
    }
}
