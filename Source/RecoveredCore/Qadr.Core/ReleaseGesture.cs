namespace Qadr.Core;

public sealed class ReleaseGesture
{
	private double? since;

	private double? last;

	private double firedAt = double.NegativeInfinity;

	private bool qualified;

	private bool armed;

	private double? neutralSince;

	public bool Active { get; private set; }

	private void Clear()
	{
		since = (neutralSince = null);
		bool flag = (Active = false);
		armed = (qualified = flag);
	}

	public bool Feed(double score, double threshold, double rest, double hold, double time, bool enabled, bool conflict = false)
	{
		if ((!enabled | conflict) || !double.IsFinite(score) || !double.IsFinite(time))
		{
			Clear();
			last = null;
			return false;
		}
		if (last.HasValue && time <= last.Value)
		{
			return false;
		}
		if (last.HasValue && time - last.Value > 0.2)
		{
			Clear();
		}
		last = time;
		double num = rest + (threshold - rest) * 0.7;
		if (score < num)
		{
			double valueOrDefault = neutralSince.GetValueOrDefault();
			if (!neutralSince.HasValue)
			{
				valueOrDefault = time;
				neutralSince = valueOrDefault;
			}
			if (time - neutralSince >= 0.06)
			{
				armed = true;
			}
		}
		else
		{
			neutralSince = null;
		}
		if (!armed)
		{
			return false;
		}
		if (since.HasValue && time - since.Value > 1.5)
		{
			Clear();
			return false;
		}
		if (score >= threshold)
		{
			double valueOrDefault = since.GetValueOrDefault();
			if (!since.HasValue)
			{
				valueOrDefault = time;
				since = valueOrDefault;
			}
			Active = true;
			qualified |= time - since >= hold;
			return false;
		}
		if (since.HasValue && score >= rest + (threshold - rest) * 0.7)
		{
			return false;
		}
		if (!since.HasValue)
		{
			return false;
		}
		bool num2 = qualified && time - firedAt >= 0.8;
		Clear();
		if (num2)
		{
			firedAt = time;
		}
		return num2;
	}
}
