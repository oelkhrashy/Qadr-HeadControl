using Qadr.Core;
using System.Text.Json;

internal static class AnalogMovementTests
{
    private static Observation O(double t, double yaw = 0, double pitch = 0) => new(yaw, pitch, 0, 0, 0, t, LeftEye: 0, RightEye: 0);
    private static MotionController Controller() { var c = new MotionController { Settings = new Settings { ClickMethod = "none", RightClickMethod = "none" } }; c.SetProfile(new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .015] }); return c; }
    public static void Run(Action<bool, string> check)
    {
        foreach (int fps in new[] { 10, 30, 60 })
        foreach (double slope in new[] { .05, .2, 1.0, 4.0 })
        {
            var c = Controller(); double x = 0, y = 0, error = 0;
            double expected = slope * 1080 / 1920;
            for (int i = 0; i <= fps * 2; i++)
            {
                double progress = Math.Min(i / (double)fps, 1);
                var p = c.Feed(O(i / (double)fps, .02 * progress, -.02 * progress * slope), 900, 500);
                x += p.Dx; y += p.Dy;
                error = Math.Max(error, Math.Abs(y - x * expected) / Math.Sqrt(1 + expected * expected));
            }
            check(x > 100 && y > 0 && error < 8,
                $"analog vector preserves a {slope:G} slope with unequal axis noise at {fps} FPS (max cross-track {error:F8}px)");
        }
        {
            var c = Controller(); double late = 0;
            for (int i = 0; i < 120; i++)
            {
                var p = c.Feed(O(i / 30.0, .02 * Math.Min(1, i / 30.0), -.01 * Math.Min(1, i / 30.0)), 900, 500);
                if (i > 65) late += Math.Abs(p.Dx) + Math.Abs(p.Dy);
            }
            check(late < 1e-8, "analog hold at a non-neutral angle stops without returning to centre");
        }
        CalibrationTests(check);
    }

    private static Profile? Calibrate(bool shift = false, bool omitDown = false)
    {
        var calibration = new Calibration(); CalibrationFeedback feedback = new("", "", 0);
        for (int i = 0; i < 1600; i++)
        {
            string title = feedback.Title.ToLowerInvariant();
            double yaw = title.Contains("right") ? .08 : title.Contains("left") ? -.06 : 0;
            double pitch = title.Contains("up") ? .05 : title.Contains("down") ? (omitDown ? .05 : -.07) : 0;
            var observation = O(i / 30.0, shift ? 0 : yaw, shift ? 0 : pitch) with
                { X = .5 + (shift ? yaw * .2 : 0), Y = .5 - (shift ? pitch * .2 : 0) };
            feedback = calibration.Feed(observation);
            if (feedback.Profile is not null) return feedback.Profile;
        }
        return null;
    }

    private static void CalibrationTests(Action<bool, string> check)
    {
        var p = Calibrate();
        check(p is { IsValid: true, CalibrationDetails: not null }, "guided analog calibration completes in the existing feedback contract");
        var details = p!.CalibrationDetails!;
        check(details.DirectionSamples.Length == 4 && details.DirectionSamples.All(n => n >= 8) && details.UnmeasuredDirections.Length == 0,
            "calibration genuinely collects left, right, up and down rather than inferring down from up");
        check(Math.Abs(details.MeasuredRanges[1][0] - .05) < 1e-6 && Math.Abs(details.MeasuredRanges[1][1] - .07) < 1e-6,
            "different comfortable up/down ranges are retained as measured evidence");
        check(details.RestSamples >= 50 && details.RestP95.Length == 4 && details.RestRms.Length == 4 && details.CaptureSeconds > 25,
            "profile records resting noise, sample counts and the full repeated collection duration");
        check(details.RepeatConsistency.Length == 4 && details.RepeatConsistency.All(v => v > .99),
            "repeat consistency is measured separately for all four directions");
        var restored = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p))!;
        check(restored.IsValid && restored.CalibrationDetails!.DirectionSamples.SequenceEqual(details.DirectionSamples),
            "new calibration evidence survives save/reload");
        var legacy = JsonSerializer.Deserialize<Profile>("{\"CoordinateVersion\":3}")!;
        check(legacy.IsValid && legacy.CalibrationDetails is null, "existing profiles remain usable without new calibration metadata");
        var translation = Calibrate(shift: true);
        check(translation is { IsValid: true } && translation.Modes.All(m => m == "shift"),
            "guided collection retains translation control for people with little head rotation");
        var oneSided = Calibrate(omitDown: true);
        check(oneSided is { IsValid: true } && oneSided.CalibrationDetails!.UnmeasuredDirections.Contains("down") &&
            oneSided.CalibrationDetails.MeasuredRanges[1][1] == 0,
            "a usable one-sided fallback explicitly labels unmeasured down instead of claiming it was measured");
        var c = new Calibration(); CalibrationFeedback f = new("", "", 0);
        for (int i = 0; i < 1800; i++) f = c.Feed(O(i / 30.0));
        check(f.Profile is null, "a motionless face cannot manufacture a completed movement profile");
        c = new Calibration();
        for (int i = 0; i <= 180; i++) f = c.Feed(O(i / 30.0));
        for (int i = 0; i < 1000; i++) f = c.Feed(O(6, .08));
        check(f.Profile is null && f.Title.Contains("right", StringComparison.OrdinalIgnoreCase),
            "duplicate capture timestamps cannot finish a calibration stage");
        f = c.Feed(O(9, .08));
        check(f.Profile is null && f.Title.Contains("right", StringComparison.OrdinalIgnoreCase),
            "a tracking gap restarts the interrupted calibration stage rather than counting the missing time");
    }
}
