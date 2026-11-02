using System;

namespace Qadr.Core;

public sealed class RelativeRest
{
	private bool seeded;

	private bool held;

	private double anchor;

	private double minimum;

	private double maximum;

	private double since;

	private double last;

	private double path;

	private double quietTime;

	public bool CanFinish { get; private set; }

	public void Reset()
	{
		bool flag = (CanFinish = false);
		seeded = (held = flag);
	}

	public bool Feed(double accepted, double filtered, double noise, double time, double dt)
	{
		if (!double.IsFinite(accepted) || !double.IsFinite(filtered) || !double.IsFinite(noise) || !double.IsFinite(time) || dt <= 0.0 || dt > 0.2)
		{
			Reset();
			return false;
		}
		double num = Math.Max(5E-05, noise * 0.2);
		double num2 = Math.Max(0.0001, noise * 0.12);
		if (!seeded)
		{
			Seed(accepted, time);
			return false;
		}
		if (held)
		{
			if (Math.Abs(accepted - anchor) <= num2)
			{
				return true;
			}
			Seed(accepted, time);
			return false;
		}
		double num3 = Math.Abs(accepted - last);
		quietTime = ((num3 <= Math.Max(1E-09, noise * 0.002)) ? (quietTime + dt) : 0.0);
		path += num3;
		last = accepted;
		minimum = Math.Min(minimum, accepted);
		maximum = Math.Max(maximum, accepted);
		if (maximum - minimum > num)
		{
			Seed(accepted, time);
			return false;
		}
		bool flag = path > Math.Max(1E-05, noise * 0.025) && Math.Abs(accepted - anchor) > path * 0.8;
		bool flag2 = (quietTime < 0.075) & flag;
		if (time - since >= 0.075 && !flag2)
		{
			double num4 = Math.Clamp(noise * 0.6, 0.0002, 0.02);
			if ((quietTime >= 0.075) & flag)
			{
				num4 = Math.Min(num4, 0.005);
			}
			CanFinish = Math.Abs(accepted - filtered) <= num4;
			held = true;
			anchor = (minimum + maximum) / 2.0;
			return true;
		}
		return false;
	}

	private void Seed(double input, double time)
	{
		seeded = true;
		bool flag = (CanFinish = false);
		held = flag;
		anchor = (last = (minimum = (maximum = input)));
		path = (quietTime = 0.0);
		since = time;
	}
}
