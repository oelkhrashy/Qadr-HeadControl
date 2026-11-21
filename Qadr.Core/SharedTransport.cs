using System.IO.MemoryMappedFiles;
using System.Text;
using System.Runtime.Versioning;

namespace Qadr.Core;

/// <summary>Bounded local RPC fallback for Windows environments that deny named pipes.</summary>
[SupportedOSPlatform("windows")]
public sealed class SharedTransport : IDisposable
{
    const int Slots=32,SlotSize=8192,StatusSize=1500000;
    readonly MemoryMappedFile requests,status;
    readonly MemoryMappedViewAccessor requestView,statusView;
    readonly Mutex requestLock;
    long sequence;
    public SharedTransport(string session,bool create)
    {
        requests=create?MemoryMappedFile.CreateNew(session+".commands",64+Slots*SlotSize):MemoryMappedFile.OpenExisting(session+".commands");
        status=create?MemoryMappedFile.CreateNew(session+".status",64+StatusSize):MemoryMappedFile.OpenExisting(session+".status");
        requestView=requests.CreateViewAccessor();statusView=status.CreateViewAccessor();requestLock=new(false,session+".commands-lock");
    }
    public bool Send(Command command)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(Protocol.Encode(command));if(bytes.Length>SlotSize-4)return false;
        bool locked=false;
        try {
            try{locked=requestLock.WaitOne(5);}catch(AbandonedMutexException){locked=true;}
            if(!locked)return false;long head=requestView.ReadInt64(0),tail=requestView.ReadInt64(8);
            if(head-tail>=Slots)return false;
            long offset=64+(head%Slots)*SlotSize;requestView.WriteArray(offset+4,bytes,0,bytes.Length);requestView.Write(offset,bytes.Length);requestView.Write(0,head+1);return true;
        }finally{if(locked)requestLock.ReleaseMutex();}
    }
    public Command? Receive()
    {
        bool locked=false;
        try {
            try{locked=requestLock.WaitOne(5);}catch(AbandonedMutexException){locked=true;}
            if(!locked)return null;long head=requestView.ReadInt64(0),tail=requestView.ReadInt64(8);if(tail>=head)return null;
            long offset=64+(tail%Slots)*SlotSize;int count=requestView.ReadInt32(offset);
            if(count<=0||count>SlotSize-4)throw new InvalidDataException("Invalid command buffer");
            byte[] bytes=new byte[count];requestView.ReadArray(offset+4,bytes,0,count);requestView.Write(8,tail+1);
            var cmd=Protocol.Decode<Command>(Encoding.UTF8.GetString(bytes));if(cmd.Version!=1)throw new InvalidDataException("Protocol mismatch");return cmd;
        }finally{if(locked)requestLock.ReleaseMutex();}
    }
    public void Publish(EngineStatus message)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(Protocol.Encode(message));if(bytes.Length>StatusSize)throw new InvalidDataException("Status buffer exceeded");
        statusView.Write(0,++sequence);Thread.MemoryBarrier();statusView.Write(8,bytes.Length);statusView.WriteArray(64,bytes,0,bytes.Length);Thread.MemoryBarrier();statusView.Write(0,++sequence);
    }
    public EngineStatus? Read(ref long previous)
    {
        long seq=statusView.ReadInt64(0);if(seq==previous||seq<=0||(seq&1)!=0)return null;int length=statusView.ReadInt32(8);
        if(length<=0||length>StatusSize)return null;byte[] bytes=new byte[length];statusView.ReadArray(64,bytes,0,length);
        Thread.MemoryBarrier();if(statusView.ReadInt64(0)!=seq)return null;previous=seq;var message=Protocol.Decode<EngineStatus>(Encoding.UTF8.GetString(bytes));
        return message.Version==1?message:throw new InvalidDataException("Protocol mismatch");
    }
    public void Dispose(){requestView.Dispose();statusView.Dispose();requests.Dispose();status.Dispose();requestLock.Dispose();}
}
