using System;

namespace Qadr.Core;

public static class RelativeReach
{
	public static (double X, double Y) Apply(double dx, double dy, double dt, double width, double height, double sensitivity, double boost, double confidence = 1.0)
	{
		if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dt) || dt <= 0.0 || dt > 0.2 || !double.IsFinite(width) || width <= 0.0 || !double.IsFinite(height) || height <= 0.0 || !double.IsFinite(sensitivity) || sensitivity <= 0.0 || !double.IsFinite(boost) || !double.IsFinite(confidence))
		{
			return (X: 0.0, Y: 0.0);
		}
		boost = Math.Clamp(boost, 1.0, 4.0);
		double num = dx / (width * 0.7 * sensitivity * dt);
		double num2 = dy / (height * 0.7 * sensitivity * dt);
		double num3 = Math.Clamp((Math.Sqrt(num * num + num2 * num2) - 0.15) / 0.75, 0.0, 1.0);
		num3 = num3 * num3 * (3.0 - 2.0 * num3);
		double num4 = 1.0 + (boost - 1.0) * num3 * Math.Clamp(confidence, 0.0, 1.0);
		return (X: dx * num4, Y: dy * num4);
	}
}
