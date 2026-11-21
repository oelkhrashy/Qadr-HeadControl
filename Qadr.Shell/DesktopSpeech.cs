using System.Runtime.InteropServices;

namespace Qadr.Shell;

/// <summary>Local SAPI fallback for Windows builds where WinRT TTS fails.
/// All COM objects are created and released on their own STA thread.</summary>
internal static class DesktopSpeech
{
    public static Task Speak(string text,CancellationToken token,string? diagnosticFile=null){
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            object? voiceObject=null,streamObject=null;
            try{
                token.ThrowIfCancellationRequested();
                voiceObject=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice",true)!)!;dynamic voice=voiceObject;
                if(diagnosticFile is not null){streamObject=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream",true)!)!;dynamic stream=streamObject;stream.Open(diagnosticFile,3,false);voice.AudioOutputStream=stream;voice.Speak(text,0);stream.Close();}
                else{
                    voice.Speak(text,1);long started=Environment.TickCount64;
                    while(!voice.WaitUntilDone(100)){if(token.IsCancellationRequested||Environment.TickCount64-started>90000){voice.Speak("",3);token.ThrowIfCancellationRequested();throw new TimeoutException("Desktop speech timed out");}}
                }
                completion.TrySetResult();
            }catch(OperationCanceledException){completion.TrySetCanceled(token);}
            catch(Exception error){completion.TrySetException(error);}
            finally{if(streamObject is not null)Marshal.FinalReleaseComObject(streamObject);if(voiceObject is not null)Marshal.FinalReleaseComObject(voiceObject);}
        }){IsBackground=true,Name="Qadr local spoken guidance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }
}
