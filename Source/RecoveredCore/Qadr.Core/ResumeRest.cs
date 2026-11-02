using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public sealed class ResumeRest
{
	private readonly List<Observation> samples = new List<Observation>();

	private Observation? anchor;

	private Observation? previous;

	public IReadOnlyList<Observation> Samples => samples;

	public double Progress { get; private set; }

	public void Reset()
	{
		samples.Clear();
		anchor = (previous = null);
		Progress = 0.0;
	}

	public bool Feed(Observation o, Profile profile)
	{
		if (!o.IsUsable)
		{
			Reset();
			return false;
		}
		// Recovery is a resting measurement, rather than a permissive travel gate.
		double num = Math.Clamp(profile.Noise[0] * 3.0, 0.012, 0.035);
		double num2 = Math.Clamp(profile.Noise[1] * 3.0, 0.012, 0.035);
		bool num3 = (object)previous == null || o.Timestamp <= previous.Timestamp || o.Timestamp - previous.Timestamp > 0.2;
		bool flag = (object)anchor != null && (Math.Abs(o.Yaw - anchor.Yaw) > num || Math.Abs(o.Pitch - anchor.Pitch) > num2 || Math.Abs(o.X - anchor.X) / o.Scale > 0.06 || Math.Abs(o.Y - anchor.Y) / o.Scale > 0.06 || Math.Abs(o.Scale / anchor.Scale - 1.0) > 0.08);
		if (num3 | flag)
		{
			Reset();
			anchor = o;
		}
		previous = o;
		samples.Add(o);
		Progress = Math.Clamp((o.Timestamp - samples[0].Timestamp) / 0.6, 0.0, 1.0);
		if (Progress >= 1.0)
		{
			return samples.Count >= 10;
		}
		return false;
	}

	public Observation Centre()
	{
		if (samples.Count == 0)
		{
			throw new InvalidOperationException("No resting observations");
		}
		List<Observation> list = samples;
		return list[list.Count - 1] with
		{
			Yaw = Calibration.Median(samples.Select((Observation o) => o.Yaw)),
			Pitch = Calibration.Median(samples.Select((Observation o) => o.Pitch)),
			X = Calibration.Median(samples.Select((Observation o) => o.X)),
			Y = Calibration.Median(samples.Select((Observation o) => o.Y)),
			Scale = Calibration.Median(samples.Select((Observation o) => o.Scale)),
			FaceX = Calibration.Median(samples.Select((Observation o) => o.FaceX)),
			FaceY = Calibration.Median(samples.Select((Observation o) => o.FaceY)),
			FaceWidth = Calibration.Median(samples.Select((Observation o) => o.FaceWidth)),
			FaceHeight = Calibration.Median(samples.Select((Observation o) => o.FaceHeight))
		};
	}
}
