using Qadr.Core;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

internal static class StabilityBenchmark
{
    internal sealed record Result(int Fps, string Scenario, double HoldRmsPx,
        double HoldDriftPx, double CrossTrackRmsPx, double TravelVelocityJitterPxPerSecond,
        double TravelX, double TravelY, double EndpointErrorPx, double LatePathPx);
    private sealed record Sample(double Time, double X, double Y, double Dx, double Dy);
    private static readonly string[] scenarios = ["rest-fine", "rest-noisy", "rest-away",
        "right-noisy", "diagonal-noisy", "slow-diagonal-noisy", "reverse", "corner"];

    internal static Result Measure(int fps, string scenario, Func<Observation, PointerOutput> feed)
    {
        bool moving = scenario is not ("rest-fine" or "rest-noisy" or "rest-away");
        double amplitude = scenario == "rest-fine" ? .25 : scenario is "reverse" or "corner" ? 0 : 1;
        double fullX = moving ? scenario == "slow-diagonal-noisy" ? .005 : .035 : 0;
        double fullY = scenario.Contains("diagonal") ? fullX * .6 : 0;
        double x = 0, y = 0; var samples = new List<Sample>();
        double desiredX = 0, desiredY = 0;
        for (int i = 0; i <= fps * 5; i++)
        {
            double t = i / (double)fps, progress = Math.Clamp((t - .5) / 2.5, 0, 1);
            double yaw = fullX * progress, vertical = fullY * progress;
            if (scenario == "reverse") yaw = t <= 1.75 ? .035 * Math.Clamp((t - .5) / 1.25, 0, 1) : .035 * (1 - Math.Clamp((t - 1.75) / 1.25, 0, 1));
            if (scenario == "corner") { yaw = .035 * Math.Clamp((t - .5) / 1.25, 0, 1); vertical = .021 * Math.Clamp((t - 1.75) / 1.25, 0, 1); }
            desiredX = yaw / .16 * 1344; desiredY = vertical / .16 * 756;
            yaw += amplitude * (.0007 * Math.Sin(t * 2 * Math.PI * 4.7) + .0003 * Math.Sin(t * 2 * Math.PI * 8.3));
            vertical += amplitude * (.0008 * Math.Sin(t * 2 * Math.PI * 5.9) + .0002 * Math.Sin(t * 2 * Math.PI * 9.1));
            if (scenario == "rest-away") { yaw += .07; vertical += .04; }
            var output = feed(new Observation(yaw, -vertical, 0, 0, 0, t, LeftEye: 0, RightEye: 0));
            x += output.Dx; y += output.Dy; samples.Add(new(t, x, y, output.Dx, output.Dy));
        }
        var hold = samples.Where(p => p.Time >= 3.5).ToArray();
        double mx = hold.Average(p => p.X), my = hold.Average(p => p.Y);
        double rms = Math.Sqrt(hold.Average(p => Math.Pow(p.X - mx, 2) + Math.Pow(p.Y - my, 2)));
        var first = hold.Where(p => p.Time <= 3.75).ToArray();
        var last = hold.Where(p => p.Time >= 4.75).ToArray();
        double drift = Math.Sqrt(Math.Pow(first.Average(p => p.X) - last.Average(p => p.X), 2) + Math.Pow(first.Average(p => p.Y) - last.Average(p => p.Y), 2));
        var travel = samples.Where(p => p.Time >= .8 && p.Time <= 2.7).ToArray();
        double length = Math.Sqrt(Math.Pow(fullX * 1344, 2) + Math.Pow(fullY * 756, 2));
        double cross = moving && length > 0 ? Math.Sqrt(travel.Average(p => Math.Pow((p.X * fullY * 756 - p.Y * fullX * 1344) / length, 2))) : 0;
        double vx = travel.Average(p => p.Dx * fps), vy = travel.Average(p => p.Dy * fps);
        double roughness = Math.Sqrt(travel.Average(p => Math.Pow(p.Dx * fps - vx, 2) + Math.Pow(p.Dy * fps - vy, 2)));
        return new(fps, scenario, rms, drift, cross, roughness, x, y,
            Math.Sqrt(Math.Pow(x - desiredX, 2) + Math.Pow(y - desiredY, 2)),
            hold.Sum(p => Math.Sqrt(p.Dx * p.Dx + p.Dy * p.Dy)));
    }

    internal static MotionController Controller() { var c = new MotionController { Settings = new Settings { ClickMethod = "none", RightClickMethod = "none", EyebrowDoubleClick = false } }; c.SetProfile(new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .002] }); return c; }

    private static (double HalfResponseSeconds, double FinalX) StepResponse(int fps, Func<Observation, PointerOutput> feed)
    {
        double x = 0; var positions = new List<(double Time, double X)>();
        for (int i = 0; i <= fps * 3; i++)
        {
            double t = i / (double)fps;
            x += feed(new Observation(t >= .5 ? .01 : 0, 0, 0, 0, 0, t, LeftEye: 0, RightEye: 0)).Dx;
            positions.Add((t, x));
        }
        return (positions.First(p => p.Time >= .5 && p.X >= x * .5).Time - .5, x);
    }

    public static void Run(string baselinePath, string destination)
    {
        var previous = new AssemblyLoadContext("stability-baseline-" + Guid.NewGuid(), true).LoadFromAssemblyPath(Path.GetFullPath(baselinePath));
        var type = previous.GetType("Qadr.Core.MotionController")!;
        object Convert(string name, object value) => JsonSerializer.Deserialize(JsonSerializer.Serialize(value), previous.GetType("Qadr.Core." + name)!)!;
        var rows = new List<object>();
        var steps = new List<object>();
        foreach (int fps in new[] { 10, 30, 60 })
        {
        foreach (string scenario in scenarios)
        {
            var c = Controller(); object legacy = Activator.CreateInstance(type)!;
            type.GetProperty("Settings")!.SetValue(legacy, Convert("Settings", c.Settings));
            type.GetMethod("SetProfile")!.Invoke(legacy, [Convert("Profile", new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .002] })]);
            PointerOutput Feed(Observation o) => JsonSerializer.Deserialize<PointerOutput>(JsonSerializer.Serialize(type.GetMethod("Feed")!.Invoke(legacy, [Convert("Observation", o), 900.0, 500.0, false])))!;
            rows.Add(new { previous = Measure(fps, scenario, Feed), current = Measure(fps, scenario, o => c.Feed(o, 900, 500)) });
        }
            var current = Controller(); object stepLegacy = Activator.CreateInstance(type)!;
            type.GetProperty("Settings")!.SetValue(stepLegacy, Convert("Settings", current.Settings));
            type.GetMethod("SetProfile")!.Invoke(stepLegacy, [Convert("Profile", new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .002] })]);
            PointerOutput StepFeed(Observation o) => JsonSerializer.Deserialize<PointerOutput>(JsonSerializer.Serialize(type.GetMethod("Feed")!.Invoke(stepLegacy, [Convert("Observation", o), 900.0, 500.0, false])))!;
            var a = StepResponse(fps, StepFeed); var b = StepResponse(fps, o => current.Feed(o, 900, 500));
            steps.Add(new { fps, previousHalfResponseSeconds = a.HalfResponseSeconds,
                currentHalfResponseSeconds = b.HalfResponseSeconds, previousTravelX = a.FinalX, currentTravelX = b.FinalX });
        }
        // The existing steadiness control can disable the added layer without
        // changing the earlier vector controller or injecting native input.
        var bypassController = Controller();
        bypassController.Settings = bypassController.Settings with { SteadyHold = 0 };
        object bypassLegacy = Activator.CreateInstance(type)!;
        type.GetProperty("Settings")!.SetValue(bypassLegacy, Convert("Settings", bypassController.Settings));
        type.GetMethod("SetProfile")!.Invoke(bypassLegacy, [Convert("Profile", new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .002] })]);
        const int bypassSamples = 300;
        for (int i = 0; i < bypassSamples; i++)
        {
            var o = new Observation(.02 * Math.Sin(i * .05), -.01 * Math.Sin(i * .08), 0, 0, 0, i / 30.0, LeftEye: 0, RightEye: 0);
            var a = JsonSerializer.Deserialize<PointerOutput>(JsonSerializer.Serialize(type.GetMethod("Feed")!.Invoke(bypassLegacy, [Convert("Observation", o), 900.0, 500.0, false])))!;
            var b = bypassController.Feed(o, 900, 500);
            if (Math.Abs(a.Dx - b.Dx) > 1e-9 || Math.Abs(a.Dy - b.Dy) > 1e-9 || a.StopX != b.StopX || a.StopY != b.StopY)
                throw new Exception("Stability bypass changed the previous vector response at sample " + i);
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { synthetic = true, nativeInput = false,
            meaning = "Matched controller displacement. Velocity jitter measures variation during constant intended travel; not native latency or live usability. Reverse/corner have intentional velocity changes and are inspected for reach instead. Half-response times are capture-sampled and relative to each controller's final travel, not native latency.", rows, steps,
            bypass = new { samples = bypassSamples, passed = true, tolerancePx = 1e-9 } }, Protocol.Json));
        Console.WriteLine($"Compared {rows.Count} noise/hold/turn scenarios; no native input or camera. {destination}");
    }

    public static void Tests(Action<bool, string> check)
    {
        foreach (int fps in new[] { 10, 30, 60 })
        {
            foreach (string scenario in new[] { "rest-noisy", "rest-away" })
            {
                var c = Controller(); var r = Measure(fps, scenario, o => c.Feed(o, 900, 500));
                Console.WriteLine($"METRIC stability_{scenario}_{fps}_rms={r.HoldRmsPx:F4},drift={r.HoldDriftPx:F4}");
                check(r.HoldRmsPx < 4 && r.HoldDriftPx < 6, $"noisy hold remains within 4px RMS/6px drift at {fps} FPS ({scenario})");
            }
            var moving = Controller(); var m = Measure(fps, "diagonal-noisy", o => moving.Feed(o, 900, 500));
            check(m.TravelX > 220 && m.TravelY > 60 && m.CrossTrackRmsPx < 8,
                $"noisy diagonal retains both components, useful reach and <8px cross-track RMS at {fps} FPS");
            var reversing = Controller(); var r2 = Measure(fps, "reverse", o => reversing.Feed(o, 900, 500));
            check(Math.Abs(r2.TravelX) < 25 && Math.Abs(r2.TravelY) < 1,
                $"direction reversal returns near the starting point without an axis lock at {fps} FPS");
            var step = Controller(); var s = StepResponse(fps, o => step.Feed(o, 900, 500));
            Console.WriteLine($"METRIC stability_step_{fps}_half_response_seconds={s.HalfResponseSeconds:F4},travel={s.FinalX:F3}");
            check(s.HalfResponseSeconds <= .2 && s.FinalX > 45,
                $"smoothing retains useful step reach and responds within 200ms of captured input at {fps} FPS");
        }
    }
}
