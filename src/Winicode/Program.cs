using System.Text.Json;
using System.Windows;

namespace Winicode;

internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        AppPaths? paths=null;InstanceChannel? channel=null;DesktopHost? host=null;
        try
        {
            string? Option(string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
            paths=AppPaths.Resolve(userDataOverride:Option("--data-dir"));
            if(args.Contains("--remove-startup")){new StartupService(paths).RemoveOwnedEntry();return 0;}
            if(args.Contains("--set-startup"))
            {
                var value=Option("--set-startup");if(value is not ("on" or "off"))return 2;
                new StartupService(paths).SetEnabled(value=="on");return 0;
            }
            if(args.Contains("--cleanup-user-data"))return Cleanup(paths,Option("--data-dir")==null)?0:2;
            if(args.Contains("--self-test"))return SelfTest(paths);
            var command=args.Contains("--exit")?"exit":args.Contains("--pause")?"pause":args.Contains("--resume")?"resume":"settings";
            using var singleton=new Mutex(false,InstanceChannel.MutexName);
            bool first;
            try{first=singleton.WaitOne(0);}catch(AbandonedMutexException){first=true;}
            if(!first)
            {
                bool accepted=InstanceChannel.SendAsync(command,TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
                if(accepted&&command=="exit"&&args.Contains("--wait"))
                {
                    try{if(!singleton.WaitOne(TimeSpan.FromSeconds(10)))return 3;singleton.ReleaseMutex();}
                    catch(AbandonedMutexException){singleton.ReleaseMutex();}
                }
                return accepted?0:3;
            }
            if(command is "exit" or "pause" or "resume"){singleton.ReleaseMutex();return 0;}
            try
            {
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var log=new DiagnosticsService(paths);
                app.DispatcherUnhandledException+=(_,e)=>
                {
                    log.LogException(e.Exception,"ui-unhandled");
                    e.Handled=true;
                    host?.Report("操作未完成，请重试或在关于页面导出诊断。",true);
                };
                host=new DesktopHost(paths);channel=new InstanceChannel();channel.Start(paths.BaseDirectory,host.HandleCommandAsync);
                app.Exit+=(_,_)=>{channel.Dispose();host.Dispose();};
                host.Start(args.Contains("--background"));
                app.Run();return 0;
            }
            finally{singleton.ReleaseMutex();}
        }
        catch(Exception e)
        {
            if(paths!=null)new DiagnosticsService(paths).LogException(e,"startup");
            if(!args.Contains("--background")&&!args.Contains("--self-test"))
                MessageBox.Show("Winicode 未能完成启动。请确认安装文件完整，并检查用户目录中的日志。","Winicode",MessageBoxButton.OK,MessageBoxImage.Error);
            return 1;
        }
        finally{channel?.Dispose();host?.Dispose();}
    }
    private static int SelfTest(AppPaths paths)
    {
        var failures=new List<string>();
        try
        {
            var catalog=UnicodeCatalog.Load(paths.DataDirectory);
            if(catalog.Lookup(0x1D6FC).Character!="𝛼")failures.Add("Supplementary scalar lookup");
            if(catalog.Search("alpha",5).FirstOrDefault()?.CodePoint!=0x03B1)failures.Add("Name search");
            Directory.CreateDirectory(paths.UserDataDirectory);
            File.WriteAllText(Path.Combine(paths.UserDataDirectory,"self-test.json"),JsonSerializer.Serialize(new {Product=BuildInfo.Name,BuildInfo.Version,Unicode=catalog.Version,Failures=failures}));
        }
        catch{failures.Add("Data loading or user-directory check");}
        return failures.Count==0?0:1;
    }
    private static bool Cleanup(AppPaths paths,bool standardPath)
    {
        if(!standardPath||paths.IsPortable)return false;
        var expected=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Winicode"));
        if(!Path.GetFullPath(paths.UserDataDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(expected,StringComparison.OrdinalIgnoreCase))return false;
        if(Directory.Exists(expected)&&(File.GetAttributes(expected)&FileAttributes.ReparsePoint)!=0)return false;
        new StartupService(paths).RemoveOwnedEntry();
        if(Directory.Exists(expected))foreach(var file in Directory.EnumerateFiles(expected))
        {
            var name=Path.GetFileName(file);
            if(new[]{"settings.json","settings.json.bak","aliases.json","aliases.json.bak","self-test.json"}.Contains(name,StringComparer.OrdinalIgnoreCase)
                ||name.StartsWith("settings.json.damaged.",StringComparison.OrdinalIgnoreCase)
                ||name.StartsWith("aliases.json.damaged.",StringComparison.OrdinalIgnoreCase)
                ||(name.StartsWith("Winicode-diagnostics-",StringComparison.Ordinal)&&name.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)))File.Delete(file);
        }
        if(Directory.Exists(paths.LogsDirectory))
        {
            if((File.GetAttributes(paths.LogsDirectory)&FileAttributes.ReparsePoint)!=0)return false;
            foreach(var file in Directory.EnumerateFiles(paths.LogsDirectory))
                if(new[]{"diagnostics.log","diagnostics.log.1","diagnostics.log.2"}.Contains(Path.GetFileName(file),StringComparer.OrdinalIgnoreCase))File.Delete(file);
            if(!Directory.EnumerateFileSystemEntries(paths.LogsDirectory).Any())Directory.Delete(paths.LogsDirectory);
        }
        if(Directory.Exists(expected)&&!Directory.EnumerateFileSystemEntries(expected).Any())Directory.Delete(expected);
        return true;
    }
}
