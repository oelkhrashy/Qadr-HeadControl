using System;

namespace Qadr.Core;

public sealed record TunedMovementProfile(int Schema, TuningContext Context, MovementParameters Anchor, MovementParameters Previous, MovementParameters Current, string Reason, DateTime SavedUtc, bool Synthetic = false)
{
	public bool Matches(TuningContext context, Settings configured, bool synthetic = false)
	{
		if (Synthetic == synthetic && Schema == 1 && Context == context)
		{
			MovementParameters anchor = Anchor;
			if ((object)anchor != null && anchor.Valid)
			{
				anchor = Previous;
				if ((object)anchor != null && anchor.Valid)
				{
					anchor = Current;
					if ((object)anchor != null && anchor.Valid && Anchor == MovementParameters.From(configured) && MovementTuner.WithinEnvelope(Current, Anchor))
					{
						return MovementTuner.WithinEnvelope(Previous, Anchor);
					}
				}
			}
		}
		return false;
	}
}
