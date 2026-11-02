using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public static class QuickTargetLayout
{
	public static QuickTargetPlan? Create(double width, double height)
	{
		if (!double.IsFinite(width) || !double.IsFinite(height) || width < 176.0 || height < 176.0)
		{
			return null;
		}
		double num = Math.Min(width, height);
		double size = ((num >= 296.0) ? 88 : ((num >= 224.0) ? 64 : 48));
		double num2 = 8.0 + size / 2.0;
		double num3 = 8.0 + size / 2.0;
		double item = width - num2;
		double item2 = height - num3;
		(double, double)[] source = new (double, double)[5]
		{
			(width / 2.0, height / 2.0),
			(num2, height / 2.0),
			(item, height / 2.0),
			(width / 2.0, num3),
			(width / 2.0, item2)
		};
		return new QuickTargetPlan(size, source.Select(((double X, double Y) p, int i) => new AssessmentTarget(((char)(65 + i)).ToString(), "move", p.X - size / 2.0, p.Y - size / 2.0, size, size)).ToArray());
	}

	public static bool Comparable(IReadOnlyList<AssessmentTarget> targets)
	{
		if (targets.Count != 5 || targets.Any((AssessmentTarget t) => !t.Valid))
		{
			return false;
		}
		for (int num = 0; num < 5; num++)
		{
			for (int num2 = num + 1; num2 < 5; num2++)
			{
				AssessmentTarget assessmentTarget = targets[num];
				AssessmentTarget assessmentTarget2 = targets[num2];
				double num3 = assessmentTarget.X + assessmentTarget.Width / 2.0 - assessmentTarget2.X - assessmentTarget2.Width / 2.0;
				double num4 = assessmentTarget.Y + assessmentTarget.Height / 2.0 - assessmentTarget2.Y - assessmentTarget2.Height / 2.0;
				double num5 = (Math.Max(assessmentTarget.Width, assessmentTarget.Height) + Math.Max(assessmentTarget2.Width, assessmentTarget2.Height)) / 2.0;
				if (num3 * num3 + num4 * num4 < (num5 + 1.0) * (num5 + 1.0))
				{
					return false;
				}
			}
		}
		AssessmentTarget assessmentTarget3 = targets[0];
		double num6 = assessmentTarget3.X + assessmentTarget3.Width / 2.0;
		double num7 = assessmentTarget3.Y + assessmentTarget3.Height / 2.0;
		if (targets[1].X + targets[1].Width / 2.0 < num6 && targets[2].X + targets[2].Width / 2.0 > num6 && targets[3].Y + targets[3].Height / 2.0 < num7)
		{
			return targets[4].Y + targets[4].Height / 2.0 > num7;
		}
		return false;
	}
}
