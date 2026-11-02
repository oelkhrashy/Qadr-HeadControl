using System;

namespace Qadr.Core;

public sealed class OneEuro
{
	private double? value;

	private double? previous;

	private double derivative;

	public void Settle(double input)
	{
		value = (previous = input);
		derivative = 0.0;
	}

	private static double Alpha(double cutoff, double dt)
	{
		return 1.0 / (1.0 + 1.0 / (Math.PI * 2.0 * cutoff * dt));
	}

	public double Feed(double input, double dt, double responsiveness)
	{
		if (!value.HasValue)
		{
			value = (previous = input);
			return input;
		}
		dt = Math.Max(0.001, dt);
		derivative += Alpha(1.0, dt) * ((input - previous.Value) / dt - derivative);
		previous = input;
		value += Alpha(0.9 + responsiveness * 2.0 + 3.5 * Math.Abs(derivative), dt) * (input - value.Value);
		return value.Value;
	}
}
