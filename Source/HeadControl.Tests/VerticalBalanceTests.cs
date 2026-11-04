using Qadr.Core;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

static class VerticalBalanceTests
{
    public static void Run(Action<bool,string> check)
    {
        var old=Protocol.Decode<Settings>("{\"balanceVertical\":false,\"balanceHorizontal\":false,\"sensitivity\":1.3,\"clickMethod\":\"smile\",\"smileLearned\":true}");
        var updated=old.Validate();
        check(updated.BalanceVertical&&updated.VerticalBalanceVersion==1,"legacy saved settings automatically enable equal up/down response");
        check(!updated.BalanceHorizontal&&updated.Sensitivity==1.3&&updated.ClickMethod=="smile"&&updated.SmileLearned,"vertical migration preserves horizontal settings, sensitivity and clicks");
        check(Protocol.Decode<Settings>(Protocol.Encode(updated)).Validate()==updated,"vertical migration survives settings serialization and is idempotent");
        check(!(updated with{BalanceVertical=false}).Validate().BalanceVertical,"a later explicit choice can still disable vertical balance");
        foreach(string mode in new[]{"turn","shift"})foreach(int fps in new[]{10,30,60})
        {
            var p=new Profile{CoordinateVersion=3,Modes=[mode,mode],Ranges=[[.12,.20],[.24,.10]],Noise=[.002,.002]};
            double Travel(double direction)
            {
                var c=new MotionController{Settings=new Settings{ClickMethod="none",RightClickMethod="none"}.Validate()};c.SetProfile(p);
                double sum=0;
                for(int i=0;i<fps*2;i++)
                {
                    double t=(double)i/fps,amount=direction*.03*Math.Min(1,t);
                    var o=new Observation(0,mode=="turn"?-amount:0,0,0,0,t,Y:.5+(mode=="shift"?amount*.2:0),LeftEye:0,RightEye:0);
                    sum+=c.Feed(o,900,500).Dy;
                }
                return sum;
            }
            double up=Travel(-1),down=Travel(1);
            Console.WriteLine($"METRIC vertical_{mode}_{fps}_fps_up={up:R},down={down:R}");
            // Translation subtraction and the additional stateful vector stage
            // can differ by fractions of a millionth of a pixel after rounding.
            check(up<0&&down>0&&Math.Abs(up+down)<1e-5,$"unequal {mode} calibration gives matching up/down travel at {fps} FPS");
        }
    }

    public static void Regression(string previous,string modified)
    {
        Assembly Load(string path)=>new AssemblyLoadContext(Guid.NewGuid().ToString(),true).LoadFromAssemblyPath(Path.GetFullPath(path));
        var a=Load(previous);var b=Load(modified);int comparisons=0;
        object Make(Assembly asm,string name,object value)=>JsonSerializer.Deserialize(JsonSerializer.Serialize(value),asm.GetType("Qadr.Core."+name)!)!;
        foreach(string mode in new[]{"turn","shift"})foreach(int fps in new[]{10,30,60})
        {
            object Controller(Assembly asm)
            {
                var type=asm.GetType("Qadr.Core.MotionController")!;var c=Activator.CreateInstance(type)!;
                // A legacy JSON document explicitly contains balanceVertical:false.
                var st=asm.GetType("Qadr.Core.Settings")!;
                object settings=JsonSerializer.Deserialize("{\"BalanceVertical\":false,\"ClickMethod\":\"none\",\"RightClickMethod\":\"none\"}",st)!;
                settings=st.GetMethod("Validate")!.Invoke(settings,null)!;
                type.GetProperty("Settings")!.SetValue(c,settings);
                type.GetMethod("SetProfile")!.Invoke(c,[Make(asm,"Profile",new Profile{CoordinateVersion=3,Modes=[mode,mode],Ranges=[[.12,.20],[.24,.10]],Noise=[.002,.002]})]);
                return c;
            }
            var ca=Controller(a);var cb=Controller(b);
            for(int i=0;i<fps*6;i++)
            {
                double time=(double)i/fps;
                // First three seconds: mixed left/right movement. Then a downward
                // stroke. Their previous output must be preserved exactly.
                double horizontal=time<3?.06*Math.Sin(time*3):0;
                double down=time<3?0:.03*Math.Min(1,time-3);
                var o=new Observation(mode=="turn"?horizontal:0,mode=="turn"?-down:0,0,0,0,time,
                    X:.5+(mode=="shift"?horizontal*.2:0),Y:.5+(mode=="shift"?down*.2:0),LeftEye:0,RightEye:0);
                JsonElement Output(object c,Assembly asm)=>JsonSerializer.SerializeToElement(c.GetType().GetMethod("Feed")!.Invoke(c,[Make(asm,"Observation",o),900.0,500.0,false]));
                var x=Output(ca,a);var y=Output(cb,b);
                foreach(string field in new[]{"Dx","Dy"})
                {
                    if(Math.Abs(x.GetProperty(field).GetDouble()-y.GetProperty(field).GetDouble())>1e-9)
                        throw new Exception($"Vertical update changed previous {field} for {mode}/{fps} at sample {i}");
                    comparisons++;
                }
            }
        }
        Console.WriteLine($"PASS: {comparisons} comparisons confirm unchanged left/right and downward displacement against the previous delivered controller, for rotation/translation at 10/30/60 FPS.");
    }
}
