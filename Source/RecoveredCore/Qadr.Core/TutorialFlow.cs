using System;

namespace Qadr.Core;

public sealed class TutorialFlow
{
	public static readonly TutorialPage[] Pages = new TutorialPage[8]
	{
		new TutorialPage("You are always in control", "Esc", "Pause → rest → resume", "Esc stops mouse control everywhere. It never starts control. You can also use the red Stop button or say stop the app after a spoken prompt. Say resume to start again.", "The tutorial does not move or click your mouse. Choose Stop whenever you need a break."),
		new TutorialPage("Choose your camera", "◉", "Choose camera → full face visible", "If you have several cameras, Qadr reads their numbers. Say camera one or choose a device manually. A single camera is selected automatically.", "After this tutorial, camera setup starts automatically. A saved movement profile avoids repeating full calibration."),
		new TutorialPage("Find your comfortable movement", "↔", "Relax → small turns → relax", "Stay in a supported, comfortable position. The oval fits your face and distance. Calibration asks for small movements left, right, up and down. Move only as far as is comfortable.", "You do not need to lean forward or force a large movement. If tracking is lost, rest while Qadr finds your face again."),
		new TutorialPage("Make a left click", "⌖", "Point → rest → click", "Move the pointer to a target. With the default dwell setting, rest there until the ring fills, then move away before your next click. You can learn a smile instead in Preferences.", "Dwell time and gesture methods follow your saved preferences. Start with one comfortable click method."),
		new TutorialPage("Make a right click", "◡", "Close → hold → reopen", "After learning your eye gesture, close your chosen eye deliberately, hold until the bar fills, then reopen to right-click. Ordinary short blinks are ignored.", "The default wink hold is 0.30 seconds; both eyes need at least 0.40 seconds. Your saved settings may be different. You can disable eye gestures."),
		new TutorialPage("Use the mouse tools", "▤", "Select tool → click target → action", "Open Mouse tools and use the right-edge arrow. After learning your eyebrow raise, raise, hold briefly, then relax to double-click directly. The tools also offer right click, double-click, middle click, drag/drop, scrolling and Back/Forward. Then activate your click on the target.", "Learn the optional lip pucker to pick up and drop directly. Learn mouth opening to toggle head scrolling, then move your head up/down and repeat the mouth gesture to exit. You can use the selectable tools as an alternative. Esc or tracking loss releases dragging and stops scrolling."),
		new TutorialPage("Make Qadr comfortable for you", "⚙", "Adjust → practise → save", "Preferences lets you adjust pointer speed, responsiveness, click timing and gesture choices. Adaptive precision is the default; Classic smoothing is also available. Use the practice target before a real task.", "Start with one or two comfortable gestures. If your position changes, let the face guide refit; relearn movement when needed."),
		new TutorialPage("Save a useful routine", "▷", "Record → review → confirm", "In Routines, record a short supported task, give it a name, and review it before using it. With Voice commands enabled, say computer run followed by its name, then computer confirm.", "Start with a simple, reversible task. Recording does not yet capture every mouse action. You are ready: start camera setup, calibrate, then practise.")
	};

	public int Page { get; private set; } = -1;

	public bool Completed { get; private set; }

	public bool Start()
	{
		if (Completed || Page >= 0)
		{
			return false;
		}
		Page = 0;
		return true;
	}

	public bool Next()
	{
		if (Completed || Page < 0)
		{
			return false;
		}
		if (Page == Pages.Length - 1)
		{
			Complete();
			return true;
		}
		Page++;
		return false;
	}

	public void Back()
	{
		if (!Completed && Page > 0)
		{
			Page--;
		}
	}

	public void Complete()
	{
		Completed = true;
		Page = -1;
	}

	public static string? Resolve(string text, bool choice)
	{
		string text2 = string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
		string result;
		if (choice)
		{
			switch (text2)
			{
			case "yes":
			case "yes tutorial":
			case "show tutorial":
				result = "tutorialStart";
				break;
			case "no":
			case "no tutorial":
			case "skip tutorial":
				result = "tutorialSkip";
				break;
			default:
				result = null;
				break;
			}
			return result;
		}
		switch (text2)
		{
		case "next":
			result = "tutorialNext";
			break;
		case "back":
			result = "tutorialBack";
			break;
		case "start":
		case "skip tutorial":
			result = "tutorialSkip";
			break;
		default:
			result = null;
			break;
		}
		return result;
	}

	public static string[] Phrases(bool choice)
	{
		if (choice)
		{
			return new string[7] { "yes", "no", "yes tutorial", "no tutorial", "show tutorial", "skip tutorial", "computer stop" };
		}
		return new string[5] { "next", "back", "start", "skip tutorial", "computer stop" };
	}

	public static int ReadPage(string cue)
	{
		if (!cue.StartsWith("tutorial:") || !int.TryParse(cue.Substring(9), out var result))
		{
			return 0;
		}
		return Math.Clamp(result, 0, Pages.Length - 1);
	}
}
