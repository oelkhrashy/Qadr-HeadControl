using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Automation;
using Qadr.Core;
using System.IO;

namespace Qadr.Engine;

internal sealed record ElementKey(string Name,string AutomationId,int Type);
internal sealed record Target(string Process,string Window,ElementKey[] Path,double X,double Y);
internal sealed record RoutineStep(string Kind,Target Target,double Delay,string? Value=null,string Button="left");
internal sealed record Routine(string Name,RoutineStep[] Steps);

internal sealed class RoutineService : IDisposable
{
    readonly Storage storage;
    readonly SafetyGate gate;
    readonly Action<string> done;
    readonly int shellPid;
    readonly List<RoutineStep> steps=[];
    readonly object sync=new();
    readonly CancellationTokenSource closing=new();
    CancellationTokenSource? playback;
    Task? playTask;
    readonly ManualResetEventSlim confirm=new();
    readonly Channel<(int Kind,int X,int Y,int Generation)> events=Channel.CreateBounded<(int,int,int,int)>(64);
    int recordingGeneration;
    Thread? hookThread;
    uint threadId;
    nint mouseHook,keyHook;
    Hook? mouseProc,keyProc;
    string? recordingName;
    bool keyboardEnabled;
    string[] names=[];
    double recordedAt,lastEvent;
    public event Action<string>? ConfirmationRequired;
    public bool WaitingForConfirmation {get;private set;}
    public string[] Names=>Volatile.Read(ref names);
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct MouseData {public Point Point;public uint Data,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)] struct Message {public nint Window;public uint Id;public nuint WParam;public nint LParam;public uint Time;public Point Point;public uint Private;}
    delegate nint Hook(int code,nuint message,nint data);
    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int id,Hook proc,nint module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook,int code,nuint message,nint data);
    [DllImport("user32.dll")] static extern int GetMessage(out Message message,nint window,uint min,uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread,uint message,nuint w,nint l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandle(string? module);
    public RoutineService(Storage storage,SafetyGate gate,Action<string> done,string? shellPid)
    {
        this.storage=storage;this.gate=gate;this.done=done;int.TryParse(shellPid,out this.shellPid);
        names=storage.Names("routines");
        _=Task.Run(Collect);
    }
    static string Name(string name)
    {
        name=Regex.Replace(name.Trim().ToLowerInvariant(),"\\s+"," ");
        if(!Regex.IsMatch(name,"^[a-z][a-z0-9 ]{0,31}$"))throw new ArgumentException("Use a routine name with 1–32 English letters, digits and spaces.");return name;
    }
    public void StartRecording(string name,bool keyboard)
    {
        if(playTask is {IsCompleted:false})throw new InvalidOperationException("Stop the current routine before recording another.");
        Cancel();lock(sync){recordingName=Name(name);steps.Clear();keyboardEnabled=keyboard;recordedAt=lastEvent=Arguments.Now;}
        if(hookThread is null){hookThread=new Thread(Hooks){IsBackground=true,Name="Qadr recording hooks"};hookThread.Start();}
    }
    void Hooks()
    {
        threadId=GetCurrentThreadId();
        mouseProc=(code,message,data)=>{
            if(code>=0 && (message==0x202 || message==0x205) && Volatile.Read(ref recordingName) is not null){var m=Marshal.PtrToStructure<MouseData>(data);events.Writer.TryWrite((message==0x202?0:1,m.Point.X,m.Point.Y,Volatile.Read(ref recordingGeneration)));}
            return CallNextHookEx(mouseHook,code,message,data);
        };
        keyProc=(code,message,data)=>{
            if(code>=0 && message==0x101 && Volatile.Read(ref recordingName) is not null && keyboardEnabled){int vk=Marshal.ReadInt32(data);if(vk!=0x75 && vk!=0x1b)events.Writer.TryWrite((2,0,0,Volatile.Read(ref recordingGeneration)));}
            return CallNextHookEx(keyHook,code,message,data);
        };
        mouseHook=SetWindowsHookEx(14,mouseProc,GetModuleHandle(null),0);keyHook=SetWindowsHookEx(13,keyProc,GetModuleHandle(null),0);
        if(mouseHook==0 || keyHook==0){lock(sync)recordingName=null;return;}
        try{while(GetMessage(out _,0,0,0)>0){}}finally{UnhookWindowsHookEx(mouseHook);UnhookWindowsHookEx(keyHook);}
    }
    async Task Collect()
    {
        try {
            await foreach(var evt in events.Reader.ReadAllAsync(closing.Token)) {
                if(recordingName is null || evt.Generation!=Volatile.Read(ref recordingGeneration))continue;
                try {
                    AutomationElement element=evt.Kind==2?AutomationElement.FocusedElement:AutomationElement.FromPoint(new System.Windows.Point(evt.X,evt.Y));
                    if(element.Current.ProcessId==shellPid || element.Current.ProcessId==Environment.ProcessId)continue;
                    bool password=element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty,true) is not bool b || b;
                    if(password)continue;
                    Target? target=Selector(element,evt.X,evt.Y);if(target is null)continue;
                    string kind="click";string? value=null;
                    if(evt.Kind==2) {
                        if(element.Current.ControlType!=ControlType.Edit || !element.TryGetCurrentPattern(ValuePattern.Pattern,out object? pattern) || pattern is not ValuePattern vp || vp.Current.IsReadOnly)continue;
                        kind="value";value=vp.Current.Value;if(value.Length>2048)continue;
                    } else if(evt.Kind==0 && element.TryGetCurrentPattern(InvokePattern.Pattern,out _))kind="invoke";
                    double now=Arguments.Now;
                    lock(sync) {
                        if(recordingName is null || evt.Generation!=recordingGeneration)continue;
                        if(now-recordedAt>120 || steps.Count>=1000){keyboardEnabled=false;continue;}
                        var step=new RoutineStep(kind,target,Math.Clamp(now-lastEvent,0,10),value,evt.Kind==1?"right":"left");
                        if(kind=="value" && steps.Count>0 && steps[^1].Kind=="value" && Protocol.Encode(steps[^1].Target)==Protocol.Encode(target))steps[^1]=step;else steps.Add(step);
                        lastEvent=now;
                    }
                }catch(Exception e) when(e is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){ }
            }
        }catch(OperationCanceledException){ }
    }
    static Target? Selector(AutomationElement element,int x,int y)
    {
        var path=new List<ElementKey>();AutomationElement? current=element,window=null;
        for(int i=0;i<12 && current is not null;i++) {
            if(current.Current.ControlType==ControlType.Window){window=current;break;}
            path.Add(new(current.Current.Name,current.Current.AutomationId,current.Current.ControlType.Id));current=TreeWalker.ControlViewWalker.GetParent(current);
        }
        if(window is null || path.Count==0)return null;
        var rect=window.Current.BoundingRectangle;if(rect.IsEmpty || rect.Width<=0 || rect.Height<=0)return null;
        string process=Process.GetProcessById(window.Current.ProcessId).ProcessName;
        if(process.StartsWith("Qadr",StringComparison.OrdinalIgnoreCase))return null;
        return new(process,window.Current.Name,path.ToArray(),Math.Clamp((x-rect.X)/rect.Width,0,1),Math.Clamp((y-rect.Y)/rect.Height,0,1));
    }
    public void SaveRecording()
    {
        lock(sync){string name=recordingName??throw new InvalidOperationException("Recording is not active");recordingName=null;
            if(steps.Count==0)throw new InvalidOperationException("No supported actions were recorded. Try a standard Windows application.");
            storage.Save("routines/"+name+".json",new Routine(name,steps.ToArray()));Volatile.Write(ref names,storage.Names("routines"));}
    }
    Routine Load(string name)
    {
        var routine=storage.Load<Routine>("routines/"+Name(name)+".json")??throw new InvalidDataException("Routine is missing or damaged");
        if(routine.Steps.Length is <1 or >1000 || routine.Steps.Any(s=>s.Kind is not ("invoke" or "value" or "click") || !double.IsFinite(s.Delay) || s.Delay is <0 or >10 || s.Target.Path.Length is <1 or >12 || !double.IsFinite(s.Target.X) || !double.IsFinite(s.Target.Y) || s.Target.X is <0 or >1 || s.Target.Y is <0 or >1 || s.Value?.Length>2048))throw new InvalidDataException("Routine contains invalid steps");
        return routine;
    }
    public string Describe(string name)
    {
        var r=Load(name);return string.Join("; ",r.Steps.Take(12).Select((s,i)=>$"{i+1}. {s.Kind} {s.Target.Path[0].Name} in {s.Target.Process}"))+(r.Steps.Length>12?$"; and {r.Steps.Length-12} more steps":"");
    }
    public void Run(string name,bool noInput,int expectedUserStops)
    {
        if(playTask is {IsCompleted:false})throw new InvalidOperationException("The previous routine is still stopping. Try again in a moment.");
        var routine=Load(name);Cancel();playback=new();var token=playback.Token;
        playTask=Task.Run(()=>{
            string outcome="Routine cancelled";
            try {
                if(gate.UserStops!=expectedUserStops)return;
                if(gate.Stopped){if(!gate.RequestResume(expectedUserStops))return;long started=Environment.TickCount64;while(gate.Stopped && Environment.TickCount64-started<500){if(token.WaitHandle.WaitOne(10))return;}}
                foreach(var step in routine.Steps) {
                    if(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(step.Delay)) || gate.Stopped || gate.UserStops!=expectedUserStops)return;
                    var (window,element)=Find(step.Target);
                    if(window is null || element is null)throw new InvalidOperationException("A routine target is missing or ambiguous. Qadr stopped before acting. Review or record this step again.");
                    string label=step.Target.Path[0].Name;
                    if(Regex.IsMatch(label,"\\b(send|delete|pay|purchase|buy|submit|confirm order)\\b|إرسال|ارسال|حذف|دفع|شراء|تأكيد الطلب",RegexOptions.IgnoreCase)) {
                        WaitingForConfirmation=true;confirm.Reset();ConfirmationRequired?.Invoke("Confirm step: "+label+" in "+step.Target.Process);
                        if(!confirm.Wait(TimeSpan.FromSeconds(30),token) || gate.Stopped)return;WaitingForConfirmation=false;
                    }
                    if(noInput)continue;
                    if(step.Kind=="invoke"){
                        if(!element!.TryGetCurrentPattern(InvokePattern.Pattern,out object? invoke))throw new InvalidOperationException("The routine target no longer supports its recorded action.");
                        if(!NativeInput.WithGate(gate,()=>((InvokePattern)invoke).Invoke()))throw new InvalidOperationException("Control stopped before invoking the routine target.");
                    }
                    else if(step.Kind=="value") {
                        if(element!.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty,true) is not false || !element.TryGetCurrentPattern(ValuePattern.Pattern,out object? value))throw new InvalidOperationException("Text entry is not permitted in this field.");
                        if(!NativeInput.WithGate(gate,()=>((ValuePattern)value).SetValue(step.Value??"")))throw new InvalidOperationException("Control stopped before entering routine text.");
                    } else {
                        var rect=element!.Current.BoundingRectangle;
                        if(window.Current.IsOffscreen || element.Current.IsOffscreen || rect.IsEmpty || rect.Width<=0 || rect.Height<=0)throw new InvalidOperationException("The routine target is not visible.");
                        if(!NativeInput.Move(gate,rect.X+rect.Width/2,rect.Y+rect.Height/2) || !NativeInput.Button(gate,step.Button,true) || !NativeInput.Button(gate,step.Button,false))throw new InvalidOperationException("Windows could not complete the routine click. Control stopped.");
                    }
                }
                outcome=noInput?"Routine preview finished (input disabled)":"Routine finished";
            }catch(OperationCanceledException){ }
            catch(Exception e){outcome="Routine stopped: "+e.Message;}
            finally{WaitingForConfirmation=false;gate.Stop();done(outcome);}
        });
    }
    static (AutomationElement?,AutomationElement?) Find(Target target)
    {
        var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Window));
        var matches=new List<AutomationElement>();
        foreach(AutomationElement w in windows)try{if(w.Current.Name==target.Window && Process.GetProcessById(w.Current.ProcessId).ProcessName==target.Process)matches.Add(w);}catch(ArgumentException){ }
        if(matches.Count!=1)return(null,null);
        var window=matches[0];var key=target.Path[0];var condition=new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.LookupById(key.Type)),
            new PropertyCondition(AutomationElement.NameProperty,key.Name),new PropertyCondition(AutomationElement.AutomationIdProperty,key.AutomationId));
        var candidates=window.FindAll(TreeScope.Descendants,condition);var selected=new List<AutomationElement>();
        foreach(AutomationElement e in candidates){var at=e;bool same=true;for(int i=0;i<target.Path.Length;i++){if(at is null || at.Current.Name!=target.Path[i].Name || at.Current.AutomationId!=target.Path[i].AutomationId || at.Current.ControlType.Id!=target.Path[i].Type){same=false;break;}at=TreeWalker.ControlViewWalker.GetParent(at);}if(same)selected.Add(e);}
        return(window,selected.Count==1?selected[0]:null);
    }
    public void ConfirmStep()=>confirm.Set();
    public void Cancel(){lock(sync){recordingName=null;Interlocked.Increment(ref recordingGeneration);}playback?.Cancel();}
    public void Dispose(){Cancel();closing.Cancel();if(threadId!=0)PostThreadMessage(threadId,0x12,0,0);hookThread?.Join(200);closing.Dispose();}
}
