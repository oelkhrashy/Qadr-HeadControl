using System.Runtime.InteropServices;
using Qadr.Core;
using System.IO;

namespace Qadr.Engine;

/// <summary>Desktop Media Foundation capture without a WinRT permission broker.</summary>
internal sealed class MediaFoundation : IDisposable
{
    static readonly Guid SourceType=new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3"),VideoCapture=new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f"),
        Symbolic=new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f"),Friendly=new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01"),
        MajorType=new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"),Video=new("73646976-0000-0010-8000-00aa00389b71"),
        Subtype=new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),Rgb32=new("00000016-0000-0010-8000-00aa00389b71"),
        Size=new("1652c33d-d6b2-4012-b834-72030849a37d"),Stride=new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6"),
        Processing=new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d"),FrameRate=new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"),LowLatency=new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    const uint FirstVideo=0xfffffffc;
    nint source,reader;
    readonly object sourceLock=new();
    readonly Thread thread;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Action<byte[],int,int> frame;
    readonly Action<string> fail;
    readonly int targetFps;
    public double NominalFps {get;private set;}
    volatile bool stopping;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint Release(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GuidValue(nint self,in Guid key,in Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StringValue(nint self,in Guid key,[MarshalAs(UnmanagedType.LPWStr)]string value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int UIntValue(nint self,in Guid key,uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int LongValue(nint self,in Guid key,ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetString(nint self,in Guid key,out nint value,out uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetLong(nint self,in Guid key,out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetUInt(nint self,in Guid key,out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Select(nint self,uint index,[MarshalAs(UnmanagedType.Bool)]bool enabled);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetType(nint self,uint index,nint reserved,nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CurrentType(nint self,uint index,out nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NativeType(nint self,uint index,uint typeIndex,out nint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Read(nint self,uint index,uint control,out uint stream,out uint flags,out long stamp,out nint sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Buffer(nint self,out nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int LockBuffer(nint self,out nint bytes,out uint maximum,out uint current);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Simple(nint self);
    [DllImport("mfplat.dll")] static extern int MFStartup(uint version,uint flags);
    [DllImport("mfplat.dll")] static extern int MFShutdown();
    [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out nint attributes,uint count);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out nint type);
    [DllImport("mf.dll")] static extern int MFEnumDeviceSources(nint attributes,out nint devices,out uint count);
    [DllImport("mf.dll")] static extern int MFCreateDeviceSource(nint attributes,out nint source);
    [DllImport("mfreadwrite.dll")] static extern int MFCreateSourceReaderFromMediaSource(nint source,nint attributes,out nint reader);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(nint reserved,uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    static T Method<T>(nint obj,int index) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),index*IntPtr.Size));
    static void Drop(nint obj){if(obj!=0)Method<Release>(obj,2)(obj);}
    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    static string String(nint obj,Guid key){Check(Method<GetString>(obj,13)(obj,key,out var text,out var length));try{return Marshal.PtrToStringUni(text,(int)length)??"Camera";}finally{Marshal.FreeCoTaskMem(text);}}
    public static CameraDevice[] Discover()
    {
        Check(CoInitializeEx(0,0));Check(MFStartup(0x20070,0));nint attributes=0,devices=0;uint count=0;
        try {
            Check(MFCreateAttributes(out attributes,1));Check(Method<GuidValue>(attributes,24)(attributes,SourceType,VideoCapture));
            Check(MFEnumDeviceSources(attributes,out devices,out count));var result=new List<CameraDevice>();
            for(int i=0;i<count;i++){nint device=Marshal.ReadIntPtr(devices,i*IntPtr.Size);result.Add(new(String(device,Symbolic),String(device,Friendly)));}
            return result.ToArray();
        }finally{for(int i=0;i<count;i++)Drop(Marshal.ReadIntPtr(devices,i*IntPtr.Size));if(devices!=0)Marshal.FreeCoTaskMem(devices);Drop(attributes);MFShutdown();CoUninitialize();}
    }
    public MediaFoundation(string id,Action<byte[],int,int> frame,Action<string> fail,int targetFps=30)
    {
        this.frame=frame;this.fail=fail;this.targetFps=targetFps;thread=new Thread(()=>Capture(id)){IsBackground=true,Name="Qadr Media Foundation capture"};thread.Start();
    }
    public Task Ready=>ready.Task;
    void Capture(string id)
    {
        nint attributes=0,options=0,type=0;bool started=false,initialized=false;
        try {
            Check(CoInitializeEx(0,0));initialized=true;Check(MFStartup(0x20070,0));started=true;
            Check(MFCreateAttributes(out attributes,2));Check(Method<GuidValue>(attributes,24)(attributes,SourceType,VideoCapture));Check(Method<StringValue>(attributes,25)(attributes,Symbolic,id));
            lock(sourceLock)Check(MFCreateDeviceSource(attributes,out source));Check(MFCreateAttributes(out options,2));Check(Method<UIntValue>(options,21)(options,Processing,1));Check(Method<UIntValue>(options,21)(options,LowLatency,1));
            Check(MFCreateSourceReaderFromMediaSource(source,options,out reader));Check(Method<Select>(reader,4)(reader,0xfffffffe,false));Check(Method<Select>(reader,4)(reader,FirstVideo,true));
            nint chosen=0;double chosenScore=double.NegativeInfinity;ulong chosenSize=0,chosenRate=0;
            for(uint i=0;i<256;i++) {
                int hr=Method<NativeType>(reader,5)(reader,FirstVideo,i,out nint candidate);if(hr<0)break;
                if(Method<GetLong>(candidate,8)(candidate,Size,out ulong dimensions)>=0){uint w=(uint)(dimensions>>32),h=(uint)dimensions;
                    double fps=0;
                    if(Method<GetLong>(candidate,8)(candidate,FrameRate,out ulong frameRate)>=0&&(uint)frameRate!=0)fps=(double)(frameRate>>32)/(uint)frameRate;
                    double score=CaptureMode.Score(w,h,fps,targetFps);
                    if(score>chosenScore){Drop(chosen);chosen=candidate;chosenScore=score;chosenSize=dimensions;chosenRate=fps>0?frameRate:0;continue;}}
                Drop(candidate);
            }
            if(chosen!=0){try{Check(Method<SetType>(reader,7)(reader,FirstVideo,0,chosen));}finally{Drop(chosen);}}
            Check(MFCreateMediaType(out type));Check(Method<GuidValue>(type,24)(type,MajorType,Video));Check(Method<GuidValue>(type,24)(type,Subtype,Rgb32));
            // Carry the selected dimensions/rate through RGB conversion instead
            // of letting an underspecified output type choose another format.
            if(chosenSize!=0)Check(Method<LongValue>(type,22)(type,Size,chosenSize));
            if(chosenRate!=0)Check(Method<LongValue>(type,22)(type,FrameRate,chosenRate));
            int converted=Method<SetType>(reader,7)(reader,FirstVideo,0,type);
            if(converted<0&&chosenSize!=0){
                // Some drivers reject explicit RGB timing constraints even
                // after accepting that native format. Retain their working
                // conversion path and report the rate actually negotiated.
                Drop(type);type=0;Check(MFCreateMediaType(out type));Check(Method<GuidValue>(type,24)(type,MajorType,Video));Check(Method<GuidValue>(type,24)(type,Subtype,Rgb32));
                converted=Method<SetType>(reader,7)(reader,FirstVideo,0,type);
            }
            Check(converted);Drop(type);type=0;
            Check(Method<CurrentType>(reader,6)(reader,FirstVideo,out type));Check(Method<GetLong>(type,8)(type,Size,out ulong packed));
            int width=(int)(packed>>32),height=(int)(packed&0xffffffff),stride=width*4;
            if(Method<GetUInt>(type,7)(type,Stride,out uint rawStride)>=0)stride=unchecked((int)rawStride);
            if(width<=0||height<=0||width>8192||height>8192||Math.Abs(stride)<width*4)throw new IOException("Camera returned an unsupported frame size.");
            if(Method<GetLong>(type,8)(type,FrameRate,out ulong negotiated)>=0&&(uint)negotiated!=0)NominalFps=(double)(negotiated>>32)/(uint)negotiated;
            byte[] input=[],output=[];
            ready.TrySetResult();
            while(!stopping) {
                nint sample=0,buffer=0;
                try {
                    Check(Method<Read>(reader,9)(reader,FirstVideo,0,out _,out uint flags,out _,out sample));
                    if((flags&3)!=0)throw new IOException("The camera disconnected.");if(sample==0)continue;
                    Check(Method<Buffer>(sample,41)(sample,out buffer));Check(Method<LockBuffer>(buffer,3)(buffer,out nint bytes,out _,out uint length));
                    try {
                        int absStride=Math.Abs(stride);if(length<(long)absStride*height)throw new IOException("Camera frame is incomplete.");
                        if(input.Length!=length)input=new byte[length];Marshal.Copy(bytes,input,0,input.Length);
                        if(width<=640&&height<=480&&stride==width*4){if(!stopping)frame(input,width,height);continue;}
                        double factor=Math.Min(640.0/width,480.0/height);int w=Math.Max(1,(int)(width*Math.Min(1,factor))),h=Math.Max(1,(int)(height*Math.Min(1,factor)));
                        if(output.Length!=w*h*4)output=new byte[w*h*4];
                        for(int y=0;y<h;y++){int sy=y*height/h;if(stride<0)sy=height-1-sy;
                            for(int x=0;x<w;x++){int from=sy*absStride+(x*width/w)*4,to=(y*w+x)*4;output[to]=input[from];output[to+1]=input[from+1];output[to+2]=input[from+2];output[to+3]=255;}}
                        if(!stopping)frame(output,w,h);
                    }finally{Method<Simple>(buffer,4)(buffer);}
                }finally{Drop(buffer);Drop(sample);}
            }
        }catch(Exception e){ready.TrySetException(e);if(!stopping)fail(e is UnauthorizedAccessException?"Windows blocked camera access. Check Camera privacy settings and allow desktop apps.":e.Message);}
        finally{Drop(type);Drop(reader);reader=0;lock(sourceLock){if(source!=0)Method<Simple>(source,12)(source);Drop(source);source=0;}Drop(options);Drop(attributes);if(started)MFShutdown();if(initialized)CoUninitialize();}
    }
    public void Dispose(){stopping=true;lock(sourceLock){if(source!=0)try{Method<Simple>(source,12)(source);}catch(COMException){ }}if(!thread.Join(1500))throw new IOException("The previous camera is still closing. Restart Qadr before choosing another camera.");}
}
