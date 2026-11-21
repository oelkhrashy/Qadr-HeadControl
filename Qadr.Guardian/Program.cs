using System.Diagnostics;
using System.Runtime.InteropServices;
using Qadr.Core;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

namespace Qadr.Guardian;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct Message {public nint Window;public uint Id;public nuint WParam;public nint LParam;public uint Time;public Point Point;public uint Private;}
    [DllImport("user32.dll")] static extern bool RegisterHotKey(nint window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(nint window,int id);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Message message,nint window,uint min,uint max,uint remove);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(nint job,int info,nint buffer,uint length);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(nint job,nint process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    static int Main(string[] args)
    {
        string session=Arguments.Get(args,"--session")??throw new ArgumentException("Session required");
        string directory=Arguments.DataDirectory(args); Directory.CreateDirectory(directory);
        string stopFile=Path.Combine(directory,"stopped.flag"); bool persistedStop=File.Exists(stopFile);
        using var gate=new SafetyGate(session,true);
        bool escape=RegisterHotKey(0,1,0x4000,VoiceControl.StopVirtualKey), secondary=RegisterHotKey(0,2,0x4003,0x1b), pause=RegisterHotKey(0,3,0x4000,0x75);
        gate.Hotkeys=(escape?1:0)|(secondary?2:0);
        Process? engine=null; nint job=0; long start=Environment.TickCount64, stopAt=start, observed=gate.Generation;
        void Persist() {try{File.WriteAllText(stopFile,"Deliberate resume required");}catch(IOException){}}
        void Stop() {gate.UserStop();stopAt=Environment.TickCount64;Persist();}
        void Release() {
            bool locked=false;
            try {try{locked=gate.InjectionMutex.WaitOne(10);}catch(AbandonedMutexException){locked=true;}
                if(locked)NativeInput.ReleaseOwned(gate);
            } finally {if(locked)gate.InjectionMutex.ReleaseMutex();}
        }
        try {
            if(!args.Contains("--test")) {
                var info=new ProcessStartInfo(Arguments.Get(args,"--engine")??Path.Combine(AppContext.BaseDirectory,"Qadr.Engine.exe")) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
                foreach(string s in new[]{"--session",session,"--data-dir",directory,"--shell-pid",Arguments.Get(args,"--shell-pid")??"0"})info.ArgumentList.Add(s);
                if(!persistedStop)info.ArgumentList.Add("--initial-arm");
                if(args.Contains("--no-input"))info.ArgumentList.Add("--no-input");
                if(args.Contains("--test-source"))info.ArgumentList.Add("--test-source");
                if(args.Contains("--no-input")&&args.Contains("--test-source")&&args.Contains("--diagnostic-control"))info.ArgumentList.Add("--diagnostic-control");
                if(args.Contains("--no-input")&&args.Contains("--test-source")&&args.Contains("--launch-assessment-check"))info.ArgumentList.Add("--launch-assessment-check");
                if(args.Contains("--no-input")&&args.Contains("--test-source")&&args.Contains("--diagnostic-control")&&args.Contains("--tuning-test-evidence"))info.ArgumentList.Add("--tuning-test-evidence");
                if(args.Contains("--offer-tutorial"))info.ArgumentList.Add("--offer-tutorial");
                string? python=Arguments.Get(args,"--python");if(python is not null){info.ArgumentList.Add("--python");info.ArgumentList.Add(python);}
                engine=Process.Start(info)??throw new IOException("Engine could not start");
                // A Windows job closes the Engine and all its descendants when Guardian exits.
                job=CreateJobObject(0,null); nint limits=Marshal.AllocHGlobal(144);
                try {for(int i=0;i<144;i++)Marshal.WriteByte(limits,i,0);Marshal.WriteInt32(limits,16,0x2000);
                    if(job==0 || !SetInformationJobObject(job,9,limits,144) || !AssignProcessToJobObject(job,engine.Handle))throw new IOException("Process supervision could not start");
                } finally {Marshal.FreeHGlobal(limits);}
            }
            while(gate.Closing==0 && (engine is null || !engine.HasExited)) {
                while(PeekMessage(out var msg,0,0,0,1)) if(msg.Id==0x312) {
                    if(msg.WParam==1 || msg.WParam==2)Stop(); else if(msg.WParam==3)gate.PauseRequest=1;
                }
                long now=Environment.TickCount64;
                if(gate.Generation!=observed){observed=gate.Generation;stopAt=now;Persist();}
                if(engine is not null && ((gate.Heartbeat!=0 && now-gate.Heartbeat>250) || (gate.Heartbeat==0 && now-start>30000))) {
                    Stop(); engine.Kill(true); Release(); break;
                }
                if(gate.Stopped) {
                    Release();
                    if(engine is not null && gate.Heartbeat!=0 && gate.Acknowledged<gate.Generation && now-stopAt>250){engine.Kill(true);Release();break;}
                }
                if(gate.ResumeRequest!=0 && gate.ResumeGeneration==gate.Generation&&gate.ResumeUserStops==gate.UserStops && gate.Hotkeys!=0 && gate.Acknowledged==gate.Generation && gate.Heartbeat!=0 && now-gate.Heartbeat<100 && gate.Closing==0) {
                    bool locked=false,resumed=false;
                    try{
                        try{locked=gate.InjectionMutex.WaitOne(5);}catch(AbandonedMutexException){locked=true;Stop();}
                        if(locked&&gate.ResumeRequest!=0&&gate.ResumeGeneration==gate.Generation&&gate.ResumeUserStops==gate.UserStops&&gate.Acknowledged==gate.Generation&&now-gate.Heartbeat<100&&gate.Closing==0){
                            long permit=gate.ResumeGeneration;gate.ResumeRequest=0;gate.Stopped=false;
                            // Stop advances generation before setting Stopped.
                            // Recheck while injection is excluded, so an older
                            // resume cannot overwrite a concurrent stop.
                            if(gate.Generation!=permit)gate.Stopped=true;else resumed=true;
                        }
                    }finally{if(locked)gate.InjectionMutex.ReleaseMutex();}
                    if(resumed)try{File.Delete(stopFile);}catch(IOException){Stop();}
                }
                Thread.Sleep(10);
            }
            Stop();Release();return 0;
        } catch(Exception e) {Stop();Release();File.WriteAllText(Path.Combine(directory,"guardian-error.txt"),e.Message);return 1;}
        finally {
            if(engine is {HasExited:false})engine.Kill(true);
            if(job!=0)CloseHandle(job);engine?.Dispose();
            if(escape)UnregisterHotKey(0,1);if(secondary)UnregisterHotKey(0,2);if(pause)UnregisterHotKey(0,3);
        }
    }
}
