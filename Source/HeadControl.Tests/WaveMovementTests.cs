using Qadr.Core;
using System.Runtime.Loader;
using System.Text.Json;

internal static class WaveMovementTests
{
    internal sealed record Result(int Fps, double Direction, double FrequencyHz,
        double CrossTrackRmsPx, double MaxCrossTrackPx, double TravelX, double TravelY,
        double FinalHoldRmsPx);

    private static MotionController Controller()
    {
        var c = new MotionController { Settings = new Settings { ClickMethod = "none", RightClickMethod = "none", EyebrowDoubleClick = false } };
        c.SetProfile(Profile()); return c;
    }
    private static Profile Profile() => new() { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .002] };

    private static Result Measure(int fps, double direction, double frequency, Func<Observation, PointerOutput> feed, double amplitude = .0015)
    {
        double x = 0, y = 0, startX = 0, startY = 0; bool startRecorded = false;
        var travel = new List<double>(); var hold = new List<(double X, double Y)>();
        for (int i = 0; i <= fps * 6; i++)
        {
            double t = i / (double)fps;
            double yaw = direction * .04 * Math.Clamp((t - .5) / 3.5, 0, 1);
            // Constant intended yaw, with known nuisance pitch oscillation.
            // This is labelled synthetic input, not an estimate of user intent.
            double pitch = amplitude * (Math.Sin(t * 2 * Math.PI * frequency) + Math.Sin(t * 2 * Math.PI * 5.3) / 6);
            var p = feed(new Observation(yaw, pitch, 0, 0, 0, t, LeftEye: 0, RightEye: 0));
            x += p.Dx; y += p.Dy;
            if (!startRecorded && t >= .5) { startX = x; startY = y; startRecorded = true; }
            if (t >= 1 && t <= 3.8) travel.Add(y - startY);
            if (t >= 4.5) hold.Add((x, y));
        }
        double mx = hold.Average(p => p.X), my = hold.Average(p => p.Y);
        return new(fps, direction, frequency, Math.Sqrt(travel.Average(v => v * v)), travel.Max(Math.Abs), x - startX, y - startY,
            Math.Sqrt(hold.Average(p => Math.Pow(p.X - mx, 2) + Math.Pow(p.Y - my, 2))));
    }

    public static void Tests(Action<bool, string> check)
    {
        foreach (int fps in new[] { 10, 30, 60 })
        {
            foreach (double direction in new[] { -1.0, 1.0 })
            foreach (double frequency in new[] { .6, 1.2, 2.4 })
            {
                var c = Controller(); var r = Measure(fps, direction, frequency, o => c.Feed(o, 900, 500), amplitude: .00025);
                Console.WriteLine($"METRIC wave_{fps}_{direction}_{frequency}_rms={r.CrossTrackRmsPx:F5},max={r.MaxCrossTrackPx:F5},travel={r.TravelX:F3}");
                check(r.CrossTrackRmsPx < 1 && r.MaxCrossTrackPx < 2 && r.TravelX * direction > 240,
                    $"horizontal travel rejects small {frequency}Hz pitch oscillation (<1px RMS/<2px peak) without losing reach at {fps} FPS (direction {direction})");
            }
            var shallow = Controller(); double sx = 0, sy = 0;
            for (int i = 0; i <= fps * 5; i++)
            {
                double yaw = .06 * Math.Clamp((i / (double)fps - .5) / 3, 0, 1);
                var p = shallow.Feed(new Observation(yaw, -yaw * .03, 0, 0, 0, i / (double)fps, LeftEye: 0, RightEye: 0), 900, 500);
                sx += p.Dx; sy += p.Dy;
            }
            check(sx > 400 && sy > 3.5, $"a deliberate very shallow diagonal retains vertical displacement at {fps} FPS");

            var turning = Controller(); double ty = 0; double firstVertical = double.PositiveInfinity;
            for (int i = 0; i <= fps * 5; i++)
            {
                double t = i / (double)fps;
                double yaw = .04 * Math.Clamp((t - .5) / 3.5, 0, 1);
                double vertical = .012 * Math.Clamp((t - 2) / .5, 0, 1);
                var p = turning.Feed(new Observation(yaw, -vertical, 0, 0, 0, t, LeftEye: 0, RightEye: 0), 900, 500);
                ty += p.Dy;
                if (ty > 2 && double.IsPositiveInfinity(firstVertical)) firstVertical = t;
            }
            check(ty > 35 && firstVertical <= 2.3, $"vertical correction during horizontal travel starts within 300ms and remains available at {fps} FPS");
        }
    }

    public static void Compare(string baselinePath, string destination)
    {
        var assembly = new AssemblyLoadContext("wave-baseline-" + Guid.NewGuid(), true).LoadFromAssemblyPath(Path.GetFullPath(baselinePath));
        var type = assembly.GetType("Qadr.Core.MotionController")!;
        object Convert(string name, object value) => JsonSerializer.Deserialize(JsonSerializer.Serialize(value), assembly.GetType("Qadr.Core." + name)!)!;
        var rows = new List<(Result Previous, Result Current)>();
        foreach (int fps in new[] { 10, 30, 60 })
        foreach (double direction in new[] { -1.0, 1.0 })
        foreach (double frequency in new[] { .6, 1.2, 2.4 })
        {
            var c = Controller(); var legacy = Activator.CreateInstance(type)!;
            type.GetProperty("Settings")!.SetValue(legacy, Convert("Settings", c.Settings));
            type.GetMethod("SetProfile")!.Invoke(legacy, [Convert("Profile", Profile())]);
            PointerOutput Feed(Observation o) => JsonSerializer.Deserialize<PointerOutput>(JsonSerializer.Serialize(type.GetMethod("Feed")!.Invoke(legacy, [Convert("Observation", o), 900.0, 500.0, false])))!;
            var previous = Measure(fps, direction, frequency, Feed);
            var current = Measure(fps, direction, frequency, o => c.Feed(o, 900, 500));
            rows.Add((previous, current));
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { synthetic = true, nativeInput = false,
            intendedInput = "horizontal yaw ramp; larger 0.0015rad pitch oscillation plus a sixth-amplitude secondary oscillation, labelled as nuisance for these tests only",
            geometry = "cross-track measured against horizontal line through actual cursor position at travel start (0.5s)",
            limitation = "Stress measurements, not an all-cases pass gate. Some cases can worsen. Tests do not infer real user intent; deliberate shallow curves can also be damped. Not live accuracy or comfort.",
            rmsWorsenedCases = rows.Count(r => r.Current.CrossTrackRmsPx > r.Previous.CrossTrackRmsPx + 1e-8),
            rows = rows.Select(r => new { previous = r.Previous, current = r.Current }) }, Protocol.Json));
        Console.WriteLine($"MEASURED: {rows.Count} matched larger-wave stress cases; {rows.Count(r => r.Current.CrossTrackRmsPx > r.Previous.CrossTrackRmsPx + 1e-8)} cross-track RMS regressions. See {destination}; no live success claim.");
        if (rows.Any(r => r.Current.CrossTrackRmsPx > r.Previous.CrossTrackRmsPx + 1e-8
            || Math.Abs(r.Current.TravelX - r.Previous.TravelX) > 1))
            throw new Exception("A larger-wave comparison worsened cross-track RMS or changed horizontal reach by more than 1px; evidence retained.");
        Console.WriteLine("PASS: cross-track RMS does not worsen and horizontal reach stays within 1px in all 18 matched cases.");
    }
}
