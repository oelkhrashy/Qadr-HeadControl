namespace Qadr.Core;

public sealed record MovementParameters(double Sensitivity, double TravelBoost, double PrecisionNoise)
{
	public bool Valid
	{
		get
		{
			if (double.IsFinite(Sensitivity))
			{
				double sensitivity = Sensitivity;
				if (sensitivity >= 0.3 && sensitivity <= 2.5 && double.IsFinite(TravelBoost))
				{
					sensitivity = TravelBoost;
					if (sensitivity >= 1.0 && sensitivity <= 4.0 && double.IsFinite(PrecisionNoise))
					{
						sensitivity = PrecisionNoise;
						if (sensitivity >= 0.1)
						{
							return sensitivity <= 2.0;
						}
						return false;
					}
				}
			}
			return false;
		}
	}

	public static MovementParameters From(Settings s)
	{
		return new MovementParameters(s.Sensitivity, s.RelativeTravelBoost, s.PrecisionNoiseMultiplier);
	}

	public Settings Apply(Settings s)
	{
		if (!Valid)
		{
			return s;
		}
		return s with
		{
			Sensitivity = Sensitivity,
			RelativeTravelBoost = TravelBoost,
			PrecisionNoiseMultiplier = PrecisionNoise
		};
	}
}
