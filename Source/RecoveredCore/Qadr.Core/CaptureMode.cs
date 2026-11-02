using System;

namespace Qadr.Core;

public static class CaptureMode
{
	public static double Score(uint width, uint height, double fps, int targetFps = 30)
	{
		if (width < 160 || height < 120 || width > 1920 || height > 1080 || !double.IsFinite(fps) || fps < 0.0)
		{
			return double.NegativeInfinity;
		}
		double num = ((fps > 0.0) ? fps : 15.0);
		double num2 = Math.Min(1.0, Math.Min(640.0 / (double)width, 480.0 / (double)height));
		double num3 = (double)width * num2 * (double)height * num2 / 307200.0;
		int num4 = ((targetFps == 60) ? 60 : 30);
		if (num4 == 60 && num3 < 0.5)
		{
			num = Math.Min(num, 24.0);
		}
		double num5 = Math.Max(0.0, (double)width * (double)height / 307200.0 - 1.0) * 500.0;
		return ((num >= (double)num4 * 0.8) ? 100000.0 : (Math.Min(num, (double)num4 * 0.8) * 1000.0)) + num3 * 10000.0 + Math.Min(num, num4) * 100.0 - Math.Max(0.0, num - (double)num4) * 5.0 - num5;
	}
}
