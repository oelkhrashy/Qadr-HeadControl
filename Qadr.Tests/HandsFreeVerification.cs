using Qadr.Core;
static class HandsFreeVerification
{
 public static void Run(Action<bool,string> check){
  check(VoiceControl.StopVirtualKey==0x1b,"global emergency key is unmodified Escape");
  check(VoiceControl.IsStop(" STOP  THE APP ")&&VoiceControl.IsStop("computer stop"),"stop the app and the earlier stop phrase both stop control");
  check(VoiceControl.IsResume("resume")&&VoiceControl.IsResume("resume the app")&&VoiceControl.IsResume("computer resume pointer"),"plain resume and explicit resume aliases work without a mouse");
  check(!VoiceControl.IsResume("resume tomorrow")&&!VoiceControl.IsStop("computer stop recording"),"unrelated text and recording commands do not become pointer stop/resume");
  check(new Settings().HandsFreeVoice&&Protocol.Decode<Settings>("{}").HandsFreeVoice,"hands-free stop/resume is enabled for new and existing settings");
  check(!new Settings{HandsFreeVoice=false}.Validate().HandsFreeVoice,"the user can turn the background command microphone off");
  foreach(var phase in new[]{TutorialFlow.Phrases(true),TutorialFlow.Phrases(false),CameraVoice.Phrases(2),Array.Empty<string>()}){
   var phrases=VoiceControl.Phrases(true,false,[],phase);
   check(VoiceControl.ResumePhrases.All(phrases.Contains)&&VoiceControl.StopPhrases.All(phrases.Contains),"stop and resume vocabulary survives tutorial, camera and ordinary phases");
  }
  check(!VoiceControl.Phrases(true,false,["mail"]).Contains("computer run mail")&&VoiceControl.Phrases(true,true,["mail"]).Contains("computer run mail"),"routine execution has a separate voice switch");
  check(VoiceControl.Phrases(false,false,[]).Length==0,"switching both command features off leaves no background vocabulary");
  var prompt=new SpeechCommandGate();prompt.Set(true,1);check(prompt.Blocked(10),"narration cannot recognize its own spoken commands");prompt.Set(false,10);check(prompt.Blocked(10.3)&&!prompt.Blocked(10.7),"recognition waits for the narration audio tail before accepting resume");
  var chain=new StartupFlow(true);chain.Cancel();check(!chain.Advance(),"stop cancels automatic startup");chain.Begin();check(chain.Advance(),"a fresh explicit resume rearms the full setup-to-control chain");chain.Started();check(!chain.Advance(),"automatic startup still completes only once");
  chain=new(false);chain.Begin();check(!chain.Advance(),"ordinary no-input diagnostics cannot rearm automatic control");
  check(!CommandIntent.IsCurrent(new("diagnosticVoice",Name:"resume",UserStopEpoch:2),3),"queued pre-stop speech cannot request a new resume");
 }
}
