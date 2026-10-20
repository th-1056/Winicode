using System.Runtime.InteropServices;

namespace Winicode;

internal sealed class KeyboardTrigger : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint HookProc(int code,nint w,nint l);
    [StructLayout(LayoutKind.Sequential)] private struct KeyData { public uint vk,scan,flags,time;public nuint extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Native.POINT point;public uint data,flags,time;public nuint extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG {public nint hwnd;public uint msg;public nuint w;public nint l;public uint time;public Native.POINT point;public uint reserved;}
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookEx(int id,HookProc proc,nint module,uint tid);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nint w,nint l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg,nint h,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id,uint msg,nuint w,nint l);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Native.POINT point);
    private readonly HookProc callback;
    private readonly HookProc mouseCallback;
    private readonly Thread thread;
    private readonly object gate=new();
    private readonly CompositionBuffer buffer=new();
    private CandidateAnchor? anchor;
    private nint target;
    private nint focus;
    private long lastKey;
    private long lastCandidate;
    private bool confirmed;
    private bool suppressZero;
    private bool enabled=true;
    private bool zeroEnabled=true;
    private nint hook;
    private nint mouseHook;
    private uint threadId;
    private readonly ManualResetEventSlim ready=new();
    internal event Action<string,CandidateAnchor>? Requested;
    internal event Action? InputObserved;
    internal nint DockHandle {get;set;}
    internal void Configure(bool active,bool allowZero){lock(gate){enabled=active;zeroEnabled=allowZero;if(!active){buffer.Clear();confirmed=false;}}}
    internal KeyboardTrigger()
    {
        callback=OnKey;
        mouseCallback=OnMouse;
        thread=new Thread(Run){IsBackground=true,Name="Winicode zero key"};
        thread.Start();
        if(!ready.Wait(2000)||hook==0)throw new InvalidOperationException("无法启用 0 键入口。");
    }
    private void Run()
    {
        threadId=GetCurrentThreadId();
        hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
        mouseHook=SetWindowsHookEx(14,mouseCallback,GetModuleHandle(null),0);
        ready.Set();
        if(hook==0)return;
        try { while(GetMessage(out _,0,0,0)>0){} }
        finally {UnhookWindowsHookEx(hook);if(mouseHook!=0)UnhookWindowsHookEx(mouseHook);hook=0;}
    }
    internal void Observe(CandidateAnchor? current)
    {
        lock(gate)
        {
            anchor=current;
            if(current!=null)
            {
                lastCandidate=Environment.TickCount64;
                if(target==current.TargetWindow&&focus==current.TargetFocus)confirmed=true;
            }
            else if(confirmed&&Environment.TickCount64-lastCandidate>250){buffer.Clear();confirmed=false;}
            else if(!confirmed&&Environment.TickCount64-lastKey>1500)buffer.Clear();
        }
    }
    internal void SetEnabled(bool value){lock(gate){enabled=value;if(!value){buffer.Clear();confirmed=false;}}}
    internal string Snapshot(){lock(gate)return confirmed?buffer.Text:"";}
    private static bool Held(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
    private nint OnKey(int code,nint w,nint l)
    {
        if(code<0)return CallNextHookEx(hook,code,w,l);
        var key=Marshal.PtrToStructure<KeyData>(l);
        if(key.extra==Native.InputMarker)return CallNextHookEx(hook,code,w,l);
        bool down=w==0x100||w==0x104;
        bool zero=key.vk is 0x30 or 0x60;
        string? request=null;CandidateAnchor? use=null;bool eat=false;
        lock(gate)
        {
            if(zero&&!down&&suppressZero){suppressZero=false;return 1;}
            if(!down)return CallNextHookEx(hook,code,w,l);
            if(zero&&suppressZero)return 1;
            if(!enabled)return CallNextHookEx(hook,code,w,l);
            var front=Native.GetForegroundWindow();
            var tid=Native.GetWindowThreadProcessId(front,out var pid);
            var now=Environment.TickCount64;
            var currentFocus=Native.ThreadInfo(front).hwndFocus;
            if(front!=target||currentFocus!=focus||(!confirmed&&now-lastKey>1500))
            {buffer.Clear();confirmed=false;target=front;focus=currentFocus;}
            lastKey=now;
            // Only the Simplified Chinese keyboard profile can create this staging text.
            if((pid==Environment.ProcessId&&!Native.IsPracticeWindow(front))||((long)GetKeyboardLayout(tid)&0xffff)!=0x0804||Held(0x11)||Held(0x12)||Held(0x5b)||Held(0x5c))
            {buffer.Clear();confirmed=false;return CallNextHookEx(hook,code,w,l);}
            if(zeroEnabled&&zero&&!Held(0x10)&&anchor?.Target!=null&&anchor.TargetWindow==front&&anchor.TargetFocus==currentFocus&&now-anchor.ObservedAt<600&&buffer.Text.Length>0)
            {request=buffer.Text;use=anchor;suppressZero=true;eat=true;buffer.Clear();confirmed=false;}
            else if(key.vk is >=0x41 and <=0x5a&&!Held(0x10))buffer.Letter((char)(key.vk+32));
            else if(key.vk==0x08)buffer.Backspace();
            else if(key.vk==0x2e)buffer.Delete();
            else if(key.vk==0x25)buffer.Left();
            else if(key.vk==0x27)buffer.Right();
            else if(key.vk==0xde&&!Held(0x10))buffer.Letter('\'');
            else {buffer.Clear();confirmed=false;}
        }
        if(key.vk is >=0x41 and <=0x5a or 0x08)InputObserved?.Invoke();
        if(request!=null&&use!=null)Requested?.Invoke(request,use);
        return eat?1:CallNextHookEx(hook,code,w,l);
    }
    private nint OnMouse(int code,nint w,nint l)
    {
        if(code>=0&&(w==0x201||w==0x204))
        {
            var data=Marshal.PtrToStructure<MouseData>(l);
            if(WindowFromPoint(data.point)!=DockHandle)
            {
                lock(gate)
                {
                    bool arrow=anchor!=null&&!anchor.ButtonBounds.IsEmpty&&anchor.ButtonBounds.Contains(data.point.X,data.point.Y);
                    if(!arrow){buffer.Clear();confirmed=false;}
                }
            }
        }
        return CallNextHookEx(mouseHook,code,w,l);
    }
    public void Dispose(){if(threadId!=0)PostThreadMessage(threadId,0x12,0,0);if(Thread.CurrentThread!=thread)thread.Join(1200);}
}
