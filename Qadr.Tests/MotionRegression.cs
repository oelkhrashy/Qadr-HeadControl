using Qadr.Core;

static class MotionRegression
{
    static Observation O(double t, double yaw = 0, double y = .5) => new(yaw, 0, 0, 0, 0, t, Y: y);

    public static void Run(Action<bool, string> check)
    {
        var quality = new QualityGate();
        quality.Accept(O(1), 1);
        check(!quality.Accept(O(1.7), 1.7), "a tracking gap restarts the stability hold");
        quality = new();
        for (int i = 0; i < 30; i++) quality.Accept(O(i / 30.0), i / 30.0);
        check(!quality.Accept(O(29 / 30.0), 1), "a duplicate capture cannot advance tracking");
        quality = new();
        for (int i = 0; i < 30; i++) quality.Accept(O(i / 30.0), i / 30.0);
        check(!quality.Accept(O(1, y: .7), 1), "a vertical tracking jump is rejected like a horizontal jump");
        check(!new QualityGate().Accept(O(double.NaN), 1), "nonfinite capture timestamps are rejected");
        check(!new QualityGate().Accept(O(1) with { LeftEye = double.NaN }, 1), "nonfinite eye scores are rejected");

        var smile = new Gesture(.28);
        smile.Feed(.8, .48, 0);
        check(!smile.Feed(.8, .48, 1), "missing frames cannot complete a smile hold");
        var eyes = new EyeClick();
        var settings = new Settings();
        for (int i = 0; i < 20; i++) eyes.Feed(.9, .9, settings, i / 30.0);
        check(!eyes.Feed(0, 0, settings, 2), "reopening after a tracking gap cannot fire an old eye click");

        var motion = new MotionController { Settings = new() { ClickMethod = "smile", RightClickMethod = "none" } };
        motion.SetProfile(new() { CoordinateVersion = 3 });
        motion.Feed(O(0), 100, 100);
        motion.Feed(O(.033, .04), 100, 100);
        var duplicate = motion.Feed(O(.033, .10) with { Smile = .9 }, 100, 100);
        check(duplicate.Frozen && duplicate.Dx == 0 && duplicate.Click is null, "duplicate samples freeze without changing a gesture or filter");
        for (int i = 2; i < 7; i++) motion.Feed(O(i / 30.0) with { Smile = .9 }, 100, 100);
        var recovered = motion.Feed(O(2) with { Smile = .9 }, 100, 100);
        check(recovered.Dx == 0 && recovered.Click is null, "a resumed stream seeds movement and restarts gesture timing");

        motion = new() { Settings = new() { ClickMethod = "none", RightClickMethod = "both", RightClickHold = .35 } };
        motion.SetProfile(new() { CoordinateVersion = 3 });
        for (int i = 0; i < 10; i++) motion.Feed(O(i / 30.0), 100, 100);
        for (int i = 10; i < 25; i++) motion.Feed(O(i / 30.0, .12) with { LeftEye = .9, RightEye = .9 }, 100, 100);
        var released = motion.Feed(O(25 / 30.0), 100, 100);
        double rebound = 0;
        for (int i = 26; i < 40; i++) { var p = motion.Feed(O(i / 30.0), 100, 100); rebound += Math.Abs(p.Dx) + Math.Abs(p.Dy); }
        check(released.Click == "right" && rebound < .001, "eye-click release discards expression motion without cursor rebound");

        double Travel(int fps) {
            var c = new MotionController { Settings = new() { ClickMethod = "none", RightClickMethod = "none", Mapping = "rate" } };
            c.SetProfile(new() { CoordinateVersion = 3 });
            c.Feed(O(0, .08), 500, 400);
            double travel = 0;
            for(int i=1;i<=fps*3;i++)travel+=c.Feed(O((double)i/fps,.08),500,400).Dx;
            return travel;
        }
        check(Math.Abs(Travel(10)-Travel(30))<.001 && Math.Abs(Travel(30)-Travel(60))<.001,
            "steady rate movement travels the same distance at 10, 30 and 60 tracking FPS");

        var points=Enumerable.Range(0,478).Select(i=>new[]{.5+Math.Sin(i)*.12,.5+Math.Cos(i)*.18,Math.Sin(i)*.02}).ToArray();
        points[33]=[.4,.5,0];points[263]=[.6,.5,0];points[1]=[.54,.55,0];
        var full=new VisionResult(1,"result",Captured:1,Landmarks:points,Blendshapes:new(){{"eyeBlinkLeft",.7},{"mouthSmileLeft",.4},{"mouthSmileRight",.6}});
        var geometry=new FaceGeometry(.5,.5,.2,.54,.55,points.Min(p=>p[0]),points.Min(p=>p[1]),points.Max(p=>p[0]),points.Max(p=>p[1]),0);
        var compact=full with{Landmarks=null,Geometry=geometry};
        var fullObservation=Features.Extract(full);var compactObservation=Features.Extract(compact);
        check(fullObservation is not null && compactObservation is not null && Math.Abs(fullObservation.Yaw-compactObservation.Yaw)<1e-12 && Math.Abs(fullObservation.Scale-compactObservation.Scale)<1e-12 && fullObservation.FaceWidth==compactObservation.FaceWidth && fullObservation.LeftEye==compactObservation.LeftEye,
            "compact geometry preserves pose, scale, face bounds and eye scores");
        check(Features.Extract(Protocol.Decode<VisionResult>(Protocol.Encode(compact)))==compactObservation,"compact observations survive the JSON wire format");
        check(Features.Extract(compact with{Geometry=geometry with{OutsideFraction=.09}}) is null,"compact geometry retains the face-clipping gate");
        check(Features.Extract(compact with{Geometry=geometry with{EyeScale=double.NaN}}) is null,"nonfinite compact geometry is rejected");
        check(Features.Extract(compact with{Matrix=[double.NaN]}) is null,"a corrupt pose matrix cannot silently switch to a different mapping");
        check(Features.Extract(compact with{Blendshapes=new(){{"eyeBlinkLeft",double.NaN}}}) is null,"nonfinite blendshape scores cannot enter gesture state");
        var brokenPoints=(double[][])points.Clone();brokenPoints[5]=null!;
        check(Features.Extract(full with{Landmarks=brokenPoints}) is null,"a malformed legacy landmark row is rejected without crashing");
    }
}
