using System;

namespace Qadr.Core;

public sealed class EyebrowClick
{
	private double? start;

	private double? last;

	private double firedAt = double.NegativeInfinity;

	private bool qualified;

	private bool armed;

	private double? neutralSince;

	public bool Active { get; private set; }

	public double Progress { get; private set; }

	private void Clear()
	{
		start = (neutralSince = null);
		bool flag = (Active = false);
		armed = (qualified = flag);
		Progress = 0.0;
	}

	public bool Feed(double score, Settings settings, double time, bool conflict = false)
	{
		if (((!double.IsFinite(score) || !double.IsFinite(time)) | conflict) || !settings.EyebrowDoubleClick || !settings.EyebrowLearned)
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
		double num = settings.EyebrowRestLevel + (settings.EyebrowThreshold - settings.EyebrowRestLevel) * 0.7;
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
		if (start.HasValue && time - start.Value > 1.5)
		{
			Clear();
			return false;
		}
		if (score >= settings.EyebrowThreshold)
		{
			double valueOrDefault = start.GetValueOrDefault();
			if (!start.HasValue)
			{
				valueOrDefault = time;
				start = valueOrDefault;
			}
			Active = true;
			Progress = Math.Clamp((time - start.Value) / settings.EyebrowHold, 0.0, 1.0);
			qualified |= Progress >= 1.0;
			return false;
		}
		if (score >= settings.EyebrowRestLevel + (settings.EyebrowThreshold - settings.EyebrowRestLevel) * 0.7 && start.HasValue)
		{
			return false;
		}
		if (!start.HasValue)
		{
			return false;
		}
		bool num2 = qualified && time - firedAt >= settings.ClickCooldown;
		Clear();
		if (num2)
		{
			firedAt = time;
		}
		return num2;
	}
}
