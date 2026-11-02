using System;

namespace Qadr.Core;

public sealed class AdaptiveAxisFilter
{
	private bool initialized;

	private bool pending;

	private double value;

	private double accepted;

	private double velocity;

	private double variation;

	private double candidate;

	private double relativeTravel;

	private double relativeTravelTime;

	public double AcceptedInput => accepted;

	public double MotionConfidence { get; private set; }

	public bool RejectedSpike { get; private set; }

	public void Settle()
	{
		value = accepted;
		velocity = (variation = (relativeTravel = (relativeTravelTime = 0.0)));
		MotionConfidence = 0.0;
	}

	private static double Alpha(double cutoff, double dt)
	{
		return 1.0 / (1.0 + 1.0 / (Math.PI * 2.0 * cutoff * dt));
	}

	public double Feed(double input, double dt, double noise, double responsiveness, bool direct = false, bool relative = false)
	{
		if (!double.IsFinite(input) || !double.IsFinite(dt) || !double.IsFinite(noise) || !double.IsFinite(responsiveness))
		{
			initialized = (pending = false);
			MotionConfidence = 0.0;
			RejectedSpike = true;
			return 0.0;
		}
		noise = Math.Clamp(noise, 0.0001, 0.3);
		responsiveness = Math.Clamp(responsiveness, 0.05, 1.0);
		if (!initialized || dt <= 0.0 || dt > 0.2)
		{
			value = (accepted = input);
			velocity = (variation = (relativeTravel = (relativeTravelTime = 0.0)));
			initialized = true;
			pending = false;
			MotionConfidence = 0.0;
			RejectedSpike = false;
			return value;
		}
		double num = input - accepted;
		double num2 = Math.Max(0.12, Math.Max(noise * 6.0, Math.Abs(velocity) * dt * 3.0));
		bool flag = Math.Abs(num) > num2;
		bool flag2 = pending && Math.Sign(input - accepted) == Math.Sign(candidate - accepted) && Math.Abs(input - candidate) <= Math.Max(noise * 3.0, Math.Abs(candidate - accepted) * 0.5);
		RejectedSpike = flag && !flag2;
		double num3 = input;
		if (RejectedSpike)
		{
			candidate = input;
			pending = true;
			num3 = accepted;
		}
		else
		{
			pending = false;
		}
		double num4 = num3 - accepted;
		if (relative && Math.Abs(num4) > noise * 0.025)
		{
			if (relativeTravel != 0.0 && Math.Sign(num4) != Math.Sign(relativeTravel) && Math.Abs(num4) > noise * 0.2)
			{
				relativeTravel = (relativeTravelTime = 0.0);
			}
			relativeTravel += num4;
			relativeTravelTime += dt;
		}
		double num5 = num4 / dt;
		double num6 = Alpha(1.0, dt);
		velocity += num6 * (num5 - velocity);
		variation += num6 * (Math.Abs(num5) - variation);
		accepted = num3;
		double num7 = Math.PI * 2.0 * noise;
		double num8 = Math.Max(0.0, Math.Abs(velocity) - num7);
		double num9 = ((variation > 1E-06) ? (Math.Abs(velocity) / variation) : 0.0);
		double num10 = Math.Clamp((num9 - 0.35) / 0.5, 0.0, 1.0);
		double num11 = (((relative & direct) && num9 >= 0.85 && relativeTravelTime >= 0.22 && Math.Abs(relativeTravel) >= noise * 4.0) ? (noise * 0.5) : (noise * 4.0));
		double val = Math.Max(0.0, Math.Abs(num3 - value) - num11) / (direct ? 0.055 : 0.12);
		double num12 = Math.Max(num8 * num10, val);
		double num13 = Math.Max(num8 * num10, Math.Max(0.0, Math.Abs(num3 - value) - noise * 4.0) / (direct ? 0.055 : 0.12));
		MotionConfidence = Math.Clamp(num13 / Math.Max(0.08, noise * 6.0), 0.0, 1.0);
		double cutoff = Math.Clamp((0.45 + responsiveness * 1.8) / (1.0 + noise * 8.0) + (3.5 + responsiveness * 4.0) * num12, 0.35, 30.0);
		value += Alpha(cutoff, dt) * (num3 - value);
		return value;
	}
}
