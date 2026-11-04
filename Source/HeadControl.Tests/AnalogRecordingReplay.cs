using Qadr.Core;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

/// <summary>Open-loop comparison only: the user cannot correct a changed path.</summary>
internal static class AnalogRecordingReplay
{
    private sealed record Frame(double Time, double Dx, double Dy);
    public static void Run(string previousCore, string trace, string report, string destination)
    {
        using var recorded = JsonDocument.Parse(File.ReadAllText(report));
        var rows = File.ReadAllLines(trace).Select(line => JsonDocument.Parse(line)).ToArray();
        var previousAssembly = new AssemblyLoadContext("analog-replay-" + Guid.NewGuid(), true).LoadFromAssemblyPath(Path.GetFullPath(previousCore));
        var outputs = new List<object>();
        foreach (bool previous in new[] { true, false })
        {
            var current = new MotionController();
            Type type = previousAssembly.GetType("Qadr.Core.MotionController")!;
            object legacy = Activator.CreateInstance(type)!;
            object Convert(string name, object value) => JsonSerializer.Deserialize(JsonSerializer.Serialize(value, Protocol.Json), previousAssembly.GetType("Qadr.Core." + name)!, Protocol.Json)!;
            var frames = new List<Frame>();
            foreach (var row in rows)
            {
                var e = row.RootElement;
                string kind = e.GetProperty("kind").GetString()!;
                if (kind == "configuration")
                {
                    var profile = e.GetProperty("profile").Deserialize<Profile>(Protocol.Json)!;
                    var settings = e.GetProperty("settings").Deserialize<Settings>(Protocol.Json)! with
                        { ClickMethod = "none", RightClickMethod = "none", EyebrowDoubleClick = false, PuckerDrag = false, MouthScroll = false };
                    var model = e.GetProperty("personalModel").Deserialize<RestNoiseModel>(Protocol.Json)!;
                    double width = e.GetProperty("width").GetDouble(), height = e.GetProperty("height").GetDouble();
                    current.SetProfile(profile); current.Settings = settings; current.PersonalModel = model; current.Width = width; current.Height = height;
                    if (previous)
                    {
                        type.GetMethod("SetProfile")!.Invoke(legacy, [Convert("Profile", profile)]);
                        type.GetProperty("Settings")!.SetValue(legacy, Convert("Settings", settings));
                        type.GetProperty("PersonalModel")!.SetValue(legacy, Convert("RestNoiseModel", model));
                        type.GetProperty("Width")!.SetValue(legacy, width); type.GetProperty("Height")!.SetValue(legacy, height);
                    }
                }
                if (kind != "sample") continue;
                var face = e.GetProperty("face").Deserialize<Observation>(Protocol.Json)!;
                var actual = e.GetProperty("output").Deserialize<PointerOutput>(Protocol.Json)!;
                double dx, dy;
                if (previous)
                {
                    var p = JsonSerializer.SerializeToElement(type.GetMethod("Feed")!.Invoke(legacy,
                        [Convert("Observation", face), actual.ClickX, actual.ClickY, false]));
                    dx = p.GetProperty("Dx").GetDouble(); dy = p.GetProperty("Dy").GetDouble();
                }
                else { var p = current.Feed(face, actual.ClickX, actual.ClickY); dx = p.Dx; dy = p.Dy; }
                frames.Add(new(face.Timestamp, dx, dy));
            }
            var trials = new List<object>();
            foreach (var trial in recorded.RootElement.GetProperty("trials").EnumerateArray())
            {
                var samples = trial.GetProperty("samples").Deserialize<MovementSample[]>(Protocol.Json)!;
                if (samples.Length == 0) continue;
                double start = samples[0].Time, end = samples[^1].Time;
                var holdSamples = samples.Where(s => s.Phase == "hold").ToArray();
                double holdStart = holdSamples.Length > 0 ? holdSamples[0].Time : double.PositiveInfinity;
                double x = 0, y = 0; var hold = new List<(double Time, double X, double Y)>();
                foreach (var frame in frames.Where(f => f.Time >= start && f.Time <= end))
                {
                    x += frame.Dx; y += frame.Dy;
                    if (frame.Time >= holdStart) hold.Add((frame.Time, x, y));
                }
                double? rms = null, drift = null;
                if (hold.Count >= 10)
                {
                    double mx = hold.Average(p => p.X), my = hold.Average(p => p.Y);
                    rms = Math.Sqrt(hold.Average(p => Math.Pow(p.X - mx, 2) + Math.Pow(p.Y - my, 2)));
                    var first = hold.Where(p => p.Time <= hold[0].Time + .25).ToArray();
                    var last = hold.Where(p => p.Time >= hold[^1].Time - .25).ToArray();
                    drift = Math.Sqrt(Math.Pow(first.Average(p => p.X) - last.Average(p => p.X), 2) + Math.Pow(first.Average(p => p.Y) - last.Average(p => p.Y), 2));
                }
                trials.Add(new { id = trial.GetProperty("id").GetString(), replayDx = x, replayDy = y,
                    replayHoldRmsPx = rms, replayHoldDriftPx = drift, holdPoseSamples = hold.Count });
            }
            outputs.Add(new { implementation = previous ? "previous-qadr" : "analog-qadr", frames = frames.Count, trials });
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { recording = Path.GetFileName(report),
            synthetic = false, offlineReplay = true, nativeInput = false,
            limitation = "Integrated controller displacement on historical poses, without native interpolation or new user corrections. Not measured live cursor accuracy or a prediction of usability.", outputs }, Protocol.Json));
        foreach (var row in rows) row.Dispose();
        Console.WriteLine("Matched historical-pose replay complete; no camera, input or settings changes.");
    }
}
