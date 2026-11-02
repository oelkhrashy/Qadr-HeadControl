using System;

namespace Qadr.Core;

public sealed class QualityGate
{
	private Observation? previous;

	private double stableSince;

	public bool Accept(Observation? o, double now)
	{
		if ((object)o == null || !o.IsUsable || !double.IsFinite(now) || now - o.Timestamp > 0.2 || now < o.Timestamp)
		{
			previous = null;
			stableSince = now;
			return false;
		}
		if ((object)previous != null)
		{
			double num = o.Timestamp - previous.Timestamp;
			if (num <= 0.0)
			{
				previous = null;
				stableSince = o.Timestamp;
				return false;
			}
			if (num > 0.2 || (num < 0.15 && (Math.Abs(o.Yaw - previous.Yaw) > 0.45 || Math.Abs(o.Pitch - previous.Pitch) > 0.4 || Math.Abs(o.Scale - previous.Scale) / previous.Scale > 0.35 || Math.Abs(o.X - previous.X) > 0.15 || Math.Abs(o.Y - previous.Y) > 0.15)))
			{
				previous = o;
				stableSince = o.Timestamp;
				return false;
			}
		}
		else
		{
			stableSince = o.Timestamp;
		}
		previous = o;
		return o.Timestamp - stableSince >= 0.6;
	}
}
