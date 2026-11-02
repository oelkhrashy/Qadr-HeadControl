using Qadr.Core;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

static class Tests
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    static Observation O(double t, double yaw=0, double pitch=0, double smile=0, double left=0, double right=0) => new(yaw,pitch,smile,0,Math.Min(left,right),t,LeftEye:left,RightEye:right);
    static MotionController Control(Settings? s=null) { var c=new MotionController { Settings=s??new Settings { ClickMethod="none",RightClickMethod="none" } }; c.SetProfile(new Profile { CoordinateVersion=3,Ranges=[[.16,.16],[.16,.16]],Noise=[.002,.002] }); return c; }
    static double Travel(int fps, double start=0, double end=.03, double centreAnchor=0) {
        var c=Control(new Settings {ClickMethod="none",RightClickMethod="none",CentreAnchoring=centreAnchor}); c.ControlArea=(0,0,1920,1080);
        double x=900,y=500,total=0;
        for(int i=0;i<=fps;i++){var p=c.Feed(O((double)i/fps,start+(end-start)*i/fps),x,y);x+=p.Dx;y+=p.Dy;total+=p.Dx;}
        for(int i=1;i<=fps;i++){var p=c.Feed(O(1+(double)i/fps,end),x,y);x+=p.Dx;total+=p.Dx;}
        return total;
    }
    static void Main(string[] args) {
        if(args.Length>0&&args[0]=="--quality-replay"){QualityReplay.Run(args[1],args[2]);return;}
        if(args.Length>0&&args[0]=="--lab-mode"){LabModeTests.Run();return;}
        if(args.Length>0&&args[0]=="--process-smoke") { ProcessSmoke.Run(args[1],args[2]).GetAwaiter().GetResult(); return; }
        if(args.Length>0&&args[0]=="--recovery") { Recovery(args[1],args[2]); return; }
        if(args.Length>0&&args[0]=="--api") { Api(args[1],args[2]); return; }
        if(args.Length>0&&args[0]=="--vertical-regression") { VerticalBalanceTests.Regression(args[1],args[2]); return; }
        VerticalBalanceTests.Run(Check);
        MovementQualityTests.Run(Check);
        RecoveryAlignmentTests.Run(Check);
        double small=Travel(30,end:.004);
        Check(small>15&&small<40,"fine horizontal motion through neutral is not swallowed by a dead zone");
        Check(Travel(30,start:.08,end:.084)>15,"the same fine correction works away from neutral");
        Check(Math.Abs(Travel(30)-Travel(30,centreAnchor:1))<1e-8,"relative head movement has no centre attraction");
        Check(Math.Abs(Travel(30)+Travel(30,end:-.03))<1e-8,"symmetric left and right turns have symmetric response");
        var travel=new[]{10,30,60}.Select(f=>Travel(f)).ToArray();
        Console.WriteLine("METRIC travel_px_10_30_60_fps="+string.Join(",",travel.Select(v=>v.ToString("F2"))));
        Check(travel.Min()>200&&travel.Max()<265&&travel.Max()-travel.Min()<35,"capture-time movement stays comparable at 10, 30 and 60 FPS");
        foreach(double pitch in new[]{-.04,.04}) {
            var c=Control(); double sum=0;
            for(int i=0;i<60;i++)sum+=c.Feed(O(i/30.0,pitch:pitch*Math.Min(1,i/30.0)),900,500).Dy;
            Check(Math.Sign(sum)==-Math.Sign(pitch),"pitch direction maps correctly: "+pitch);
        }
        {
            var c=Control();double late=0;bool stopped=false;
            for(int i=0;i<90;i++){var p=c.Feed(O(i/30.0,.03*Math.Min(1,i/30.0)),900,500);if(i>45)late+=Math.Abs(p.Dx);stopped|=p.StopX;}
            Check(late<1e-8&&stopped,"stopping the head discards filter remainder without coasting");
        }
        {
            var c=Control(); double total=0;
            for(int i=0;i<90;i++)total+=Math.Abs(c.Feed(O(i/30.0,i==30?.35:0),900,500).Dx);
            Check(total<.01,"one isolated head-pose spike cannot move the pointer");
            var invalid=c.Feed(O(4,double.NaN),900,500);
            Check(invalid.Frozen&&invalid.Dx==0&&invalid.Click is null,"invalid tracking freezes movement and clicks");
        }
        {
            var c=Control();double x=900;var positions=new List<double>();
            for(int i=0;i<300;i++){var p=c.Feed(O(i/30.0,.0003*Math.Sin(i*1.7)),x,500);x+=p.Dx;if(i>60)positions.Add(x);}
            double rms=Math.Sqrt(positions.Select(v=>Math.Pow(v-positions.Average(),2)).Average());
            Console.WriteLine("METRIC synthetic_rest_jitter_rms_px="+rms.ToString("F3"));
            Check(rms<2,"small synthetic resting noise remains bounded");
        }
        {
            var c=Control();c.Feed(O(0),900,500);c.Feed(O(.033,.03),900,500);
            var duplicate=c.Feed(O(.033,.4),900,500);
            Check(duplicate.Frozen&&duplicate.Dx==0,"duplicate camera timestamps do not advance movement");
            var recovered=c.Feed(O(2,.3),900,500);
            Check(recovered.Dx==0&&recovered.Click is null,"tracking-gap recovery seeds movement without a jump");
        }
        SmileTests(); EyeTests(); ConflictTests(); TrajectoryTests();
        var mouse=new MouseActions();
        Check(mouse.Trigger("left").Steps.Select(s=>s.Button+":"+s.Kind).SequenceEqual(new[]{"left:down","left:up"}),"left action requests one matched press and release");
        Check(mouse.Trigger("right").Steps.Select(s=>s.Button+":"+s.Kind).SequenceEqual(new[]{"right:down","right:up"}),"right action requests one matched press and release");
        Console.WriteLine($"{count} head-control checks passed. No native input or camera was used.");
    }
    static void SmileTests() {
        var g=new Gesture(.28);int clicks=0;
        for(int i=0;i<180;i++)if(g.Feed(i<10?0:.8,.48,i/30.0))clicks++;
        Check(clicks==1&&g.Active,"a long held smile produces exactly one click and stays anchored");
        for(int i=180;i<190;i++)g.Feed(0,.48,i/30.0);
        Check(!g.Active,"stable expression release unfreezes movement");
        for(int i=190;i<210;i++)if(g.Feed(.8,.48,i/30.0))clicks++;
        Check(clicks==2,"a second smile after stable neutral produces one new click");
        g=new(.28);clicks=0;for(int i=0;i<50;i++)if(g.Feed(i>=10&&i<14?.8:0,.48,i/30.0))clicks++;
        Check(clicks==0,"brief smile noise does not click");
        g=new(.28);clicks=0;for(int i=0;i<80;i++)if(g.Feed(i<10?0:i%2==0?.6:.36,.48,i/30.0))clicks++;
        Check(clicks==1,"activation/release hysteresis tolerates threshold chatter without repeats");
        g=new(.28);for(int i=0;i<16;i++)g.Feed(i<10?0:.8,.48,i/30.0);
        Check(!g.Feed(.8,.48,2),"a tracking gap cannot complete an old smile");
        clicks=0;for(int i=61;i<90;i++)if(g.Feed(.8,.48,i/30.0))clicks++;
        Check(clicks==0,"a smile already held when tracking returns requires neutral before clicking");
    }
    static int EyeTrace(string method,double seconds,bool both=false,bool initiallyClosed=false) {
        var e=new EyeClick();var s=new Settings{RightClickMethod=method};int clicks=0;
        for(int i=0;i<180;i++){double t=i/60.0;bool closed=t>=(initiallyClosed?0:.3)&&t<(initiallyClosed?0:.3)+seconds;if(e.Feed(closed?.9:0,closed&&both?.9:0,s,t))clicks++;}
        return clicks;
    }
    static void EyeTests() {
        Check(EyeTrace("either",.1)==0,"ordinary short blinks do not right-click");
        Check(EyeTrace("either",.4)==1,"a deliberate wink and stable reopening produce one right-click");
        Check(EyeTrace("both",.32,true)==0,"short bilateral closure does not right-click");
        Check(EyeTrace("both",.5,true)==1,"deliberate both-eye hold and reopening produce one right-click");
        Check(EyeTrace("left",.5,true)==0,"both-eye closure cannot trigger left-eye-only mode");
        Check(EyeTrace("none",.5)==0,"disabled right-click never fires");
        Check(EyeTrace("either",.5,initiallyClosed:true)==0,"starting with closed eyes cannot trigger right-click");
        Check(EyeTrace("either",2.6)==0,"very long eye closures are canceled");
        var e=new EyeClick();var s=new Settings();for(int i=0;i<35;i++)e.Feed(i<10?0:.9,0,s,i/60.0);
        Check(!e.Feed(0,0,s,2),"reopening across a tracking gap cannot fire an old eye gesture");
        for(int i=121;i<125;i++)Check(!e.Feed(0,0,s,i/60.0),"tracking recovery stays click-free during neutral rearming "+i);
        e=new();int clicks=0;for(int i=0;i<150;i++){bool closed=i>=20&&i<50||i>=90&&i<120;if(e.Feed(closed?.9:0,0,s,i/60.0))clicks++;}
        Check(clicks==2,"two distinct deliberate winks produce two right-clicks");
    }
    static void ConflictTests() {
        var c=Control(new Settings{ClickMethod="smile",SmileLearned=true,SmileThreshold=.48});int left=0,right=0;double moved=0;
        for(int i=0;i<120;i++) {
            bool gesture=i>=15&&i<42;var p=c.Feed(O(i/30.0,gesture?.05:0,smile:gesture?.8:0,left:gesture?.9:0),900,500);
            if(p.Click=="left")left++;if(p.Click=="right")right++;if(i>=15&&i<50)moved+=Math.Abs(p.Dx);
        }
        Check(right==1&&left==0,"simultaneous smile and wink yield right-click only");
        Check(moved<.01,"expression-related pose changes do not move the pointer during the click");
        c=Control(new Settings{ClickMethod="smile",SmileLearned=true});left=0;double rebound=0;
        for(int i=0;i<120;i++){bool g=i>=15&&i<40;var p=c.Feed(O(i/30.0,g?.05:0,smile:g?.8:0),900,500);if(p.Click=="left")left++;if(i>48)rebound+=Math.Abs(p.Dx);}
        Check(left==1&&rebound<.01,"smile clicks once without cursor rebound after release");
        c=Control(new Settings{ClickMethod="dwell",RightClickMethod="none",DwellSeconds=.6});left=0;
        for(int i=0;i<120;i++)if(c.Feed(O(i/30.0),900,500).Click=="left")left++;
        Check(left==1,"dwell clicks only once until leaving its anchor");
    }
    static void TrajectoryTests() {
        var t=new CursorTrajectory();t.Add(100,0,900,500,-1920,0,3840,1080,captured:1);var a=t.Step(.008,1.008);var b=t.Step(.008,1.3);
        Check(a==b,"stale capture time discards remaining relative interpolation");
        t=new();t.SetAbsolute(.9,.9,900,500,0,0,1920,1080,1);a=t.Step(.008,1.008);b=t.Step(.008,1.3);
        Check(a==b,"absolute interpolation also freezes when observations become stale");
        t=new();t.Add(double.NaN,0,900,500,0,0,1920,1080);Check(!t.Ready,"nonfinite displacement cannot enter native pointer trajectory");
        t=new();t.SetRate(100,0,900,500,0,0,1920,1080,1);t.Add(0,0,900,500,0,0,1920,1080,captured:1.01);a=t.Step(.01,1.02);
        Check(a.X==900,"changing from velocity to relative mode cancels the old velocity");
        t=new();t.Add(-5000,0,-1000,500,-1920,0,3840,1080,captured:1);
        for(int i=1;i<20;i++)a=t.Step(.008,1+i*.008);
        Check(a.X>=-1920&&a.X<0,"negative-coordinate monitor bounds are preserved");
    }
    static Assembly Load(string path) => new AssemblyLoadContext(Guid.NewGuid().ToString(),true).LoadFromAssemblyPath(Path.GetFullPath(path));
    static string Signature(MemberInfo member) => member.MemberType+":"+member.ToString();
    static void Api(string original,string modified) {
        var a=Load(original);var b=Load(modified);
        foreach(var t in a.GetExportedTypes()) {
            var target=b.GetType(t.FullName!);Check(target is not null,"public type preserved: "+t.FullName);
            var before=t.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Select(Signature).Order().ToArray();
            // This revision adds one serialized settings migration marker. All
            // pre-existing public signatures must still match exactly.
            var after=target!.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)
                .Where(m=>t.FullName!="Qadr.Core.Settings"||m.Name is not ("VerticalBalanceVersion" or "get_VerticalBalanceVersion" or "set_VerticalBalanceVersion"))
                .Select(Signature).Order().ToArray();
            Check(before.SequenceEqual(after),"public members preserved: "+t.FullName);
        }
        Console.WriteLine($"{count} binary interface checks passed.");
    }
    static void Recovery(string original,string recovered) {
        var a=Load(original);var b=Load(recovered);int samples=0;
        foreach(string mapping in new[]{"relative","rate","absolute"})foreach(string mode in new[]{"turn","shift"})foreach(int fps in new[]{10,30,60}) {
            object Make(Assembly asm,string name,object value)=>JsonSerializer.Deserialize(JsonSerializer.Serialize(value),asm.GetType("Qadr.Core."+name)!)!;
            object Controller(Assembly asm) {
                var type=asm.GetType("Qadr.Core.MotionController")!;object c=Activator.CreateInstance(type)!;
                type.GetProperty("Settings")!.SetValue(c,Make(asm,"Settings",new Settings{Mapping=mapping,ClickMethod="smile",CentreAnchoring=.75}));
                type.GetMethod("SetProfile")!.Invoke(c,[Make(asm,"Profile",new Profile{CoordinateVersion=3,Modes=[mode,mode]})]);return c;
            }
            var ca=Controller(a);var cb=Controller(b);
            for(int i=0;i<fps*6;i++) {
                double time=(double)i/fps;var o=O(time,.06*Math.Sin(time*2),.03*Math.Sin(time*3),smile:time>2&&time<2.6?.8:0,left:time>4&&time<4.6?.9:0);
                JsonElement Output(object c,Assembly asm)=>JsonSerializer.SerializeToElement(c.GetType().GetMethod("Feed")!.Invoke(c,[Make(asm,"Observation",o),900.0,500.0,false]));
                var x=Output(ca,a);var y=Output(cb,b);
                foreach(var p in x.EnumerateObject()) {
                    var q=y.GetProperty(p.Name);
                    bool equal=p.Value.ValueKind==JsonValueKind.Number?Math.Abs(p.Value.GetDouble()-q.GetDouble())<1e-9:p.Value.ToString()==q.ToString();
                    if(!equal)throw new Exception($"Recovery changed {mapping}/{mode}/{fps} sample {i} field {p.Name}: {p.Value} vs {q}");
                }
                samples++;
            }
        }
        Console.WriteLine($"PASS: unmodified recovered controller matches supplied binary for {samples} synthetic observations across three mappings, two movement channels, and three frame rates.");
    }
}
