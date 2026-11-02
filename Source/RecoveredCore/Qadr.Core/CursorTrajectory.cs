using System;

namespace Qadr.Core;

public sealed class CursorTrajectory
{
	public const double AbsoluteGlideSpeed = 2.5;

	private double x;

	private double y;

	private double targetX;

	private double targetY;

	private double vx;

	private double vy;

	private double received;

	private double left;

	private double top;

	private double width;

	private double height;

	private double interval = double.NaN;

	private double lastObservation = double.NaN;

	private bool rate;

	private bool absolute;

	public bool Ready { get; private set; }

	private void Observed(double captured)
	{
		if (double.IsFinite(captured))
		{
			double num = captured - lastObservation;
			lastObservation = captured;
			if (num > 0.0 && num <= 0.2)
			{
				interval = (double.IsNaN(interval) ? num : (interval + (num - interval) * 0.2));
			}
		}
	}

	public void Reset(double px, double py)
	{
		x = (targetX = px);
		y = (targetY = py);
		Ready = true;
		rate = (absolute = false);
		vx = (vy = 0.0);
	}

	public void Clear()
	{
		Ready = (rate = (absolute = false));
		vx = (vy = 0.0);
		lastObservation = interval = double.NaN;
	}

	public void SetAbsolute(double positionX, double positionY, double px, double py, double areaLeft, double areaTop, double areaWidth, double areaHeight, double captured = double.NaN)
	{
		Observed(captured);
		if (!double.IsFinite(positionX) || !double.IsFinite(positionY) || !double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(areaLeft) || !double.IsFinite(areaTop) || !double.IsFinite(areaWidth) || !double.IsFinite(areaHeight) || areaWidth < 1.0 || areaHeight < 1.0)
		{
			Clear();
			return;
		}
		if (!Ready || rate || Math.Abs(px - x) > 8.0 || Math.Abs(py - y) > 8.0)
		{
			Reset(px, py);
		}
		left = areaLeft;
		top = areaTop;
		width = areaWidth;
		height = areaHeight;
		absolute = true;
		targetX = left + (width - 1.0) / 2.0 * (1.0 + Math.Clamp(positionX, -1.0, 1.0));
		targetY = top + (height - 1.0) / 2.0 * (1.0 + Math.Clamp(positionY, -1.0, 1.0));
	}

	public void SetRate(double velocityX, double velocityY, double px, double py, double desktopLeft, double desktopTop, double desktopWidth, double desktopHeight, double captured)
	{
		if (!double.IsFinite(velocityX) || !double.IsFinite(velocityY) || !double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(captured) || !double.IsFinite(desktopLeft) || !double.IsFinite(desktopTop) || !double.IsFinite(desktopWidth) || !double.IsFinite(desktopHeight) || desktopWidth <= 0.0 || desktopHeight <= 0.0)
		{
			Clear();
			return;
		}
		if (!Ready || !rate || Math.Abs(px - x) > 8.0 || Math.Abs(py - y) > 8.0)
		{
			Reset(px, py);
		}
		vx = velocityX;
		vy = velocityY;
		left = desktopLeft;
		top = desktopTop;
		width = desktopWidth;
		height = desktopHeight;
		received = captured;
		rate = true;
	}

	public void Add(double dx, double dy, double px, double py, double left, double top, double width, double height, bool stopX = false, bool stopY = false, double captured = double.NaN)
	{
		if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(px) || !double.IsFinite(py)
			|| !double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height)
			|| width < 1 || height < 1) { Clear(); return; }
		Observed(captured);
		if (!Ready || absolute || rate)
		{
			Reset(px, py);
		}
		if (Math.Abs(px - x) > 8.0 || Math.Abs(py - y) > 8.0)
		{
			Reset(px, py);
		}
		if (stopX)
		{
			x = (targetX = px);
		}
		else
		{
			targetX = Math.Clamp(targetX + dx, left, left + width - 1.0);
		}
		if (stopY)
		{
			y = (targetY = py);
		}
		else
		{
			targetY = Math.Clamp(targetY + dy, top, top + height - 1.0);
		}
	}

	public (double X, double Y) Step(double dt, double now = double.NaN, bool direct = false)
	{
		if (!Ready)
		{
			return (X: x, Y: y);
		}
		if (!double.IsFinite(dt) || dt < 0.0)
		{
			Clear();
			return (X: x, Y: y);
		}
		dt = Math.Min(dt, 0.05);
		// Drop unfinished interpolation if no fresh head observation arrives.
		if (!rate && double.IsFinite(now) && double.IsFinite(lastObservation)
			&& (now < lastObservation || now - lastObservation > TrackingTiming.MaximumAgeSeconds))
		{
			targetX = x; targetY = y;
			return (X: x, Y: y);
		}
		if (rate)
		{
			if (!double.IsFinite(now) || now < received || now - received > 0.2)
			{
				vx = (vy = 0.0);
				return (X: x, Y: y);
			}
			x = Math.Clamp(x + vx * dt, left, left + width - 1.0);
			y = Math.Clamp(y + vy * dt, top, top + height - 1.0);
			targetX = x;
			targetY = y;
			return (X: x, Y: y);
		}
		double num = (direct ? 0.008 : 0.018);
		if (double.IsFinite(interval))
		{
			num = Math.Max(num, interval / 3.0);
		}
		double num2 = 1.0 - Math.Exp((0.0 - dt) / num);
		double num3 = (targetX - x) * num2;
		double num4 = (targetY - y) * num2;
		if (absolute)
		{
			double num5 = Math.Sqrt(num3 * num3 + num4 * num4);
			double num6 = 2.5 * width * dt;
			if (num5 > num6)
			{
				num3 *= num6 / num5;
				num4 *= num6 / num5;
			}
		}
		x += num3;
		y += num4;
		return (X: x, Y: y);
	}
}
