using Qadr.Core;

/// <summary>StillnessPark: steady when still, free when moving.</summary>
static class StillnessParkTests
{
    static MotionController Control(bool park)
    {
        var c = new MotionController { Settings = new Settings { ClickMethod = "none", RightClickMethod = "none", ParkWhenStill = park } };
        c.SetProfile(new Profile { CoordinateVersion = 3, Ranges = [[.4, .4], [.2, .2]], Noise = [.002, .002],
            CalibrationDetails = RestEvidence() });
        return c;
    }

    // What calibration's rest step would measure from this same sway: 5 s
    // at 30 fps, speed over 0.2 s and spread, normalized by the ranges.
    static CalibrationDetails RestEvidence()
    {
        double[] ranges = [.4, .2]; double[] speed = new double[2], sway = new double[2];
        for (int axis = 0; axis < 2; axis++)
        {
            var v = Enumerable.Range(0, 150).Select(i => i / 30.0)
                .Select(t => (T: t, V: (axis == 0 ? SwayYaw(t) : SwayPitch(t)) / ranges[axis])).ToArray();
            var sp = new List<double>();
            for (int i = 0; i + 6 < v.Length; i++) sp.Add(Math.Abs(v[i + 6].V - v[i].V) / (v[i + 6].T - v[i].T));
            speed[axis] = Calibration.Percentile(sp, .95);
            double m = v.Average(p => p.V);
            sway[axis] = Math.Sqrt(v.Average(p => (p.V - m) * (p.V - m)));
        }
        return new CalibrationDetails { RestSpeedP95 = speed, RestSway = sway };
    }

    // a resting head: slow sway (0.6 deg, ~0.15 Hz) plus breathing on pitch
    static double SwayYaw(double t) => .0105 * Math.Sin(2 * Math.PI * .12 * t) + .002 * Math.Sin(2 * Math.PI * 1.1 * t);
    static double SwayPitch(double t) => .013 * Math.Sin(2 * Math.PI * .25 * t + 1) + .002 * Math.Sin(2 * Math.PI * .9 * t);

    static (double Moved, double Reach) Run(bool park, int fps)
    {
        var c = Control(park); double x = 960, y = 540, moved = 0;
        for (int i = 0; i <= 20 * fps; i++)
        {
            double t = (double)i / fps;
            var p = c.Feed(new Observation(SwayYaw(t), SwayPitch(t), 0, 0, 0, t), x, y);
            if (t > 2) moved += Math.Abs(p.Dx) + Math.Abs(p.Dy);
            x += p.Dx; y += p.Dy;
        }
        double startX = x;
        for (int i = 1; i <= fps; i++)        // a deliberate 12 deg turn in 1 s
        {
            double t = 20 + (double)i / fps;
            var p = c.Feed(new Observation(SwayYaw(t) + .21 * i / fps, SwayPitch(t), 0, 0, 0, t), x, y);
            x += p.Dx; y += p.Dy;
        }
        for (int i = 1; i <= fps; i++)
        {
            double t = 21 + (double)i / fps;
            var p = c.Feed(new Observation(SwayYaw(t) + .21, SwayPitch(t), 0, 0, 0, t), x, y);
            x += p.Dx; y += p.Dy;
        }
        return (moved / 18.0, x - startX);
    }

    public static void Run(Action<bool, string> check)
    {
        foreach (int fps in new[] { 10, 30 })
        {
            var off = Run(false, fps); var on = Run(true, fps);
            check(on.Moved < off.Moved * .4,
                $"park @{fps}fps: resting sway moves the pointer {on.Moved:0.0} px/s with parking vs {off.Moved:0.0} without");
            check(on.Reach > off.Reach * .85 && on.Reach > 50,
                $"park @{fps}fps: a deliberate turn still travels ({on.Reach:0} px vs {off.Reach:0} px)");
        }
        var s = new StillnessPark();
        s.Configure(new Profile { CoordinateVersion = 3, Ranges = [[.4, .4], [.2, .2]], Noise = [.002, .002] });
        check(s.ReleaseSpeed.All(v => v >= .06) && s.DriftLimit.All(v => v >= .03),
            "park: a profile without rest-speed evidence gets safe defaults");
        s.Configure(new Profile { CoordinateVersion = 3, Ranges = [[.4, .4], [.2, .2]], Noise = [.002, .002],
            CalibrationDetails = new CalibrationDetails { RestSpeedP95 = [.1, .2], RestSway = [.03, .05] } });
        check(Math.Abs(s.ReleaseSpeed[0] - .15) < 1e-9 && Math.Abs(s.DriftLimit[1] - .1) < 1e-9,
            "park: thresholds come from this person's calibration");
        var q = new StillnessPark(); q.Configure(new Profile { CoordinateVersion = 3, Ranges = [[.4, .4], [.2, .2]], Noise = [.002, .002] });
        for (int i = 0; i < 20; i++) q.Feed(i / 30.0, 0, 0);
        bool parked = q.Parked;
        q.Feed(20 / 30.0, .5, 0);                       // a one-frame glitch
        var r = q.Feed(21 / 30.0, 0, 0);                // gone again
        check(parked && Math.Abs(r.X) < 1e-9, "park: a one-frame glitch leaves no lasting offset");
        q = new StillnessPark(); q.Configure(new Profile { CoordinateVersion = 3, Ranges = [[.4, .4], [.2, .2]], Noise = [.002, .002] });
        for (int i = 0; i < 20; i++) q.Feed(i / 30.0, 0, 0);
        for (int i = 20; i < 40; i++) q.Feed(i / 30.0, .025 * (i - 19) / 20, 0);   // slow sway while parked
        double held = q.Feed(40 / 30.0, .025, 0).X;
        r = q.Feed(41 / 30.0, .1, 0);                   // then a real, fast move
        check(q.Parked == false && Math.Abs(held) < 1e-9 && Math.Abs(r.X - .075) < 1e-9,
            "park: sway while parked is dropped, the move itself comes through without a jump");
    }
}
