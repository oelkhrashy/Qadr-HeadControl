using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public sealed record ExpressionRest(double Smile, double SmileNoise, double Brow, double BrowNoise, double Pucker, double PuckerNoise, double Mouth, double MouthNoise, double Eye = 0.0, double EyeNoise = 0.0)
{
	public static ExpressionRest? From(IEnumerable<Observation> observations)
	{
		Observation[] samples = observations.Where((Observation o) => o.IsUsable).ToArray();
		if (samples.Length < 10 || samples[^1].Timestamp - samples[0].Timestamp < 0.3)
		{
			return null;
		}
		(double, double) tuple = Measure((Observation o) => o.Smile);
		(double, double) tuple2 = Measure((Observation o) => o.Eyebrow);
		(double, double) tuple3 = Measure((Observation o) => o.Pucker);
		(double, double) tuple4 = Measure((Observation o) => o.Mouth);
		(double, double) tuple5 = Measure((Observation o) => Math.Max(o.Blink, Math.Max(o.LeftEye, o.RightEye)));
		return new ExpressionRest(tuple.Item1, tuple.Item2, tuple2.Item1, tuple2.Item2, tuple3.Item1, tuple3.Item2, tuple4.Item1, tuple4.Item2, tuple5.Item1, tuple5.Item2);
		(double Level, double Noise) Measure(Func<Observation, double> channel)
		{
			double rest = Calibration.Median(samples.Select(channel));
			return (Level: rest, Noise: 1.4826 * Calibration.Median(samples.Select((Observation o) => Math.Abs(channel(o) - rest))));
		}
	}

	public Settings Apply(Settings saved)
	{
		return saved with
		{
			EyeRestLevel = Eye,
			BlinkThreshold = Eye + Math.Max(0.12, Math.Max(EyeNoise * 5.0, Math.Min(saved.BlinkThreshold - saved.EyeRestLevel, (1.0 - Eye) * 0.7))),
			SmileRestLevel = Smile,
			SmileThreshold = Threshold(Smile, SmileNoise, saved.SmileRestLevel, saved.SmileThreshold, 0.04),
			EyebrowRestLevel = Brow,
			EyebrowThreshold = Threshold(Brow, BrowNoise, saved.EyebrowRestLevel, saved.EyebrowThreshold, 0.08),
			PuckerRestLevel = Pucker,
			PuckerThreshold = Threshold(Pucker, PuckerNoise, saved.PuckerRestLevel, saved.PuckerThreshold, 0.06),
			ScrollGestureRestLevel = Mouth,
			ScrollGestureThreshold = Threshold(Mouth, MouthNoise, saved.ScrollGestureRestLevel, saved.ScrollGestureThreshold, 0.08),
			MouthRestLevel = Mouth,
			MouthThreshold = Threshold(Mouth, MouthNoise, saved.MouthRestLevel, saved.MouthThreshold, 0.08)
		};
		static double Threshold(double rest, double noise, double oldRest, double oldThreshold, double floor)
		{
			return rest + Math.Max(floor, Math.Max(oldThreshold - oldRest, noise * 5.0));
		}
	}
}
