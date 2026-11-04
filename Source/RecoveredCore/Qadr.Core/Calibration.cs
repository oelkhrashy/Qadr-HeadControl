using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

/// <summary>Guided capture-timed collection through Qadr's existing feedback UI.</summary>
public sealed class Calibration
{
    private sealed record Stage(string Title, string Help, double Seconds, int Axis);
    private static readonly Stage[] stages =
    [
        new("Move gently right", "Turn or shift gently to your right and pause comfortably. Do not stretch.", 3, 0),
        new("Relax at your starting position", "Return to your comfortable position and relax.", 1, -1),
        new("Move gently left", "Turn or shift gently to your left and pause comfortably. Do not stretch.", 3, 0),
        new("Relax at your starting position", "Return to your comfortable position and relax.", 1, -1),
        new("Look gently up", "Move gently up, only as far as is comfortable. Keep your whole face visible.", 3, 1),
        new("Relax at your starting position", "Return to your comfortable position and relax.", 1, -1),
        new("Look gently down", "Move gently down, only as far as is comfortable. Keep your whole face visible.", 3, 1),
        new("Relax at your starting position", "Return to your comfortable position and relax.", 1, -1),
        new("Repeat a small move right", "Repeat a comfortable move right. We are checking consistency.", 2, 0),
        new("Relax at your starting position", "Return to your comfortable position and relax.", .8, -1),
        new("Repeat a small move left", "Repeat a comfortable move left. We are checking consistency.", 2, 0),
        new("Relax at your starting position", "Return to your comfortable position and relax.", .8, -1),
        new("Repeat a small move up", "Repeat a comfortable move up. We are checking consistency.", 2, 1),
        new("Relax at your starting position", "Return to your comfortable position and relax.", .8, -1),
        new("Repeat a small move down", "Repeat a comfortable move down. We are checking consistency.", 2, 1)
    ];
    private readonly List<Observation> restSamples = [];
    private readonly List<Observation>[] movement = [[], []];
    private readonly List<Observation> pending = [];
    private readonly List<(int Axis, Observation[] Samples)> passes = [];
    private Profile? rest;
    private double[] baseline = [.003, .003, .01, .01];
    private double[] rms = [], p95 = [];
    private double? previous, started, phaseStarted;
    private int stage;
    private bool exploring;
    private CalibrationFeedback last = new("Find your relaxed position", "Look toward the screen and relax.", 0);
    public bool Resting => rest is null;
    public string Cue => Resting || (!exploring && stage < stages.Length && stages[stage].Axis < 0) ? "rest" :
        !exploring && stage < stages.Length && stages[stage].Axis == 1 ? "vertical" : "sides";
    public double SmileRest { get; private set; }
    public double MouthRest { get; private set; }
    public double BlinkRest { get; private set; }

    public void Lost()
    {
        previous = phaseStarted = null;
        pending.Clear();
        if (rest is null) { restSamples.Clear(); started = null; }
    }

    public CalibrationFeedback Feed(Observation o)
    {
        if (!o.IsUsable) { Lost(); return last = new("Waiting for tracking", "Bring your whole face back into view, then relax.", last.Progress); }
        if (previous.HasValue && o.Timestamp <= previous.Value) return last;
        if (previous.HasValue && o.Timestamp - previous.Value > .5) Lost();
        previous = o.Timestamp;
        started ??= o.Timestamp;
        if (rest is null) return LearnRest(o);
        phaseStarted ??= o.Timestamp;
        if (!exploring && stage < stages.Length)
        {
            var current = stages[stage];
            double elapsed = o.Timestamp - phaseStarted.Value;
            if (current.Axis >= 0 && elapsed >= .35) pending.Add(o);
            if (elapsed >= current.Seconds)
            {
                if (current.Axis >= 0) { movement[current.Axis].AddRange(pending); passes.Add((current.Axis, pending.ToArray())); }
                pending.Clear(); stage++; phaseStarted = o.Timestamp;
                if (stage == stages.Length)
                {
                    var learned = Finish(o);
                    if (learned is not null) return last = learned;
                    exploring = true;
                }
            }
            if (!exploring && stage < stages.Length)
            {
                current = stages[stage];
                double done = stages.Take(stage).Sum(s => s.Seconds);
                double part = Math.Min(current.Seconds, o.Timestamp - phaseStarted.Value);
                return last = Feedback(o, current.Title, current.Help, .2 + .7 * (done + part) / stages.Sum(s => s.Seconds));
            }
        }
        // Keep useful measurements and allow gentle exploration of a missing axis.
        // Do not manufacture a completed profile from a motionless face.
        movement[0].Add(o); movement[1].Add(o);
        foreach (var observations in movement)
            if (observations.Count > 2048) observations.RemoveRange(0, observations.Count - 2048);
        var result = Finish(o);
        if (result is not null) return last = result;
        return last = Feedback(o, "Try a comfortable movement",
            "Try gentle movement to either side and up or down. You can stop and keep your previous profile if this is uncomfortable.", .9);
    }

    private CalibrationFeedback LearnRest(Observation o)
    {
        restSamples.Add(o);
        restSamples.RemoveAll(s => o.Timestamp - s.Timestamp > 5.2);
        bool stable = restSamples.Count >= 50 && Enumerable.Range(0, 4).All(i =>
            Spread(restSamples.Select(s => Channels(s, null)[i])) < (i < 2 ? .06 : .03));
        double duration = restSamples.Count > 1 ? o.Timestamp - restSamples[0].Timestamp : 0;
        if (stable && duration >= 5)
        {
            rest = new Profile { CoordinateVersion = 3,
                Neutral = [Median(restSamples.Select(s => s.Yaw)), Median(restSamples.Select(s => s.Pitch))],
                Center = [Median(restSamples.Select(s => s.X)), Median(restSamples.Select(s => s.Y))],
                Scale = Median(restSamples.Select(s => s.Scale)) };
            var channels = Enumerable.Range(0, 4).Select(i => restSamples.Select(s => Channels(s, rest)[i]).ToArray()).ToArray();
            baseline = channels.Select((values, i) => Math.Max(Noise(values), i < 2 ? .002 : .006)).ToArray();
            rms = channels.Select(values => { double centre = Median(values); return Math.Sqrt(values.Average(v => (v - centre) * (v - centre))); }).ToArray();
            p95 = channels.Select(values => { double centre = Median(values); return Percentile(values.Select(v => Math.Abs(v - centre)), .95); }).ToArray();
            SmileRest = Percentile(restSamples.Select(s => s.Smile), .95);
            MouthRest = Percentile(restSamples.Select(s => s.Mouth), .95);
            BlinkRest = Percentile(restSamples.Select(s => s.Blink), .8);
            phaseStarted = o.Timestamp;
            return last = Feedback(o, stages[0].Title, stages[0].Help, .2);
        }
        return last = new("Find your relaxed position", stable ? "Rest comfortably and look toward the screen for a few seconds." :
            "Qadr is measuring your natural movement. Relax without forcing yourself still.", Math.Min(.19, duration / 5 * .19));
    }

    private CalibrationFeedback? Finish(Observation o)
    {
        string[] modes = new string[2]; double[][] ranges = new double[2][], measured = new double[2][];
        double[] noises = new double[2], consistency = new double[4]; int[] counts = new int[4]; var missing = new List<string>();
        for (int axis = 0; axis < 2; axis++)
        {
            var rotation = movement[axis].Select(s => Channels(s, rest)[axis]).ToArray();
            var translation = movement[axis].Select(s => Channels(s, rest)[axis + 2]).ToArray();
            double Strength(double[] values, int channel) => Percentile(values.Select(Math.Abs), .85) / baseline[channel];
            bool useShift = Percentile(rotation.Select(Math.Abs), .85) < Math.Max(.035, baseline[axis] * 5) &&
                Strength(translation, axis + 2) > Strength(rotation, axis) * 1.8;
            int best = useShift ? axis + 2 : axis;
            double[] values = useShift ? translation : rotation;
            modes[axis] = useShift ? "shift" : "turn"; noises[axis] = baseline[best];
            double floor = Math.Max(baseline[best] * 4, useShift ? .025 : .01);
            var negative = values.Where(v => v < -floor).Select(v => -v).ToArray();
            var positive = values.Where(v => v > floor).ToArray();
            double lo = negative.Length >= 8 ? Percentile(negative, .8) : 0;
            double hi = positive.Length >= 8 ? Percentile(positive, .8) : 0;
            for (int sign = 0; sign < 2; sign++)
            {
                var attempts = passes.Where(p => p.Axis == axis).Select(p => p.Samples
                    .Select(s => Channels(s, rest)[best] * (sign == 0 ? -1 : 1)).Where(v => v > floor).ToArray())
                    .Where(a => a.Length >= 8).Select(a => Percentile(a, .8)).ToArray();
                consistency[axis * 2 + sign] = attempts.Length >= 2 ? attempts.Min() / attempts.Max() : 0;
            }
            measured[axis] = [lo, hi]; counts[axis * 2] = negative.Length; counts[axis * 2 + 1] = positive.Length;
            if (lo == 0) missing.Add(axis == 0 ? "left" : "up");
            if (hi == 0) missing.Add(axis == 0 ? "right" : "down");
            double observed = Math.Max(lo, hi);
            if (observed <= floor) return null;
            // Existing one-sided accessibility fallback, now explicitly labelled.
            ranges[axis] = [Math.Clamp(lo > 0 ? lo : observed, floor, 5), Math.Clamp(hi > 0 ? hi : observed, floor, 5)];
        }
        var profile = (rest! with { Modes = modes, Ranges = ranges, Noise = noises,
            CalibrationDetails = new CalibrationDetails { RestRms = rms, RestP95 = p95,
                MeasuredRanges = measured, DirectionSamples = counts,
                RepeatConsistency = consistency,
                UnmeasuredDirections = missing.ToArray(), RestSamples = restSamples.Count,
                MovementSamples = movement.Sum(m => m.Count), CaptureSeconds = o.Timestamp - started!.Value } }).WithBalancedDirections();
        return new("Your analog movement is learned", missing.Count == 0 ?
            "Both directions of both axes were measured. Return to your relaxed position." :
            "Your usable movement was learned. Unmeasured directions use a balanced fallback; you can repeat calibration later.", 1, Profile: profile);
    }

    private CalibrationFeedback Feedback(Observation o, string title, string help, double progress)
    {
        var values = Channels(o, rest);
        return new(title, help, progress, Math.Clamp(values[0] / .16, -1, 1), Math.Clamp(values[1] / .16, -1, 1));
    }
    private static double[] Channels(Observation o, Profile? p) => p is null ?
        [o.Yaw, -o.Pitch, o.X, o.Y] : [o.Yaw - p.Neutral[0], p.Neutral[1] - o.Pitch,
        (o.X - p.Center[0]) / o.Scale, (o.Y - p.Center[1]) / o.Scale];
    public static double Percentile(IEnumerable<double> values, double fraction)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0 ? 0 : ordered[Math.Clamp((int)((ordered.Length - 1) * fraction), 0, ordered.Length - 1)];
    }
    public static double Median(IEnumerable<double> values) => Percentile(values, .5);
    private static double Spread(IEnumerable<double> values) => Percentile(values, .9) - Percentile(values, .1);
    private static double Noise(IEnumerable<double> values) { double centre = Median(values); return Median(values.Select(v => Math.Abs(v - centre))) * 1.4826; }
}
