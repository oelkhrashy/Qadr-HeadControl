using System;

namespace Qadr.Core;

public sealed class SteadyHold
{
	public const double MemorySeconds = 0.45;

	private double netX;

	private double netY;

	private double path;

	private double heldX;

	private double heldY;

	public double Gain { get; private set; } = 1.0;

	public void Reset()
	{
		netX = (netY = (path = (heldX = (heldY = 0.0))));
		Gain = 1.0;
	}

	public (double X, double Y) Apply(double dx, double dy, double dt, double strength)
	{
		if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dt) || dt <= 0.0 || dt > 0.2 || !double.IsFinite(strength))
		{
			Reset();
			return (X: 0.0, Y: 0.0);
		}
		strength = Math.Clamp(strength, 0.0, 1.0);
		if (strength == 0.0)
		{
			Reset();
			return (X: dx, Y: dy);
		}
		double num = Math.Exp((0.0 - dt) / 0.45);
		netX = netX * num + dx;
		netY = netY * num + dy;
		path = path * num + Math.Sqrt(dx * dx + dy * dy);
		if (path <= 1E-09)
		{
			Gain = 1.0;
			return (X: 0.0, Y: 0.0);
		}
		double num2 = Math.Clamp((Math.Min(1.0, Math.Sqrt(netX * netX + netY * netY) / path) - 0.35) / 0.45, 0.0, 1.0);
		num2 = num2 * num2 * (3.0 - 2.0 * num2);
		double num3 = 1.0 - 0.8 * strength;
		Gain = num3 + (1.0 - num3) * num2;
		double num4 = Math.Exp((0.0 - dt) / 5.0);
		heldX = heldX * num4 + dx * (1.0 - Gain);
		heldY = heldY * num4 + dy * (1.0 - Gain);
		double num5 = dx * Gain;
		double num6 = dy * Gain;
		if (num2 > 0.6 && heldX * dx + heldY * dy > 0.0)
		{
			double num7 = 1.0 - Math.Exp((0.0 - dt) / 0.08);
			num5 += heldX * num7;
			num6 += heldY * num7;
			heldX -= heldX * num7;
			heldY -= heldY * num7;
		}
		return (X: num5, Y: num6);
	}
}
