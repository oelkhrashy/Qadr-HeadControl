using Qadr.Core;

static class LabModeTests
{
    public static void Run()
    {
        string marker=Environment.GetEnvironmentVariable("QADR_MOVEMENT_LAB_ACTIVE")!;
        var c=new MotionController{Settings=new Settings{ClickMethod="dwell",RightClickMethod="either"}};
        c.SetProfile(new Profile{CoordinateVersion=3});
        if(c.EffectiveSettings.ClickMethod!="dwell")throw new Exception("Normal clicks disabled before trial.");
        File.WriteAllText(marker,"test");Thread.Sleep(150);
        if(c.EffectiveSettings.ClickMethod!="none"||c.EffectiveSettings.RightClickMethod!="none")throw new Exception("Trial did not suspend clicks.");
        for(int i=0;i<120;i++)
        {
            var p=c.Feed(new Observation(0,0,.9,0,.9,i/30.0,LeftEye:.9,RightEye:.9),900,500);
            if(p.Click is not null||p.Frozen)throw new Exception("A gesture contaminated the movement-only hold.");
        }
        if(c.Settings.ClickMethod!="dwell")throw new Exception("Saved settings were mutated.");
        File.Delete(marker);Thread.Sleep(150);
        if(c.EffectiveSettings.ClickMethod!="dwell"||c.EffectiveSettings.RightClickMethod!="either")throw new Exception("Normal clicks did not return after trial.");
        Console.WriteLine("PASS: Movement-only trials suspend click locks without changing preferences, and restore normal click behavior on completion.");
    }
}
