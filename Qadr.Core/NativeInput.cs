using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Qadr.Core;

[SupportedOSPlatform("windows")]
public static class NativeInput
{
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Data { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Data Data; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int n);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(nint context);
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    public static (int X,int Y,int Width,int Height) MonitorBounds(long window){
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(GetMonitorInfoW(MonitorFromWindow((nint)window,2),ref info))return(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top);
        return(0,0,GetSystemMetrics(0),GetSystemMetrics(1));
    }
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint process);
    public static uint WindowOwner(long window){GetWindowThreadProcessId((nint)window,out uint process);return process;}
    public static long WindowAt(Point point)=>GetAncestor(WindowFromPoint(point),2).ToInt64();
    public static void EnablePhysicalPixels()=>SetProcessDpiAwarenessContext(-4);
    public static Point Cursor { get { GetCursorPos(out var p); return p; } }
    public static (int X,int Y,int Width,int Height) Desktop => (GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79));
    public static bool Move(SafetyGate gate, double x, double y) => Inject(gate, MouseEvent(0x8000 | 0x4000 | 1, x, y));
    public static bool Button(SafetyGate gate, string button, bool down)
    {
        var (mask,flag,data)=button switch {
            "left"=>(1,down?2u:4u,0u),"right"=>(2,down?8u:16u,0u),"middle"=>(4,down?32u:64u,0u),
            "back"=>(8,down?0x80u:0x100u,1u),"forward"=>(16,down?0x80u:0x100u,2u),_=>(0,0u,0u)};
        return mask!=0&&Inject(gate, MouseEvent(flag,data:data), () => gate.MouseButtons = down ? gate.MouseButtons | mask : gate.MouseButtons & ~mask);
    }
    public static bool Scroll(SafetyGate gate, int amount) => Inject(gate, MouseEvent(0x800, data: unchecked((uint)(amount * 120))));
    public static bool ScrollHorizontal(SafetyGate gate,int amount)=>Inject(gate,MouseEvent(0x1000,data:unchecked((uint)(amount*120))));
    public static bool Key(SafetyGate gate, ushort vk, bool down) => Inject(gate, new Input { Type=1, Data=new Data {Keyboard=new Keyboard {Vk=vk,Flags=down?0u:2u}} }, () => gate.SetKey(vk,down));
    static Input MouseEvent(uint flags,double x=0,double y=0,uint data=0)
    {
        var d=Desktop; int px=Math.Clamp((int)Math.Round(x),d.X,d.X+d.Width-1), py=Math.Clamp((int)Math.Round(y),d.Y,d.Y+d.Height-1);
        return new() {Type=0,Data=new Data {Mouse=new Mouse {X=(int)Math.Round((px-d.X)*65535.0/Math.Max(1,d.Width-1)),Y=(int)Math.Round((py-d.Y)*65535.0/Math.Max(1,d.Height-1)),Flags=flags,Data=data}}};
    }
    static bool Inject(SafetyGate gate,Input input,Action? own=null)
    {
        bool locked=false;
        try {
            try { locked=gate.InjectionMutex.WaitOne(10); } catch(AbandonedMutexException) { locked=true; gate.Stop(); }
            if(!locked || gate.Stopped || gate.Hotkeys==0 || Environment.TickCount64-gate.Heartbeat>250) return false;
            // Ownership is written before injection; Guardian can release it after a crash.
            own?.Invoke(); return SendInput(1,[input],Marshal.SizeOf<Input>())==1;
        } finally {if(locked) gate.InjectionMutex.ReleaseMutex();}
    }
    public static bool WithGate(SafetyGate gate,Action action)
    {
        bool locked=false;
        try {
            try { locked=gate.InjectionMutex.WaitOne(10); } catch(AbandonedMutexException) {locked=true;gate.Stop();}
            if(!locked || gate.Stopped || gate.Hotkeys==0) return false;
            action(); return true;
        } finally {if(locked)gate.InjectionMutex.ReleaseMutex();}
    }
    public static void ReleaseOwned(SafetyGate gate)
    {
        int held=gate.MouseButtons; gate.MouseButtons=0;
        if((held&1)!=0) SendInput(1,[MouseEvent(4)],Marshal.SizeOf<Input>());
        if((held&2)!=0) SendInput(1,[MouseEvent(16)],Marshal.SizeOf<Input>());
        if((held&4)!=0) SendInput(1,[MouseEvent(64)],Marshal.SizeOf<Input>());
        if((held&8)!=0) SendInput(1,[MouseEvent(0x100,data:1)],Marshal.SizeOf<Input>());
        if((held&16)!=0) SendInput(1,[MouseEvent(0x100,data:2)],Marshal.SizeOf<Input>());
        for(ushort vk=0;vk<256;vk++) if(gate.KeyOwned(vk)) {
            gate.SetKey(vk,false); SendInput(1,[new Input {Type=1,Data=new Data {Keyboard=new Keyboard {Vk=vk,Flags=2}}}],Marshal.SizeOf<Input>());
        }
    }
}
