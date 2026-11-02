using System;

namespace Qadr.Core;

public sealed record RestNoiseModel(int Version = 1, long Samples = 0L, double VarianceX = 0.0, double VarianceY = 0.0)
{
	public bool Valid
	{
		get
		{
			if (Version == 1 && Samples >= 0 && Samples <= 10000000 && double.IsFinite(VarianceX) && double.IsFinite(VarianceY))
			{
				double varianceX = VarianceX;
				if (varianceX >= 0.0 && varianceX <= 1.0)
				{
					varianceX = VarianceY;
					if (varianceX >= 0.0)
					{
						return varianceX <= 1.0;
					}
					return false;
				}
			}
			return false;
		}
	}

	public RestNoiseModel Observe(double dx, double dy)
	{
		return new RestNoiseModel(1, Math.Min(10000000L, Samples + 1), VarianceX + (dx * dx - VarianceX) / (double)Math.Min(300L, Samples + 1), VarianceY + (dy * dy - VarianceY) / (double)Math.Min(300L, Samples + 1));
	}

	public double Noise(int axis, double baseline)
	{
		if (Samples < 60)
		{
			return baseline;
		}
		double num = Math.Sqrt(((axis == 0) ? VarianceX : VarianceY) / 2.0);
		return Math.Clamp(0.5 * baseline + 0.5 * num, Math.Max(0.0001, baseline * 0.65), Math.Max(0.0002, baseline * 1.5));
	}
}
