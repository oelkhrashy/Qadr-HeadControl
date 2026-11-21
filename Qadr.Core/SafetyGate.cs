using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace Qadr.Core;

[SupportedOSPlatform("windows")]
public sealed unsafe class SafetyGate : IDisposable
{
    readonly MemoryMappedFile memory;
    readonly MemoryMappedViewAccessor view;
    byte* pointer;
    public Mutex InjectionMutex { get; }
    public SafetyGate(string session, bool create)
    {
        memory = create ? MemoryMappedFile.CreateNew(session + ".gate", 512) : MemoryMappedFile.OpenExisting(session + ".gate");
        view = memory.CreateViewAccessor(); view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        InjectionMutex = new(false, session + ".injection");
        if (create) { Stopped = true; Hotkeys = 0; Heartbeat = 0; ShellHeartbeat = 0; Generation = 1; Acknowledged = 0; }
    }
    int ReadInt(int offset) => Volatile.Read(ref *(int*)(pointer + offset));
    void WriteInt(int offset, int n) => Volatile.Write(ref *(int*)(pointer + offset), n);
    long ReadLong(int offset) => Interlocked.Read(ref *(long*)(pointer + offset));
    void WriteLong(int offset, long n) => Interlocked.Exchange(ref *(long*)(pointer + offset), n);
    public bool Stopped { get => ReadInt(0) != 0; set => WriteInt(0, value ? 1 : 0); }
    public int Hotkeys { get => ReadInt(4); set => WriteInt(4, value); }
    public long Heartbeat { get => ReadLong(8); set => WriteLong(8, value); }
    public long Generation { get => ReadLong(16); private set => WriteLong(16, value); }
    public long Acknowledged { get => ReadLong(24); set => WriteLong(24, value); }
    public long ShellHeartbeat { get => ReadLong(32); set => WriteLong(32, value); }
    public int ResumeRequest { get => ReadInt(40); set {if(value!=0)RequestResume(UserStops);else WriteInt(40,0);} }
    public long ResumeGeneration=>ReadLong(96);
    public int ResumeUserStops=>ReadInt(104);
    public int UserStops=>ReadInt(60);
    public bool RequestResume(int expectedUserStops){if(UserStops!=expectedUserStops)return false;WriteInt(104,expectedUserStops);WriteLong(96,Generation);WriteInt(40,1);return true;}
    public int MouseButtons { get => ReadInt(44); set => WriteInt(44, value); }
    public int PauseRequest { get => ReadInt(48); set => WriteInt(48, value); }
    public int Closing { get => ReadInt(52); set => WriteInt(52, value); }
    public int SharedRpc { get => ReadInt(56); set => WriteInt(56, value); }
    public bool KeyOwned(int vk) => ReadInt(64 + (vk / 32) * 4) is var mask && (mask & (1 << (vk % 32))) != 0;
    public void SetKey(int vk, bool down) { int off = 64 + (vk / 32) * 4, mask = ReadInt(off); WriteInt(off, down ? mask | (1 << (vk % 32)) : mask & ~(1 << (vk % 32))); }
    public void Stop() { ResumeRequest = 0; Interlocked.Increment(ref *(long*)(pointer + 16)); Stopped = true; }
    public void UserStop(){Interlocked.Increment(ref *(int*)(pointer+60));Stop();}
    public void Dispose() { view.SafeMemoryMappedViewHandle.ReleasePointer(); pointer = null; view.Dispose(); memory.Dispose(); InjectionMutex.Dispose(); }
}

public static class Arguments
{
    public static string? Get(string[] args, string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    public static string DataDirectory(string[] args) => Get(args, "--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qadr");
    public static double Now => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
}
