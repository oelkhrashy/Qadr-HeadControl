using Qadr.Core;
static class PersonalMovementVerification
{
 public static void Run(Action<bool,string> check){
  Observation O(double t,double yaw=0,double pitch=0,double smile=0)=>new(yaw,pitch,smile,0,0,t);
  var p=new Profile{CoordinateVersion=3};var recovery=new RecoveryRest();check(!recovery.Feed(O(0)),"tracking return starts a brief rest hold");for(int i=1;i<12;i++)recovery.Feed(O(i/30.0));check(recovery.Feed(O(.4)),"stable return recentres without a new movement calibration");
  recovery.Reset();recovery.Feed(O(0));check(!recovery.Feed(O(1)),"a tracking gap cannot complete recentre");recovery.Feed(O(1.1));check(!recovery.Feed(O(1.2,.1)),"intentional turn restarts the recovery hold");
  var learner=new RestNoiseLearner();for(int i=0;i<120;i++)learner.Feed(O(i/30.0,Math.Sin(i)*.002,Math.Cos(i)*.002),p,false);check(learner.Model.Samples==0,"unlabeled free head motion never trains the resting model");
  for(int i=0;i<120;i++)learner.Feed(O(i/30.0,Math.Sin(i)*.002,Math.Cos(i)*.002),p,true);check(learner.Model.Samples>=60&&learner.Model.Valid,"labeled rest samples train a valid local statistical model");
  long count=learner.Model.Samples;learner.Feed(O(5,.4),p,true);check(learner.Model.Samples==count,"deliberate turn and discontinuity are excluded from noise training");
  var restored=Protocol.Decode<RestNoiseModel>(Protocol.Encode(learner.Model));check(restored==learner.Model,"personal model persists only versioned numerical sufficient statistics");
  check(new RestNoiseModel(Samples:1000,VarianceX:1,VarianceY:1).Noise(0,.003)<=.004500001,"learned noise is bounded above to preserve movement capability");
  check(new RestNoiseModel(Samples:1000).Noise(0,.003)>=.001949999,"learned noise is bounded below to preserve resting stability");
  check(new RestNoiseModel(Samples:20).Noise(0,.003)==.003,"warm-up model uses calibrated filtering until sufficient samples exist");
  learner.Load(new(VarianceX:double.NaN));check(learner.Model.Samples==0&&learner.Model.Valid,"damaged or nonfinite learned models fall back safely");
  var smallSmile=new ClickLearning(false,new Settings());ClickLessonFeedback? result=null;for(int i=0;i<50;i++)smallSmile.Feed(.01,i/30.0);for(int i=50;i<70;i++)smallSmile.Feed(.065,i/30.0);result=smallSmile.Feed(.01,70/30.0);check(result.Threshold is >.02 and <.065,"subtle-smile lesson learns a tiny demonstrated smile above rest");
  var settings=(new Settings{SmileLearned=true,SmileThreshold=.04}).Validate();check(settings.SmileThreshold==.04,"a learned subtle-smile threshold survives settings validation");
  var c=new MotionController{Settings=settings with{ClickMethod="smile",RightClickMethod="none"}};c.SetProfile(p);int clicks=0;for(int i=0;i<60;i++){var output=c.Feed(O(i/30.0,smile:i>=10&&i<30?.065:.01),100,100);if(output.Click=="left")clicks++;}check(clicks==1,"tiny learned smile emits one left click and relaxes without repeats");
  check(p.Ranges[0].SequenceEqual(new[]{.16,.16}),"tracking recovery and rest learning leave calibrated directional ranges unchanged");
 }
}
