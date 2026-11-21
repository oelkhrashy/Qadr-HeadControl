using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Qadr.Shell;

/// <summary>Local spoken prompts. Recognition stays in the existing Vosk worker.</summary>
internal sealed class StartupSpeech:IDisposable
{
    CancellationTokenSource? current;
    bool disposed;
    public bool Enabled {get;set;}=false;
    public Action<bool>? SpeakingChanged {get;set;}
    public string? Error {get;private set;}
    public void Cancel(){current?.Cancel();}
    public async Task<bool> Say(string text){
        Cancel();if(!Enabled||disposed)return false;
        using var cancel=new CancellationTokenSource();current=cancel;SpeakingChanged?.Invoke(true);
        try{
            using var synthesizer=new SpeechSynthesizer();
            var english=SpeechSynthesizer.AllVoices.FirstOrDefault(v=>v.Language.StartsWith("en-",StringComparison.OrdinalIgnoreCase));if(english is not null)synthesizer.Voice=english;
            using var stream=await synthesizer.SynthesizeTextToStreamAsync(text);
            cancel.Token.ThrowIfCancellationRequested();
            using var player=new MediaPlayer();using var source=MediaSource.CreateFromStream(stream,stream.ContentType);
            var ended=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaEnded+=(_,_)=>ended.TrySetResult(true);player.MediaFailed+=(_,_)=>ended.TrySetResult(false);
            player.Source=source;player.Play();
            bool played=await ended.Task.WaitAsync(TimeSpan.FromSeconds(90),cancel.Token);if(!played)throw new InvalidOperationException("Windows audio playback failed");Error=null;return true;
        }catch(OperationCanceledException){return false;}
        catch(Exception error){
            Diagnostic.Write("Windows TTS unavailable; trying desktop speech: "+error.Message);
            try{await DesktopSpeech.Speak(text,cancel.Token);Error=null;return true;}
            catch(OperationCanceledException){return false;}
            catch(Exception fallback){Error="Spoken guidance unavailable. Check installed Windows voices; visual setup remains available.";Diagnostic.Write("Spoken guidance unavailable: "+fallback.Message);return false;}
        }
        finally{if(ReferenceEquals(current,cancel)){current=null;SpeakingChanged?.Invoke(false);}}
    }
    public void Dispose(){disposed=true;Cancel();}
}
