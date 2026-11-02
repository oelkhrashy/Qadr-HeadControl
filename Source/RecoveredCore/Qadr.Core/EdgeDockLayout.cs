using System;

namespace Qadr.Core;

public static class EdgeDockLayout
{
	public static DockBounds Calculate(int x, int y, int width, int height, double scale, bool expanded)
	{
		scale = (double.IsFinite(scale) ? Math.Clamp(scale, 0.75, 3.0) : 1.0);
		int num = (int)Math.Ceiling(8.0 * scale);
		int num2 = Math.Min(Math.Max(48, width - 2 * num), (int)Math.Ceiling((double)(expanded ? 240 : 136) * scale));
		int num3 = (expanded ? Math.Max(96, height - 2 * num) : Math.Min(Math.Max(96, height - 2 * num), (int)Math.Ceiling(112.0 * scale)));
		return new DockBounds(x + width - num - num2, y + (height - num3) / 2, num2, num3);
	}
}
