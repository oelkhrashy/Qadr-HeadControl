using Qadr.Core;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

internal static class AnalogBenchmark
{
    public static void Run(string baselinePath, string destination)
    {
        var baseline = new AssemblyLoadContext("analog-baseline-" + Guid.NewGuid(), true).LoadFromAssemblyPath(Path.GetFullPath(baselinePath));
        var results = new List<object>();
        foreach (int fps in new[] { 10, 30, 60 })
        foreach (string scenario in new[] { "right", "left", "up", "down", "shallow-diagonal", "diagonal", "noisy-horizontal", "rest" })
        foreach (bool previous in new[] { true, false })
        {
            var profile = new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .015] };
            var settings = new Settings { ClickMethod = "none", RightClickMethod = "none", EyebrowDoubleClick = false };
            var current = new MotionController { Settings = settings }; current.SetProfile(profile);
            object? legacy = null; Type? legacyType = null;
            object Convert(string type, object value) => JsonSerializer.Deserialize(JsonSerializer.Serialize(value), baseline.GetType("Qadr.Core." + type)!)!;
            if (previous)
            {
                legacyType = baseline.GetType("Qadr.Core.MotionController")!;
                legacy = Activator.CreateInstance(legacyType)!;
                legacyType.GetProperty("Settings")!.SetValue(legacy, Convert("Settings", settings));
                legacyType.GetMethod("SetProfile")!.Invoke(legacy, [Convert("Profile", profile)]);
            }
            double x = 0, y = 0, path = 0, tail = 0;
            var positions = new List<(double Time, double X, double Y)>();
            double headX = scenario is "left" ? -.02 : scenario is "up" or "down" or "rest" ? 0 : .02;
            double headY = scenario is "up" ? -.02 : scenario is "down" ? .02 : scenario is "shallow-diagonal" ? .001 : scenario is "diagonal" ? .02 : 0;
            double targetX = headX / .16 * 1920 * .7, targetY = headY / .16 * 1080 * .7;
            for (int i = 0; i <= fps * 3; i++)
            {
                double t = i / (double)fps, progress = Math.Min(t, 1);
                bool noise = scenario is "rest" or "noisy-horizontal";
                double yaw = headX * progress + (noise ? .0003 * Math.Sin(t * 2 * Math.PI * 8.7) : 0);
                double pitch = -headY * progress + (noise ? .0005 * Math.Sin(t * 2 * Math.PI * 7.1) : 0);
                var o = new Observation(yaw, pitch, 0, 0, 0, t, LeftEye: 0, RightEye: 0);
                double dx, dy;
                if (previous)
                {
                    var output = JsonSerializer.SerializeToElement(legacyType!.GetMethod("Feed")!.Invoke(legacy, [Convert("Observation", o), 900.0, 500.0, false]));
                    dx = output.GetProperty("Dx").GetDouble(); dy = output.GetProperty("Dy").GetDouble();
                }
                else { var p = current.Feed(o, 900, 500); dx = p.Dx; dy = p.Dy; }
                x += dx; y += dy; path += Math.Sqrt(dx * dx + dy * dy);
                if (t >= 1.5) tail += Math.Sqrt(dx * dx + dy * dy);
                positions.Add((t, x, y));
            }
            double length = Math.Sqrt(targetX * targetX + targetY * targetY);
            var travel = positions.Where(p => p.Time <= 1).ToArray();
            double cross = length == 0 ? 0 : Math.Sqrt(travel.Average(p => Math.Pow((p.X * targetY - p.Y * targetX) / length, 2)));
            var hold = positions.Where(p => p.Time >= 1.5).ToArray();
            double mx = hold.Average(p => p.X), my = hold.Average(p => p.Y);
            double rms = Math.Sqrt(hold.Average(p => Math.Pow(p.X - mx, 2) + Math.Pow(p.Y - my, 2)));
            results.Add(new { implementation = previous ? "previous-qadr" : "analog-qadr", fps, scenario,
                crossTrackRmsPx = cross, endpointErrorPx = Math.Sqrt(Math.Pow(x - targetX, 2) + Math.Pow(y - targetY, 2)),
                holdRmsPx = rms, postStopPathPx = tail, totalPathPx = path, dx = x, dy = y });
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { synthetic = true,
            input = "identical capture-timed pose inputs; smoothing/gains unchanged except vector mapping",
            meaning = "controller regression evidence, not live comfort or accuracy improvement", results }, Protocol.Json));
        Console.WriteLine($"Compared {results.Count / 2} matched controller scenarios; no camera or native input. {destination}");
    }
}
