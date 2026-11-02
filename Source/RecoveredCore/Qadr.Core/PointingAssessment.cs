using System;

namespace Qadr.Core;

public sealed class PointingAssessment
{
	private readonly AssessmentTarget target;

	private readonly double started;

	private readonly double startX;

	private readonly double startY;

	private readonly double doubleClickSeconds;

	private AssessmentResult? pendingSingle;

	private double singleAt;

	private double lastTime;

	private double lastX;

	private double lastY;

	private double path;

	private double firstEntry = double.NaN;

	private double insideSince = double.NaN;

	private double maximumOvershoot;

	private double meanX;

	private double meanY;

	private double m2X;

	private double m2Y;

	private long insideSamples;

	private long samples;

	private bool inside;

	private bool usableBefore = true;

	private bool completed;

	private int entries;

	private int overshoots;

	private int attempts;

	private int wrong;

	private int losses;

	public AssessmentProgress Progress => new AssessmentProgress(target, double.IsFinite(insideSince) ? Math.Clamp((lastTime - insideSince) / 0.6, 0.0, 1.0) : 0.0, Math.Max(0.0, lastTime - started), (object)pendingSingle != null);

	public PointingAssessment(AssessmentTarget target, double started, double startX, double startY, double doubleClickSeconds = 0.5)
	{
		this.target = target;
		this.started = started;
		this.startX = startX;
		this.startY = startY;
		this.doubleClickSeconds = (double.IsFinite(doubleClickSeconds) ? Math.Clamp(doubleClickSeconds, 0.1, 5.0) : 0.5);
		lastTime = started;
		lastX = startX;
		lastY = startY;
	}

	public AssessmentResult? Feed(double now, double x, double y, bool usable)
	{
		if (completed || !double.IsFinite(now) || now < lastTime || !double.IsFinite(x) || !double.IsFinite(y))
		{
			return null;
		}
		bool flag = now - lastTime > 0.2;
		lastTime = now;
		AssessmentResult assessmentResult = pendingSingle;
		if ((object)assessmentResult != null)
		{
			if (!(!usable | flag))
			{
				if (now - singleAt >= doubleClickSeconds)
				{
					completed = true;
					pendingSingle = null;
					return assessmentResult;
				}
				return null;
			}
			pendingSingle = null;
		}
		if (!usable)
		{
			if (usableBefore)
			{
				losses++;
			}
			usableBefore = false;
			inside = false;
			insideSince = double.NaN;
			insideSamples = 0L;
			return null;
		}
		if (flag && usableBefore)
		{
			losses++;
		}
		if (usableBefore && !flag)
		{
			path += Math.Sqrt(Math.Pow(x - lastX, 2.0) + Math.Pow(y - lastY, 2.0));
		}
		lastX = x;
		lastY = y;
		usableBefore = true;
		samples++;
		bool flag2 = target.Contains(x, y);
		if (flag2)
		{
			if (!inside | flag)
			{
				entries++;
				insideSince = now;
				insideSamples = 0L;
				meanX = (meanY = (m2X = (m2Y = 0.0)));
				firstEntry = (double.IsFinite(firstEntry) ? firstEntry : now);
			}
			insideSamples++;
			double num = x - meanX;
			double num2 = y - meanY;
			meanX += num / (double)insideSamples;
			meanY += num2 / (double)insideSamples;
			m2X += num * (x - meanX);
			m2Y += num2 * (y - meanY);
		}
		else
		{
			if (inside)
			{
				overshoots++;
			}
			if (double.IsFinite(firstEntry))
			{
				double num3 = Math.Sqrt(Math.Pow(x - target.X - target.Width / 2.0, 2.0) + Math.Pow(y - target.Y - target.Height / 2.0, 2.0));
				maximumOvershoot = Math.Max(maximumOvershoot, Math.Max(0.0, num3 - Math.Min(target.Width, target.Height) / 2.0));
			}
			insideSince = double.NaN;
			insideSamples = 0L;
		}
		inside = flag2;
		if (!(target.Action == "move") || insideSamples < 10 || !(now - insideSince >= 0.6))
		{
			return null;
		}
		return Finish(now, success: true, "held inside target");
	}

	public AssessmentResult? Click(string action, double now, double x, double y)
	{
		if (completed || !double.IsFinite(now) || now < lastTime || !double.IsFinite(x) || !double.IsFinite(y))
		{
			return null;
		}
		pendingSingle = null;
		AssessmentResult assessmentResult = Feed(now, x, y, usableBefore);
		if ((object)assessmentResult != null)
		{
			return assessmentResult;
		}
		attempts++;
		if (!usableBefore || !target.Contains(x, y))
		{
			return null;
		}
		if (target.Action == "double" && action == "left")
		{
			return null;
		}
		if (action != target.Action)
		{
			wrong++;
			return null;
		}
		if (action == "left")
		{
			pendingSingle = Result(now, success: true, "single tap confirmed");
			singleAt = now;
			return null;
		}
		return Finish(now, success: true, "required action received");
	}

	public AssessmentResult Cancel(double now, string reason)
	{
		return Finish(Math.Max(lastTime, now), success: false, reason);
	}

	private AssessmentResult Finish(double now, bool success, string reason)
	{
		completed = true;
		pendingSingle = null;
		return Result(now, success, reason);
	}

	private AssessmentResult Result(double now, bool success, string reason)
	{
		double num = Math.Sqrt(Math.Pow(lastX - startX, 2.0) + Math.Pow(lastY - startY, 2.0));
		return new AssessmentResult(target.Id, target.Action, success, reason, Math.Max(0.0, now - started) * 1000.0, double.IsFinite(firstEntry) ? new double?((firstEntry - started) * 1000.0) : ((double?)null), path, (path > 0.0) ? Math.Clamp(num / path, 0.0, 1.0) : 0.0, entries, overshoots, maximumOvershoot, (samples > 0) ? new double?(Math.Sqrt(Math.Pow(lastX - target.X - target.Width / 2.0, 2.0) + Math.Pow(lastY - target.Y - target.Height / 2.0, 2.0))) : ((double?)null), (insideSamples > 1) ? new double?(Math.Sqrt((m2X + m2Y) / (double)(insideSamples - 1))) : ((double?)null), attempts, wrong, losses, samples);
	}
}
