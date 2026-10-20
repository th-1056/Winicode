using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Automation;

namespace Winicode;

internal static class Native
{
    internal const nuint InputMarker=0x55444F43;
    private static readonly HashSet<nint> practiceWindows=[];
    internal static void RegisterPracticeWindow(nint handle){lock(practiceWindows)practiceWindows.Add(handle);}
    internal static void UnregisterPracticeWindow(nint handle){lock(practiceWindows)practiceWindows.Remove(handle);}
    internal static bool IsPracticeWindow(nint handle){lock(practiceWindows)return practiceWindows.Contains(handle);}
    internal delegate bool EnumChildProc(nint h,nint p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(nint h,EnumChildProc callback,nint p);
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public readonly int Width => Right-Left; public readonly int Height => Bottom-Top; public readonly Rect ToRect() => new(Left,Top,Width,Height); }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO { public int cbSize; public uint flags; public nint hwndActive,hwndFocus,hwndCapture,hwndMenuOwner,hwndMoveSize,hwndCaret; public RECT rcCaret; }
    [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public int cbSize; public RECT Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)]public KEYBDINPUT ki; [FieldOffset(0)]public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort vk,scan; public uint flags,time; public nuint extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int dx,dy; public uint data,flags,time; public nuint extra; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent,nint after,string? cls,string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(nint h,StringBuilder s,int length);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint h,out RECT r);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint h,out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO info);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtr(nint h,int index,nint value);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] internal static extern nint MonitorFromRect(ref RECT r,uint flags);
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfo(nint h,ref MONITORINFO info);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint h,ref POINT p);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint h,uint msg,nint w,nint l);
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint h,uint msg,nint w,nint l);

    internal static IEnumerable<nint> TopWindows()
    {
        nint h=0; var seen=new HashSet<nint>();
        while((h=FindWindowEx(0,h,null,null))!=0 && seen.Add(h)) yield return h;
    }
    internal static IEnumerable<nint> DescendantWindows(nint parent)
    {
        var seen=new HashSet<nint>();var queue=new Queue<nint>();queue.Enqueue(parent);
        while(queue.Count>0&&seen.Count<256)
        {
            var p=queue.Dequeue(); nint h=0;
            while((h=FindWindowEx(p,h,null,null))!=0&&seen.Add(h))
            {yield return h;queue.Enqueue(h);}
        }
    }
    internal static string ClassName(nint h) { var s=new StringBuilder(256); GetClassName(h,s,s.Capacity); return s.ToString(); }
    internal static string ProcessName(nint h) { try { GetWindowThreadProcessId(h,out var p); return Process.GetProcessById((int)p).ProcessName; } catch { return ""; } }
    internal static GUITHREADINFO ThreadInfo(nint h) { var info=new GUITHREADINFO { cbSize=Marshal.SizeOf<GUITHREADINFO>() }; GetGUIThreadInfo(GetWindowThreadProcessId(h,out _),ref info); return info; }
    internal static void MakeNoActivate(Window window)
    {
        var helper=new WindowInteropHelper(window); var hwnd=helper.EnsureHandle();
        SetWindowLongPtr(hwnd,-20,GetWindowLongPtr(hwnd,-20) | 0x08000000 | 0x00000080);
        HwndSource.FromHwnd(hwnd)?.AddHook((nint h,int msg,nint w,nint l,ref bool handled)=> { if(msg==0x21){handled=true;return 3;}return 0; });
    }
    internal static Rect WorkArea(Rect rect)
    {
        var r=new RECT {Left=(int)rect.Left,Top=(int)rect.Top,Right=(int)rect.Right,Bottom=(int)rect.Bottom};
        var info=new MONITORINFO {cbSize=Marshal.SizeOf<MONITORINFO>()};
        return GetMonitorInfo(MonitorFromRect(ref r,2),ref info)?info.Work.ToRect():SystemParameters.WorkArea;
    }
    internal static bool SendVirtualKey(ushort key,bool markAsHelper=true)
    {
        var marker=markAsHelper?InputMarker:0;
        INPUT[] input=[new(){type=1,u=new(){ki=new(){vk=key,extra=marker}}},new(){type=1,u=new(){ki=new(){vk=key,flags=2,extra=marker}}}];
        return SendInput(2,input,Marshal.SizeOf<INPUT>())==2;
    }
    internal static InputDeliveryResult SendUnicode(string text)
    {
        var input=text.SelectMany(ch=>new INPUT[]{new(){type=1,u=new(){ki=new(){scan=ch,flags=4,extra=InputMarker}}},new(){type=1,u=new(){ki=new(){scan=ch,flags=6,extra=InputMarker}}}}).ToArray();
        var sent=SendInput((uint)input.Length,input,Marshal.SizeOf<INPUT>());
        return new(input.Length,(int)sent,sent==input.Length?0:Marshal.GetLastPInvokeError());
    }
}

internal sealed record InputTarget(nint Window,nint Focus,uint ProcessId,int[] FocusIdentity)
{
    internal static InputTarget? Capture()
    {
        var window=Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(window,out var pid);
        if(window==0 || (pid==Environment.ProcessId&&!Native.IsPracticeWindow(window)))return null;
        var focus=Native.ThreadInfo(window).hwndFocus;
        try
        {
            var element=AutomationElement.FocusedElement;
            if(element.Current.ProcessId!=pid)
            {
                var root=AutomationElement.FromHandle(focus);
                element=root.FindFirst(TreeScope.Subtree,new PropertyCondition(AutomationElement.HasKeyboardFocusProperty,true));
            }
            if(element==null||element.Current.ProcessId!=pid||element.Current.IsPassword)return null;
            return new(window,focus,pid,element.GetRuntimeId());
        }
        catch { return null; }
    }
    internal bool IsValid()
    {
        Native.GetWindowThreadProcessId(Window,out var pid);
        return Native.IsWindow(Window)&&pid==ProcessId&&(Focus==0||Native.IsWindow(Focus));
    }
    internal bool HasNativeFocus() => Native.GetForegroundWindow()==Window && (Focus==0 || Native.ThreadInfo(Window).hwndFocus==Focus);
    internal bool HasFocus()
    {
        if(!HasNativeFocus())return false;
        try { return AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(FocusIdentity); }
        catch { return false; }
    }
    internal bool Restore() => IsValid() && Native.SetForegroundWindow(Window);
}
