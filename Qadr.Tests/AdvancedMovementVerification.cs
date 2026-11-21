using Qadr.Core;

static class AdvancedMovementVerification
{
    sealed record TraceMetrics(string Algorithm,double StationarySigmaPx,double StationaryPeakToPeakPx,
        double IsolatedSpikeExcursionPx,double Step90Seconds,double StepEndpointErrorPx,
        double StepOvershootPx,double SmallCorrectionPx);

    static Observation O(double time,double yaw=.04,double pitch=0)=>new(yaw,pitch,0,0,0,time);
    static MotionController Controller(string algorithm="adaptive",string mapping="relative",double noise=.003,string mode="turn") {
        var controller=new MotionController{Settings=new(){MovementAlgorithm=algorithm,Mapping=mapping,ClickMethod="none",RightClickMethod="none"}};
        controller.SetProfile(new(){CoordinateVersion=3,Noise=[noise,noise],Modes=[mode,mode]});
        return controller;
    }
    static double IdealTravel(double yawChange,double noise=.003) {
        double zone=Math.Clamp(noise*3/.16+.035,.065,.32);
        return yawChange/.16/(1-zone)*1920*.7;
    }

    static TraceMetrics Measure(string algorithm) {
        var controller=Controller(algorithm);var random=new Random(20261012);
        var positions=new List<double>();double x=960;
        for(int i=0;i<450;i++) {
            double t=i/30.0;
            double noise=.0045*Math.Sin(2*Math.PI*6*t)+.002*Math.Sin(2*Math.PI*9.7*t)+.003*(random.NextDouble()*2-1);
            var output=controller.Feed(O(t,.04+noise),x,540);x+=output.Dx;
            if(t>=2)positions.Add(x);
        }
        double mean=positions.Average();
        double sigma=Math.Sqrt(positions.Average(v=>(v-mean)*(v-mean)));
        double peakToPeak=positions.Max()-positions.Min();

        controller=Controller(algorithm);x=960;double excursion=0;
        for(int i=0;i<120;i++) {
            var output=controller.Feed(O(i/30.0,i==30?.11:.04),x,540);x+=output.Dx;
            excursion=Math.Max(excursion,Math.Abs(x-960));
        }

        controller=Controller(algorithm);x=960;double step90=double.PositiveInfinity,maxX=x;
        double travel=IdealTravel(.05);
        for(int i=0;i<120;i++) {
            double t=i/30.0;var output=controller.Feed(O(t,t>=1?.09:.04),x,540);x+=output.Dx;maxX=Math.Max(maxX,x);
            if(t>=1 && x-960>=travel*.9 && double.IsPositiveInfinity(step90))step90=t-1;
        }
        double error=Math.Abs(x-960-travel),overshoot=Math.Max(0,maxX-960-travel);

        controller=Controller(algorithm,noise:.00015);x=960;
        for(int i=0;i<150;i++) {
            double t=i/30.0,yaw=.04+.0008*Math.Clamp(t-1,0,1);
            var output=controller.Feed(O(t,yaw),x,540);x+=output.Dx;
        }
        return new(algorithm,sigma,peakToPeak,excursion,step90,error,overshoot,x-960);
    }

    static double StationarySigma(string algorithm,double noise,double frequency,double responsiveness) {
        var controller=Controller(algorithm,noise:noise);controller.Settings=controller.Settings with{Responsiveness=responsiveness};
        var random=new Random(19);double x=960;var positions=new List<double>();
        for(int i=0;i<360;i++) {
            double t=i/30.0,disturbance=noise*(1.5*Math.Sin(2*Math.PI*frequency*t)+.65*Math.Sin(2*Math.PI*1.617*frequency*t)+random.NextDouble()*2-1);
            x+=controller.Feed(O(t,.04+disturbance),x,540).Dx;
            if(t>=2)positions.Add(x);
        }
        double mean=positions.Average();return Math.Sqrt(positions.Average(p=>(p-mean)*(p-mean)));
    }

    static object[] NoiseMatrix() {
        var scenarios=new List<object>();
        foreach(double noise in new[]{.001,.003,.008})foreach(double frequency in new[]{3.0,6.0,10.0})foreach(double response in new[]{.1,.38,1.0})
            scenarios.Add(new{noiseRadians=noise,frequencyHz=frequency,responsiveness=response,
                classicSigmaPx=StationarySigma("classic",noise,frequency,response),adaptiveSigmaPx=StationarySigma("adaptive",noise,frequency,response)});
        return scenarios.ToArray();
    }

    public static int Report() {
        Console.WriteLine(Protocol.Encode(new{synthetic=true,desktop="1920x1080",classic=Measure("classic"),adaptive=Measure("adaptive"),noiseScenarios=NoiseMatrix(),
            notes="Deterministic 30 FPS traces. Stationary signal: 6 and 9.7 Hz disturbances plus seeded noise around an off-centre pose. Spike: one 0.07-radian sample. Step: 0.05 radians held. Small correction: 0.0008 radians over one second with a lower calibrated noise. Accumulates controller deltas before cursor interpolation; excludes inference, native input and users. Step90 measures time after the first changed sample, not sensor-to-pointer latency."}));
        return 0;
    }

    public static void Run(Action<bool,string> check) {
        var classic=Measure("classic");var adaptive=Measure("adaptive");
        check(adaptive.StationarySigmaPx<classic.StationarySigmaPx*.7,"adaptive filtering reduces stationary synthetic jitter by at least 30 percent");
        check(adaptive.IsolatedSpikeExcursionPx<1 && classic.IsolatedSpikeExcursionPx>20,"a single synthetic pose spike cannot move the adaptive pointer");
        check(adaptive.Step90Seconds<=.3 && adaptive.StepEndpointErrorPx<25,"a confirmed intentional step reaches the target promptly without excessive endpoint error");
        check(adaptive.StepOvershootPx<.001,"a noise-free step produces no pointer overshoot");
        check(adaptive.SmallCorrectionPx>5.5 && adaptive.SmallCorrectionPx<8,$"a small slow correction survives filtering when calibrated noise is low ({adaptive.SmallCorrectionPx:F3} px)");
        bool noiseImproved=true;
        foreach(double noise in new[]{.001,.003,.008})foreach(double frequency in new[]{3.0,6.0,10.0})foreach(double response in new[]{.1,.38,1.0})
            noiseImproved &= StationarySigma("adaptive",noise,frequency,response)<=StationarySigma("classic",noise,frequency,response)+.01;
        check(noiseImproved,"adaptive jitter does not worsen across 27 synthetic noise, frequency and responsiveness combinations");

        foreach(int fps in new[]{10,15,30,60}) {
            var filter=new AdaptiveAxisFilter();filter.Feed(0,0,.005,.38);
            var first=filter.Feed(.5,1.0/fps,.005,.38);
            bool rejected=filter.RejectedSpike;var second=filter.Feed(.5,1.0/fps,.005,.38);
            check(first==0 && rejected && second>0 && !filter.RejectedSpike,$"a sustained fast movement is confirmed in one interval at {fps} FPS");
            double value=second;for(int i=0;i<fps;i++)value=filter.Feed(.5,1.0/fps,.005,.38);
            check(value>.49&&value<=.5,$"adaptive filtering converges to a sustained movement without overshoot at {fps} FPS");
        }

        foreach(string mapping in new[]{"relative","rate"}) {
            var controller=Controller(mapping:mapping);controller.Feed(O(0,0),960,540);
            double dx=0,dy=0;
            for(int i=1;i<=60;i++){var output=controller.Feed(O(i/30.0,.05,.04),960,540);dx+=output.Dx;dy+=output.Dy;}
            check(dx>0&&dy<0,$"adaptive {mapping} retains right and up movement directions");
            controller=Controller(mapping:mapping);controller.Settings=controller.Settings with{InvertX=true,InvertY=true};controller.Feed(O(0,0),960,540);
            dx=dy=0;for(int i=1;i<=60;i++){var output=controller.Feed(O(i/30.0,.05,.04),960,540);dx+=output.Dx;dy+=output.Dy;}
            check(dx<0&&dy>0,$"adaptive {mapping} respects both inversion settings");
        }

        var near=Controller(mode:"shift");var far=Controller(mode:"shift");near.SetProfile(near.Profile! with{Scale=.3});far.SetProfile(far.Profile! with{Scale=.1});
        double nearX=960,farX=960;
        for(int i=0;i<100;i++){
            double offset=.02*Math.Clamp((i-10)/30.0,0,1);
            var n=near.Feed(O(i/30.0,0) with{Scale=.3,X=.5+offset},nearX,540);nearX+=n.Dx;
            var f=far.Feed(O(i/30.0,0) with{Scale=.1,X=.5+offset/3},farX,540);farX+=f.Dx;
        }
        check(Math.Abs(nearX-farX)<.001&&nearX>980,"adaptive translation remains invariant to camera distance");

        var dwell=Controller();dwell.Settings=dwell.Settings with{ClickMethod="dwell",DwellSeconds=.6};
        PointerOutput outputAtRest=new(0,0,null,960,540,0);
        for(int i=0;i<30;i++)outputAtRest=dwell.Feed(O(i/30.0),960,540);
        var candidate=dwell.Feed(O(1,.11),960,540);var escape=dwell.Feed(O(1+1/30.0,.11),960,540);
        check(outputAtRest.Frozen&&candidate.Dx==0&&escape.Dx>0&&!escape.Frozen,"a dwell lock tolerates one spike but releases after a confirmed intended movement");

        var reset=Controller();reset.Feed(O(0,0),960,540);reset.Feed(O(.033,.1),960,540);
        var afterGap=reset.Feed(O(1,.1),960,540);
        check(afterGap.Dx==0&&afterGap.Click is null,"a pending adaptive innovation cannot cause a jump across a tracking gap");
        reset.Reset();var afterReset=reset.Feed(O(1.1,.1),960,540);
        check(afterReset.Dx==0&&afterReset.Dy==0,"explicit reset seeds the adaptive filter without moving the pointer");
        check(Protocol.Decode<Settings>("{\"sensitivity\":1}").Validate().MovementAlgorithm=="adaptive","old saved settings select adaptive precision without losing their other values");
        check(new Settings{MovementAlgorithm="classic"}.Validate().MovementAlgorithm=="classic"&&new Settings{MovementAlgorithm="unknown"}.Validate().MovementAlgorithm=="adaptive","classic remains selectable and unknown movement algorithms validate safely");

        var circuit=Controller();double circuitX=960;
        for(int i=0;i<=3000;i++){
            double t=i/30.0,yaw=.04+.02*Math.Sin(2*Math.PI*.25*t);
            circuitX+=circuit.Feed(O(t,yaw),circuitX,540).Dx;
        }
        for(int i=3001;i<=3060;i++)circuitX+=circuit.Feed(O(i/30.0),circuitX,540).Dx;
        check(Math.Abs(circuitX-960)<30,$"repeated closed movement paths do not accumulate adaptive acceleration drift ({circuitX-960:F3} px)");

        var variable=Controller();double time=0,variableX=960;
        double[] intervals=[.017,.043,.021,.049,.03];
        for(int i=0;time<4;i++) {
            double yaw=.04+.05*Math.Clamp(time-1,0,1);
            variableX+=variable.Feed(O(time,yaw),variableX,540).Dx;
            time+=intervals[i%intervals.Length];
        }
        check(Math.Abs(variableX-960-IdealTravel(.05))<25,$"variable tracking intervals preserve the intended ramp endpoint ({Math.Abs(variableX-960-IdealTravel(.05)):F3}px error)");
        var invalid=new AdaptiveAxisFilter();invalid.Feed(.2,.033,.01,.38);
        check(invalid.Feed(double.NaN,.033,.01,.38)==0 && invalid.RejectedSpike,"nonfinite input cannot poison adaptive filter state");
        check(invalid.Feed(.4,.033,.01,.38)==.4,"the adaptive filter reseeds after unusable input");
    }
}
