using Qadr.Core;

/// <summary>Checks for the fixes from the HeadMouse review (headmouse-python branch).</summary>
static class ReviewFixTests
{
    static Observation O(double t, double left = 0, double right = 0, double yaw = 0) =>
        new(yaw, 0, 0, 0, Math.Min(left, right), t, LeftEye: left, RightEye: right);

    static MotionController Control(Settings s)
    {
        var c = new MotionController { Settings = s };
        c.SetProfile(new Profile { CoordinateVersion = 3, Ranges = [[.16, .16], [.16, .16]], Noise = [.002, .002] });
        return c;
    }

    static int Winks(Settings s, bool both)
    {
        var c = Control(s); int right = 0;
        for (int i = 0; i < 180; i++)
        {
            double t = i / 60.0; bool closed = t >= .5 && t < 1.0;
            if (c.Feed(O(t, closed ? .9 : 0, closed && both ? .9 : 0), 900, 500).Click == "right") right++;
        }
        return right;
    }

    public static void Run(Action<bool, string> check)
    {
        var unlearned = new Settings { ClickMethod = "none" };
        check(unlearned.RightClickMethod == "either" && !unlearned.EyesLearned,
            "review: default settings start with an unlearned wink");
        check(Winks(unlearned, both: false) == 0, "review: an unlearned wink never right-clicks");
        var learned = unlearned with { EyesLearned = true };
        check(Winks(learned, both: false) == 1, "review: a learned one-eyed wink right-clicks once");
        check(Winks(learned, both: true) == 0, "review: a slow both-eye blink never right-clicks in either-eye mode");
        check(Winks(learned with { RightClickMethod = "both" }, both: true) == 1,
            "review: both-eye mode still works when chosen explicitly");

        var dwell = new Settings { ClickMethod = "dwell", RightClickMethod = "none", DwellSeconds = .6 };
        var c = Control(dwell); int left = 0;
        c.DisarmDwellUntilMoved();
        for (int i = 0; i < 120; i++) if (c.Feed(O(i / 30.0), 960, 540).Click == "left") left++;
        check(left == 0, "review: dwell never clicks where Qadr itself moved the pointer");
        double x = 960;
        for (int i = 120; i < 150; i++) { x += 12; if (c.Feed(O(i / 30.0), x, 540).Click == "left") left++; }
        for (int i = 150; i < 240; i++) if (c.Feed(O(i / 30.0), x, 540).Click == "left") left++;
        check(left == 1, "review: dwell works again once the user has moved the pointer");

        check(RoutineSafety.NeedsConfirmation("OK") && RoutineSafety.NeedsConfirmation("Remove item")
              && RoutineSafety.NeedsConfirmation("Transfer") && RoutineSafety.NeedsConfirmation("")
              && RoutineSafety.NeedsConfirmation("موافق"),
            "review: OK, Remove, Transfer, unnamed buttons and Arabic confirmations need confirming");
        check(!RoutineSafety.NeedsConfirmation("File") && !RoutineSafety.NeedsConfirmation("Next page"),
            "review: ordinary navigation is not interrupted");

        var p = new WorkerRestartPolicy();
        check(p.Allow(0) && !p.Allow(2) && p.Allow(10) && p.Allow(20) && !p.Allow(30) && p.Allow(700),
            "review: the face tracker can restart more than once, but not endlessly");
    }
}
