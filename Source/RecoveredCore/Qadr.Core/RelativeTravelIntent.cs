using System;

namespace Qadr.Core;

public sealed class RelativeTravelIntent
{
	private double previousX;

	private double previousY;

	private double seconds;

	private bool seeded;

	public void Reset()
	{
		previousX = (previousY = (seconds = 0.0));
		seeded = false;
	}

	public double Feed(double dx, double dy, double dt, double width, double height, double sensitivity)
	{
		if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dt) || !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(sensitivity) || dt <= 0.0 || dt > 0.2 || width <= 0.0 || height <= 0.0 || sensitivity <= 0.0)
		{
			Reset();
			return 0.0;
		}
		double num = dx / (width * 0.7 * sensitivity * dt);
		double num2 = dy / (height * 0.7 * sensitivity * dt);
		double num3 = Math.Sqrt(num * num + num2 * num2);
		if (num3 <= 0.15)
		{
			Reset();
			return 0.0;
		}
		num /= num3;
		num2 /= num3;
		if (!seeded || num * previousX + num2 * previousY < 0.5)
		{
			seconds = 0.0;
		}
		else
		{
			seconds = Math.Min(0.24, seconds + dt);
		}
		previousX = num;
		previousY = num2;
		seeded = true;
		double num4 = Math.Clamp((seconds - 0.06) / 0.18, 0.0, 1.0);
		return num4 * num4 * (3.0 - 2.0 * num4);
	}
}
