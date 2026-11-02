using System;

namespace Qadr.Core;

public sealed class ManualAdjustmentHistory
{
	private MovementParameters? previous;

	private MovementParameters? expected;

	private string message = "Choose one small change. Undo restores your previous movement settings.";

	public void Observe(Settings settings)
	{
		if ((object)expected != null && MovementParameters.From(settings) != expected)
		{
			previous = (expected = null);
			message = "Movement settings changed elsewhere. Choose a new adjustment to enable Undo.";
		}
	}

	public ManualAdjustmentStatus Status(Settings settings)
	{
		Observe(settings);
		return new ManualAdjustmentStatus(message, (object)previous != null);
	}

	public Settings? Adjust(Settings settings, string? choice)
	{
		Observe(settings);
		MovementParameters movementParameters = MovementParameters.From(settings);
		MovementParameters movementParameters2 = choice switch
		{
			"slower" => movementParameters with
			{
				Sensitivity = Math.Max(0.3, Math.Round(movementParameters.Sensitivity * 0.9, 3))
			}, 
			"reach" => movementParameters with
			{
				TravelBoost = Math.Min(4.0, Math.Round(movementParameters.TravelBoost + 0.25, 2))
			}, 
			"steady" => movementParameters with
			{
				PrecisionNoise = Math.Min(2.0, Math.Round(movementParameters.PrecisionNoise * 1.1, 3))
			}, 
			_ => null, 
		};
		if ((object)movementParameters2 == null || !movementParameters2.Valid)
		{
			return null;
		}
		if (movementParameters2 == movementParameters)
		{
			message = "This setting is already at its limit. Your settings are unchanged.";
			return null;
		}
		previous = movementParameters;
		expected = movementParameters2;
		string text;
		if (choice == "slower")
		{
			text = "Slower movement applied. Try pointing, or choose Undo.";
		}
		else
		{
			text = ((!(choice == "reach")) ? "More steadiness applied. Small corrections may need more movement. Try it, or choose Undo." : "More screen reach applied. Try a longer stroke, or choose Undo.");
		}
		message = text;
		return movementParameters2.Apply(settings);
	}

	public Settings? Undo(Settings settings)
	{
		Observe(settings);
		if ((object)previous == null)
		{
			return null;
		}
		Settings result = previous.Apply(settings);
		previous = (expected = null);
		message = "Previous movement settings restored.";
		return result;
	}

	public void Explain(string reason)
	{
		message = reason;
	}
}
