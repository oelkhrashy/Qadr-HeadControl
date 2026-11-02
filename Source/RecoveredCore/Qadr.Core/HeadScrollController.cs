using System;

namespace Qadr.Core;

public sealed class HeadScrollController
{
	private double neutral;

	private double lastTime;

	private double lastWheel;

	public bool Active { get; private set; }

	public double AnchorX { get; private set; }

	public double AnchorY { get; private set; }

	public void Start(Observation o, Profile profile, double x, double y)
	{
		Active = true;
		neutral = profile.Signal(o, 1);
		lastTime = o.Timestamp;
		lastWheel = double.NegativeInfinity;
		AnchorX = x;
		AnchorY = y;
	}

	public void Reset()
	{
		Active = false;
	}

	public int Feed(Observation o, Profile profile, Settings settings, bool gestureActive = false)
	{
		if (!Active)
		{
			return 0;
		}
		if (!o.IsUsable)
		{
			Reset();
			return 0;
		}
		if (o.Timestamp <= lastTime)
		{
			return 0;
		}
		if (o.Timestamp - lastTime > 0.2)
		{
			Reset();
			return 0;
		}
		lastTime = o.Timestamp;
		if (gestureActive)
		{
			return 0;
		}
		double num = Math.Min(profile.Ranges[1][0], profile.Ranges[1][1]);
		double num2 = profile.Signal(o, 1) - neutral;
		double num3 = Math.Max(num * 0.18, profile.Noise[1] * 4.0);
		if (Math.Abs(num2) < num3)
		{
			lastWheel = double.NegativeInfinity;
			return 0;
		}
		if (o.Timestamp - lastWheel < settings.ScrollRepeatSeconds)
		{
			return 0;
		}
		lastWheel = o.Timestamp;
		if (!(num2 < 0.0))
		{
			return -settings.ScrollNotches;
		}
		return settings.ScrollNotches;
	}
}
