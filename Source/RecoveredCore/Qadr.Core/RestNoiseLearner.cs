using System;

namespace Qadr.Core;

public sealed class RestNoiseLearner
{
	private Observation? previous;

	public RestNoiseModel Model { get; private set; } = new RestNoiseModel(1, 0L);

	public void Load(RestNoiseModel? model)
	{
		Model = (((object)model != null && model.Valid) ? model : new RestNoiseModel(1, 0L));
		previous = null;
	}

	public void Break()
	{
		previous = null;
	}

	public bool Feed(Observation o, Profile profile, bool labeledRest)
	{
		if (!labeledRest || !o.IsUsable)
		{
			Break();
			return false;
		}
		Observation observation = previous;
		previous = o;
		if ((object)observation == null)
		{
			return false;
		}
		double num = o.Timestamp - observation.Timestamp;
		if (num <= 0.0 || num > 0.2)
		{
			Break();
			return false;
		}
		double num2 = profile.Signal(o, 0) - profile.Signal(observation, 0);
		double num3 = profile.Signal(o, 1) - profile.Signal(observation, 1);
		if (Math.Abs(num2) > Math.Max(0.008, profile.Noise[0] * 4.0) || Math.Abs(num3) > Math.Max(0.008, profile.Noise[1] * 4.0))
		{
			Break();
			return false;
		}
		Model = Model.Observe(num2, num3);
		return true;
	}
}
