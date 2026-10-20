using System.Windows;
using System.Windows.Automation;

namespace Winicode;

internal sealed record CandidateAnchor(nint Window,Rect Bounds,Rect ButtonBounds,nint TargetWindow,nint TargetFocus,long ObservedAt,InputTarget? Target);

internal sealed class CandidateTracker : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Thread? thread;
    private readonly AutoResetEvent wake=new(false);
    private long fastUntil;
    private volatile bool enabled=true;
    private HashSet<string> excluded=new(StringComparer.OrdinalIgnoreCase);
    internal event Action<CandidateAnchor?>? Changed;
    internal string LastError {get;private set;}="";
    internal void Configure(bool active,IEnumerable<string> exclusions){enabled=active;Volatile.Write(ref excluded,new HashSet<string>(exclusions.Select(Path.GetFileNameWithoutExtension).OfType<string>(),StringComparer.OrdinalIgnoreCase));wake.Set();}
    internal void RequestScan(){Interlocked.Exchange(ref fastUntil,Environment.TickCount64+400);wake.Set();}

    internal void Start()
    {
        thread=new Thread(Run){IsBackground=true,Name="IME candidate observer"};
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }
    private void Run()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var h=Native.GetForegroundWindow();
                var excludedNow=Volatile.Read(ref excluded).Contains(Native.ProcessName(h));
                Changed?.Invoke(enabled&&!excludedNow?Find():null);
            }
            catch(Exception e) { LastError=e.Message; Changed?.Invoke(null); }
            WaitHandle.WaitAny([stop.Token.WaitHandle,wake],Environment.TickCount64<Interlocked.Read(ref fastUntil)?25:200);
        }
    }
    internal static CandidateAnchor? Find()
    {
        var target=Native.GetForegroundWindow();
        if(target==0)return null;
        foreach(var h in Native.TopWindows().Where(h=>Native.ClassName(h) is "ApplicationFrameWindow" or "Windows.UI.Core.CoreWindow").SelectMany(h=>Native.DescendantWindows(h).Prepend(h)))
        {
            var cls=Native.ClassName(h);
            if(!Native.IsWindowVisible(h)||cls!="Windows.UI.Core.CoreWindow"||Native.ProcessName(h)!="TextInputHost")continue;
            try
            {
                var root=AutomationElement.FromHandle(h);
                var candidate=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"IME_Candidate_Window"));
                if(candidate==null)continue;
                var r=candidate.Current.BoundingRectangle;
                if(candidate.Current.IsOffscreen||r.IsEmpty||r.Width<50||r.Height<15)continue;
                var arrow=candidate.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"TEMPLATE_PART_searchSuggestionButton"));
                var arrowRect=arrow?.Current.BoundingRectangle??Rect.Empty;
                if(Native.GetForegroundWindow()!=target)return null;
                var captured=InputTarget.Capture();
                return new(h,r,arrowRect,target,Native.ThreadInfo(target).hwndFocus,Environment.TickCount64,captured);
            }
            catch { /* Providers can disappear between keyboard events; retry with a fresh element. */ }
        }
        return null;
    }
    public void Dispose(){stop.Cancel();wake.Set();if(thread!=Thread.CurrentThread)thread?.Join(1200);}
}
