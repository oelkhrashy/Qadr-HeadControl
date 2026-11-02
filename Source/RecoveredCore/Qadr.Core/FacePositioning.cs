using System;

namespace Qadr.Core;

public sealed class FacePositioning
{
	private double targetX = 0.5;

	private double targetY = 0.48;

	private double targetWidth = 0.34;

	private double targetHeight = 0.53;

	private double neutralYaw;

	private double neutralPitch;

	private double scale;

	private Observation? candidate;

	private double since;

	private bool personalized;

	public Settings Settings { get; set; } = new Settings();

	public int Revision { get; private set; }

	public Observation? FittedObservation { get; private set; }

	public void UseComfortablePosition(Observation o)
	{
		FittedObservation = o;
		Revision++;
		targetX = o.FaceX;
		targetY = o.FaceY;
		neutralYaw = o.Yaw;
		neutralPitch = o.Pitch;
		scale = o.Scale;
		personalized = true;
		candidate = null;
		targetWidth = Math.Clamp(o.FaceWidth * Settings.PositionGuidePadding, 0.1, Math.Max(0.1, Math.Min(0.95, 2.0 * Math.Min(targetX, 1.0 - targetX))));
		targetHeight = Math.Clamp(o.FaceHeight * Settings.PositionGuidePadding, 0.12, Math.Max(0.12, Math.Min(0.96, 2.0 * Math.Min(targetY, 1.0 - targetY))));
	}

	public FacePosition Check(Observation? o, bool placement = true)
	{
		if ((object)o == null)
		{
			candidate = null;
			return Result("find", "Bring your face into the camera view");
		}
		if (o.Scale < Settings.PositionMinimumScale || o.FaceHeight < Settings.PositionMinimumHeight)
		{
			candidate = null;
			return Result("closer", "Your face is too small to track reliably. Bring the camera a little closer");
		}
		if (o.Scale > Settings.PositionMaximumScale || o.FaceHeight > Settings.PositionMaximumHeight)
		{
			candidate = null;
			return Result("farther", "Your face needs more room in the view. Move the camera a little farther away");
		}
		if (o.FaceX - o.FaceWidth / 2.0 < 0.015 || o.FaceX + o.FaceWidth / 2.0 > 0.985 || o.FaceY - o.FaceHeight / 2.0 < 0.01 || o.FaceY + o.FaceHeight / 2.0 > 0.99)
		{
			candidate = null;
			return Result("frame", "Keep your whole face inside the camera view");
		}
		if (!placement && personalized)
		{
			targetX += (o.FaceX - targetX) * 0.12;
			targetY += (o.FaceY - targetY) * 0.12;
			targetWidth += (Math.Clamp(o.FaceWidth * Settings.PositionGuidePadding, 0.1, 0.95) - targetWidth) * 0.12;
			targetHeight += (Math.Clamp(o.FaceHeight * Settings.PositionGuidePadding, 0.12, 0.96) - targetHeight) * 0.12;
			return Result("good", "Tracking your face; the guide follows your distance", ready: true);
		}
		if (!personalized || Math.Abs(o.Scale / scale - 1.0) > Settings.PositionScaleTolerance || (placement && (Math.Abs(o.FaceX - targetX) > 0.14 || Math.Abs(o.FaceY - targetY) > 0.16)))
		{
			if ((object)candidate == null || o.Timestamp - candidate.Timestamp > 0.25 || Math.Abs(o.Scale / candidate.Scale - 1.0) > Settings.PositionScaleTolerance || Math.Abs(o.FaceX - candidate.FaceX) > Settings.PositionCentreTolerance || Math.Abs(o.FaceY - candidate.FaceY) > Settings.PositionCentreTolerance || Math.Abs(o.Yaw - candidate.Yaw) > Settings.PositionAngleTolerance || Math.Abs(o.Pitch - candidate.Pitch) > Settings.PositionAngleTolerance)
			{
				candidate = o;
				since = o.Timestamp;
			}
			else
			{
				candidate = candidate with
				{
					Timestamp = o.Timestamp
				};
			}
			if (o.Timestamp - since < Settings.PositionSettleSeconds)
			{
				return Result("adapting", "Hold comfortably while the oval fits your face and distance");
			}
			UseComfortablePosition(o);
		}
		if (placement)
		{
			if (Math.Abs(o.FaceX - targetX) > 0.14)
			{
				return Result((o.FaceX < targetX) ? "right" : "left", (o.FaceX < targetX) ? "Move gently toward the right arrow" : "Move gently toward the left arrow");
			}
			if (Math.Abs(o.FaceY - targetY) > 0.16)
			{
				return Result((o.FaceY < targetY) ? "down" : "up", (o.FaceY < targetY) ? "Lower the camera aim, or move toward the down arrow" : "Raise the camera aim, or move toward the up arrow");
			}
			if (Math.Abs(o.Yaw - neutralYaw) > 0.32 || Math.Abs(o.Pitch - neutralPitch) > 0.42)
			{
				return Result("angle", personalized ? "Return to your comfortable angle" : "Look toward the camera, or choose Use my comfortable position");
			}
		}
		return Result("good", "Your face and distance are learned. Relax comfortably", ready: true);
		FacePosition Result(string code, string hint, bool ready = false)
		{
			return new FacePosition(code, hint, ready, targetX, targetY, targetWidth, targetHeight);
		}
	}
}
