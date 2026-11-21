namespace Qadr.Core;

/// <summary>Local command vocabulary, shared by every listening phase.</summary>
public static class VoiceControl
{
 public const uint StopVirtualKey=0x1b;
 public static readonly string[] StopPhrases=["stop the app","computer stop","computer pause pointer"];
 public static readonly string[] ResumePhrases=["resume","resume the app","computer resume pointer"];
 public static readonly string[] MovementPhrases=["centre pointer","center pointer","recenter pointer","undo movement settings","start motion test","save motion test","mark rest","mark movement","start pointing test","skip target","save pointing test"];
 public static string Normalize(string text)=>string.Join(' ',text.Trim().ToLowerInvariant().Split(' ',StringSplitOptions.RemoveEmptyEntries));
 public static bool IsStop(string text)=>StopPhrases.Contains(Normalize(text));
 public static bool IsResume(string text)=>ResumePhrases.Contains(Normalize(text));
 public static string[] Phrases(bool handsFree,bool routines,string[] names,string[]? phase=null)=>
  (handsFree?StopPhrases.Concat(ResumePhrases).Concat(MovementPhrases):Enumerable.Empty<string>())
  .Concat(phase??[]).Concat(routines?new[]{"computer confirm","computer start recording","computer stop recording"}.Concat(names.Select(n=>"computer run "+n)):Enumerable.Empty<string>()).Distinct().ToArray();
}
/// <summary>Mute recognition of the app's own speech and its short audio tail.</summary>
public sealed class SpeechCommandGate
{
 bool speaking;double until;
 public void Set(bool active,double now){speaking=active;until=now+.6;}
 public bool Blocked(double now)=>speaking||now<until;
}
