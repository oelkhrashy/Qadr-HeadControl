using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public sealed class Calibration
{
	private readonly List<Observation> samples = new List<Observation>();

	private Profile? rest;

	private double[] baseline = new double[4] { 0.003, 0.003, 0.01, 0.01 };

	private double? started;

	private double? previous;

	public bool Resting => (object)rest == null;

	public double SmileRest { get; private set; }

	public double MouthRest { get; private set; }

	public double BlinkRest { get; private set; }

	public void Lost()
	{
		samples.Clear();
		started = (previous = null);
	}

	public CalibrationFeedback Feed(Observation o)
	{
		if (previous.HasValue && o.Timestamp - previous > 0.5)
		{
			Lost();
		}
		previous = o.Timestamp;
		double valueOrDefault = started.GetValueOrDefault();
		if (!started.HasValue)
		{
			valueOrDefault = o.Timestamp;
			started = valueOrDefault;
		}
		samples.Add(o);
		samples.RemoveAll((Observation s) => o.Timestamp - s.Timestamp > (((object)rest == null) ? 5.2 : 12.0));
		double num = o.Timestamp - started.Value;
		if ((object)rest == null)
		{
			bool flag = samples.Count >= 50 && new int[4] { 0, 1, 2, 3 }.All((int i) => Spread(samples.Select((Observation s) => Channels(s, null)[i])) < ((i < 2) ? 0.06 : 0.03));
			List<Observation> list = samples;
			if ((list[list.Count - 1].Timestamp - samples[0].Timestamp >= 5.0) & flag)
			{
				rest = new Profile
				{
					CoordinateVersion = 3,
					Neutral = new double[2]
					{
						Median(samples.Select((Observation s) => s.Yaw)),
						Median(samples.Select((Observation s) => s.Pitch))
					},
					Center = new double[2]
					{
						Median(samples.Select((Observation s) => s.X)),
						Median(samples.Select((Observation s) => s.Y))
					},
					Scale = Median(samples.Select((Observation s) => s.Scale))
				};
				baseline = (from i in Enumerable.Range(0, 4)
					select Math.Max(Noise(samples.Select((Observation s) => Channels(s, rest)[i])), (i < 2) ? 0.002 : 0.006)).ToArray();
				SmileRest = Percentile(samples.Select((Observation s) => s.Smile), 0.95);
				MouthRest = Percentile(samples.Select((Observation s) => s.Mouth), 0.95);
				BlinkRest = Percentile(samples.Select((Observation s) => s.Blink), 0.8);
				samples.Clear();
				started = o.Timestamp;
				return new CalibrationFeedback("Explore your comfortable movement", "Move gently in any direction. Small movements are enough. Stop if you feel tired.", 0.25);
			}
			return new CalibrationFeedback("Find your relaxed position", flag ? "Rest comfortably and look toward the screen for a few seconds." : "Qadr is measuring your natural movement. Relax without forcing yourself still.", Math.Min(0.24, num / 5.0 * 0.24));
		}
		double[][] channels = samples.Select((Observation s) => Channels(s, rest)).ToArray();
		string[] array = new string[2];
		double[][] array2 = new double[2][];
		double[] array3 = new double[2];
		bool[] array4 = new bool[2];
		int axis;
		for (axis = 0; axis < 2; axis++)
		{
			bool flag2 = Math.Max(Math.Abs(Percentile(channels.Select((double[] p) => p[axis]), 0.08)), Math.Abs(Percentile(channels.Select((double[] p) => p[axis]), 0.92))) >= Math.Max(0.035, baseline[axis] * 5.0);
			int best = ((!flag2 && Strength(axis + 2) > Strength(axis) * 1.8) ? (axis + 2) : axis);
			double num2 = Math.Abs(Math.Min(0.0, Percentile(channels.Select((double[] p) => p[best]), 0.08)));
			double num3 = Math.Max(0.0, Percentile(channels.Select((double[] p) => p[best]), 0.92));
			double num4 = Math.Max(baseline[best] * 4.0, (best < 2) ? 0.01 : 0.025);
			array4[axis] = Math.Min(num2, num3) >= num4 || (num >= 10.0 && Math.Max(num2, num3) >= num4);
			double num5 = Math.Max(num2, num3);
			array2[axis] = new double[2]
			{
				Math.Max(num4, (num2 < num4) ? num5 : num2),
				Math.Max(num4, (num3 < num4) ? num5 : num3)
			};
			array[axis] = ((best < 2) ? "turn" : "shift");
			array3[axis] = baseline[best];
		}
		if (num >= 4.0 && array4.All((bool v) => v))
		{
			return new CalibrationFeedback("Your movement is learned", "Return to your relaxed position. Your pointer will stay still until you are ready.", 1.0, 0.0, 0.0, (rest with
			{
				Modes = array,
				Ranges = array2,
				Noise = array3
			}).WithBalancedDirections());
		}
		double[] array5 = Channels(o, rest);
		string help;
		if (array4[0])
		{
			help = ((!array4[1]) ? "Try a comfortable movement up or down." : "A little more exploration will help Qadr learn your range.");
		}
		else
		{
			help = "Try a comfortable movement to either side.";
		}
		return new CalibrationFeedback("Explore your comfortable movement", help, 0.25 + 0.3 * (double)array4.Count((bool v) => v), Math.Clamp(array5[(!(array[0] == "turn")) ? 2 : 0] / array2[0].Max(), -1.0, 1.0), Math.Clamp(array5[(array[1] == "turn") ? 1 : 3] / array2[1].Max(), -1.0, 1.0));
		double Strength(int c)
		{
			return Math.Max(Math.Abs(Percentile(channels.Select((double[] p) => p[c]), 0.08)), Math.Abs(Percentile(channels.Select((double[] p) => p[c]), 0.92))) / baseline[c];
		}
	}

	private static double[] Channels(Observation o, Profile? p)
	{
		if ((object)p == null)
		{
			return new double[4]
			{
				o.Yaw,
				0.0 - o.Pitch,
				o.X,
				o.Y
			};
		}
		return new double[4]
		{
			o.Yaw - p.Neutral[0],
			p.Neutral[1] - o.Pitch,
			(o.X - p.Center[0]) / o.Scale,
			(o.Y - p.Center[1]) / o.Scale
		};
	}

	public static double Percentile(IEnumerable<double> values, double fraction)
	{
		double[] array = values.Order().ToArray();
		if (array.Length != 0)
		{
			return array[Math.Clamp((int)((double)(array.Length - 1) * fraction), 0, array.Length - 1)];
		}
		return 0.0;
	}

	public static double Median(IEnumerable<double> v)
	{
		return Percentile(v, 0.5);
	}

	private static double Spread(IEnumerable<double> v)
	{
		return Percentile(v, 0.9) - Percentile(v, 0.1);
	}

	private static double Noise(IEnumerable<double> values)
	{
		double[] array = values.ToArray();
		double m = Median(array);
		return Median(array.Select((double x) => Math.Abs(x - m))) * 1.4826;
	}
}
