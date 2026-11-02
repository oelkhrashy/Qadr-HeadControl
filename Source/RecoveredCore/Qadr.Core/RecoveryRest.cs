using System;

namespace Qadr.Core;

public sealed class RecoveryRest
{
	private Observation? anchor;

	private double since;

	private double last;

	public void Reset()
	{
		anchor = null;
		since = (last = 0.0);
	}

	public bool Feed(Observation o)
	{
		if (!o.IsUsable)
		{
			Reset();
			return false;
		}
		if ((object)anchor == null || o.Timestamp <= last || o.Timestamp - last > 0.2 || Math.Abs(o.Yaw - anchor.Yaw) > 0.025 || Math.Abs(o.Pitch - anchor.Pitch) > 0.025 || Math.Abs(o.Scale / anchor.Scale - 1.0) > 0.08)
		{
			anchor = o;
			since = o.Timestamp;
		}
		last = o.Timestamp;
		return o.Timestamp - since >= 0.35;
	}
}
