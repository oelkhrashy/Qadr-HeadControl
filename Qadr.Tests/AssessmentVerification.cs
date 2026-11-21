using Qadr.Core;
static class AssessmentVerification
{
 public static void Run(Action<bool,string> check){
  var target=new AssessmentTarget("E","double",100,100,100,100);
  check(target.Valid&&!new AssessmentTarget("E","invalid",0,0,100,100).Valid,"assessment accepts only labelled supported tasks");
  check(target.Contains(150,150)&&!target.Contains(100,100),"a visually circular target does not accept rectangular corner hits");
  var trial=new PointingAssessment(target,0,50,150);
  trial.Feed(.1,100,150,true);trial.Feed(.2,150,150,true);trial.Feed(.3,230,150,true);trial.Feed(.4,150,150,true);
  check(trial.Click("left",.45,150,150) is null,"the first tap cannot complete a required double click");
  var result=trial.Click("double",.5,150,150)!;
  check(result.Completed&&result.ExpectedAction=="double"&&result.ClickAttempts==2,"an actual double-tap event completes the labelled double-click trial once");
  check(result.Overshoots==1&&result.Entries==2&&Math.Abs(result.MaximumOvershootPx-30)<1e-8,"overshoot and re-entry are measured after initially reaching the circle");
  check(result.PathEfficiency is >0 and <1&&result.PathLengthPx>100,"detours lower path efficiency and retain travelled distance");
  check(result.FinalCentreErrorPx==0&&result.FirstEntryMs==100,"arrival time and final centre error use actual cursor samples");
  check(trial.Click("double",.6,150,150) is null,"a completed target cannot generate duplicate successes");
  trial=new(target with{Action="left"},0,50,150);trial.Feed(.1,150,150,true);check(trial.Click("right",.2,150,150) is null,"a wrong button cannot pass a left-click task");
  check(trial.Click("left",.3,150,150) is null&&trial.Progress.ConfirmingSingleClick,"single-click classification waits for the double-click window");result=null!;
  for(int i=4;i<=10;i++)result??=trial.Feed(i/10d,150,150,true)!;
  check(result.WrongActionAttempts==1&&result.Completed,"wrong actions are counted separately before the eventual successful click");
  check(Math.Abs(result.DurationMs-300)<1e-8,"single-click completion metrics use the actual tap time rather than confirmation delay");
  trial=new(target with{Action="left"},0,50,150);trial.Click("left",.1,150,150);trial.Click("double",.2,150,150);AssessmentResult? mistaken=null;
  for(int i=3;i<=10;i++)mistaken??=trial.Feed(i/10d,150,150,true);
  check(mistaken is null&&trial.Cancel(1.1,"end").WrongActionAttempts==1,"an accidental double click cannot pass a single-click target");
  trial=new(target with{Action="move"},0,50,150);AssessmentResult? held=null;
  for(int i=1;i<=45;i++)held??=trial.Feed(i/60d,150+Math.Sin(i),150,true);
  check(held is {Completed:true,InsideJitterRmsPx:>0},"a movement task measures jitter during a real sampled hold inside the target");
  trial=new(target with{Action="move"},0,50,150);for(int i=1;i<20;i++)trial.Feed(i/60d,150,150,true);trial.Feed(.4,150,150,false);check(trial.Feed(.5,150,150,true) is null,"lost tracking cannot complete the old inside-target hold");
  result=trial.Cancel(.6,"user stop");check(!result.Completed&&result.TrackingLosses==1,"an interrupted trial records loss and cancellation instead of success");
  trial=new(target,0,50,150);trial.Feed(.1,150,150,true);check(trial.Click("double",.2,300,150) is null,"an action outside the required target cannot pass the trial");
  check(new[]{"assessmentTarget","assessmentClick","assessmentSkip"}.All(type=>!CommandIntent.IsCurrent(new(type,UserStopEpoch:0),1)),"queued target commands from before Escape are rejected");
 }
}
