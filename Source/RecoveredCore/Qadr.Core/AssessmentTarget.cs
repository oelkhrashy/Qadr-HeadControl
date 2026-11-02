using System;
using System.Linq;

namespace Qadr.Core;

public sealed record AssessmentTarget(string Id, string Action, double X, double Y, double Width, double Height)
{
	public bool Valid
	{
		get
		{
			int length = Id.Length;
			bool flag = length > 0 && length <= 8;
			if (flag)
			{
				bool flag2;
				switch (Action)
				{
				case "move":
				case "left":
				case "double":
				case "right":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag && new double[4] { X, Y, Width, Height }.All(double.IsFinite))
			{
				double width = Width;
				if (width >= 20.0 && width <= 1000.0)
				{
					width = Height;
					if (width >= 20.0)
					{
						return width <= 1000.0;
					}
					return false;
				}
			}
			return false;
		}
	}

	public bool Contains(double x, double y)
	{
		return Math.Pow((x - X - Width / 2.0) / (Width / 2.0), 2.0) + Math.Pow((y - Y - Height / 2.0) / (Height / 2.0), 2.0) <= 1.0;
	}
}
