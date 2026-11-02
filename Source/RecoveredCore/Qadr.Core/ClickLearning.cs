using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public sealed class ClickLearning(bool eyes, Settings settings)
{
	private readonly List<double> rest = new List<double>();

	private readonly List<double> action = new List<double>();

	private double restNoise;

	private double? started;

	private double? actionStarted;

	private double baseline;

	private double threshold;

	private bool releasing;

	public void Lost()
	{
		started = (actionStarted = null);
		rest.Clear();
		action.Clear();
		releasing = false;
	}

	public ClickLessonFeedback Feed(double value, double timestamp)
	{
		double valueOrDefault = started.GetValueOrDefault();
		if (!started.HasValue)
		{
			valueOrDefault = timestamp;
			started = valueOrDefault;
		}
		if (timestamp - started < settings.ClickLearningRestSeconds)
		{
			rest.Add(value);
			return new ClickLessonFeedback("rest", Math.Clamp((timestamp - started.Value) / settings.ClickLearningRestSeconds, 0.0, 1.0));
		}
		baseline = Calibration.Median(rest);
		restNoise = 1.4826 * Calibration.Median(rest.Select((double v) => Math.Abs(v - baseline)));
		double num;
		if (eyes)
		{
			num = 0.2;
		}
		else
		{
			num = (settings.SubtleSmile ? 0.02 : 0.1);
		}
		double num2 = ((!eyes && settings.SubtleSmile) ? Math.Max(0.025, restNoise * 4.0) : settings.ClickLearningSeparation);
		if (releasing)
		{
			if (value < baseline + (threshold - baseline) * settings.EyeReleaseRatio)
			{
				return new ClickLessonFeedback("done", 1.0, threshold, baseline);
			}
			return new ClickLessonFeedback("release", 1.0);
		}
		if (value >= Math.Max(num, baseline + num2))
		{
			valueOrDefault = actionStarted.GetValueOrDefault();
			if (!actionStarted.HasValue)
			{
				valueOrDefault = timestamp;
				actionStarted = valueOrDefault;
			}
			action.Add(value);
			double num3 = (timestamp - actionStarted.Value) / settings.ClickLearningHoldSeconds;
			if (num3 >= 1.0)
			{
				threshold = Math.Clamp(baseline + Math.Max(num2 * 0.8, (Calibration.Median(action) - baseline) * 0.5), num, 0.9);
				releasing = true;
				return new ClickLessonFeedback("release", 1.0);
			}
			return new ClickLessonFeedback("gesture", Math.Clamp(num3, 0.0, 1.0));
		}
		actionStarted = null;
		action.Clear();
		return new ClickLessonFeedback("gesture", 0.0);
	}
}
