using Qadr.Core;

/// <summary>Numeric observation replay only. Never opens a camera/microphone or
/// calls NativeInput. Target hit rate requires an actual labelled task trace.</summary>
static class Replay
{
    sealed record Trace(Profile Profile,Settings Settings,Observation[] Observations,bool Synthetic=false);
    public static int Run(string[] args){
        try{
            var input=Arguments.Get(args,"--replay")??throw new ArgumentException("Replay file required");
            var trace=Protocol.Decode<Trace>(File.ReadAllText(input));
            if(!trace.Profile.IsValid||trace.Observations.Length is <2 or >200000)throw new InvalidDataException("Invalid profile/trace size");
            var controller=new MotionController{Settings=trace.Settings.Validate(),Width=1920,Height=1080};controller.SetProfile(trace.Profile);
            var path=new CursorTrajectory();path.Reset(500,400);var cursor=(X:500.0,Y:400.0);
            var rest=new List<(double X,double Y)>();var clicks=new List<object>();var samples=new List<(double Input,double Output,double Time)>();
            double? previous=null,onset=null;double beforeX=500,beforeY=400,maxExpressionMotion=0,pathLength=0;
            bool restPeriodEnded=false;
            double trigger=trace.Settings.SmileRestLevel+(trace.Settings.SmileThreshold-trace.Settings.SmileRestLevel)*.4;
            foreach(var observation in trace.Observations){
                if(previous.HasValue&&observation.Timestamp<=previous.Value)throw new InvalidDataException("Capture timestamps must increase");
                if(new[]{observation.Timestamp,observation.Yaw,observation.Pitch,observation.Scale,observation.Smile}.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("Nonfinite observation");
                if(observation.Smile>=trigger){restPeriodEnded=true;if(!onset.HasValue){onset=observation.Timestamp;beforeX=cursor.X;beforeY=cursor.Y;}}
                var output=controller.Feed(observation,cursor.X,cursor.Y);
                if(onset.HasValue&&observation.Smile>=trigger)maxExpressionMotion=Math.Max(maxExpressionMotion,Math.Sqrt(output.Dx*output.Dx+output.Dy*output.Dy));
                double dt=previous.HasValue?observation.Timestamp-previous.Value:0;previous=observation.Timestamp;
                var old=cursor;
                if(output.Frozen)path.Reset(cursor.X,cursor.Y);else path.Add(output.Dx,output.Dy,cursor.X,cursor.Y,0,0,1920,1080);
                // Include interpolation across the same simulated 8 ms pump.
                for(double remaining=dt;remaining>0;remaining-=.008)cursor=path.Step(Math.Min(.008,remaining));
                if(output.Click is not null){clicks.Add(new{at=observation.Timestamp,source=output.Click,x=output.ClickX,y=output.ClickY,displacementFromOnsetPx=Math.Sqrt(Math.Pow(output.ClickX-beforeX,2)+Math.Pow(output.ClickY-beforeY,2))});cursor=(output.ClickX,output.ClickY);path.Reset(cursor.X,cursor.Y);}
                pathLength+=Math.Sqrt(Math.Pow(cursor.X-old.X,2)+Math.Pow(cursor.Y-old.Y,2));
                if(!restPeriodEnded&&observation.Smile<trigger&&Math.Abs(observation.Yaw-trace.Profile.Neutral[0])<.01&&Math.Abs(observation.Pitch-trace.Profile.Neutral[1])<.01)rest.Add(cursor);
                samples.Add((observation.Yaw,cursor.X,observation.Timestamp));
                if(observation.Smile<trigger)onset=null;
            }
            static double Std(IEnumerable<double> data){var a=data.ToArray();double mean=a.Length==0?0:a.Average();return a.Length==0?0:Math.Sqrt(a.Average(v=>Math.Pow(v-mean,2)));}
            var report=new{trace.Synthetic,observations=trace.Observations.Length,durationSeconds=trace.Observations[^1].Timestamp-trace.Observations[0].Timestamp,restSamples=rest.Count,restSigmaXPx=Std(rest.Select(p=>p.X)),restSigmaYPx=Std(rest.Select(p=>p.Y)),maxExpressionOutputPerFramePx=maxExpressionMotion,clicks,pathLengthPx=pathLength,
                notes="1920x1080 simulated desktop; no native input. Rest sigma assumes the trace returns to the same target. Lag, overshoot and hit rate need labelled movement/target trials; they are not inferred from arbitrary observations."};
            string json=Protocol.Encode(report);string? destination=Arguments.Get(args,"--replay-output");if(destination is not null)File.WriteAllText(destination,json);Console.WriteLine(json);return 0;
        }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
}
