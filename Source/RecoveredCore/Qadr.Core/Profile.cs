using System;
using System.Linq;

namespace Qadr.Core;

public sealed record Profile
{
	public int CoordinateVersion { get; init; } = 1;

	public double[] Neutral { get; init; } = new double[2];

	public double[] Center { get; init; } = new double[2] { 0.5, 0.5 };

	public double Scale { get; init; } = 0.2;

	public string[] Modes { get; init; } = new string[2] { "turn", "turn" };

	public double[][] Ranges { get; init; } = new double[2][]
	{
		new double[2] { 0.16, 0.16 },
		new double[2] { 0.16, 0.16 }
	};

	public double[] Noise { get; init; } = new double[2] { 0.003, 0.003 };

	public bool IsValid
	{
		get
		{
			if (CoordinateVersion == 3)
			{
				return StructurallyValid;
			}
			return false;
		}
	}

	private bool StructurallyValid
	{
		get
		{
			double[] neutral = Neutral;
			if (neutral != null && neutral.Length == 2)
			{
				neutral = Center;
				if (neutral != null && neutral.Length == 2)
				{
					string[] modes = Modes;
					if (modes != null && modes.Length == 2)
					{
						double[][] ranges = Ranges;
						if (ranges != null && ranges.Length == 2)
						{
							neutral = Noise;
							if (neutral != null && neutral.Length == 2 && Ranges.All((double[] r) => r != null && r.Length == 2) && double.IsFinite(Scale))
							{
								double scale = Scale;
								if (scale >= 0.04 && scale <= 1.0 && Neutral.Concat(Center).Concat(Noise).Concat(Ranges.SelectMany((double[] r) => r))
									.All(double.IsFinite) && Noise.All((double n) => n >= 0.0 && n <= 1.0) && Ranges.All((double[] r) => r.Length == 2 && r.All((double v) => v > 0.0 && v <= 5.0)))
								{
									return Modes.All((string m) => (m == "turn" || m == "shift") ? true : false);
								}
							}
						}
					}
				}
			}
			return false;
		}
	}

	public const double MinimumDirectionRatio = 0.5;

	public double Signal(Observation o, int axis)
	{
		if (!(Modes[axis] == "turn"))
		{
			return (((axis == 0) ? o.X : o.Y) - Center[axis]) / o.Scale;
		}
		if (axis != 0)
		{
			return Neutral[1] - o.Pitch;
		}
		return o.Yaw - Neutral[0];
	}

	public double[] Signals(Observation o)
	{
		return new double[2]
		{
			Signal(o, 0),
			Signal(o, 1)
		};
	}

	public Profile WithBalancedDirections()
	{
		return this with
		{
			Ranges = Ranges.Select((double[] r) => new double[2]
			{
				Math.Max(r[0], r[1] * 0.5),
				Math.Max(r[1], r[0] * 0.5)
			}).ToArray()
		};
	}

	public Profile Upgrade()
	{
		if (CoordinateVersion != 2 || !StructurallyValid)
		{
			return this;
		}
		return this with
		{
			CoordinateVersion = 3,
			Ranges = new double[2][]
			{
				Ranges[0].ToArray(),
				(!(Modes[1] == "turn")) ? Ranges[1].ToArray() : new double[2]
				{
					Ranges[1][1],
					Ranges[1][0]
				}
			}
		};
	}
}
