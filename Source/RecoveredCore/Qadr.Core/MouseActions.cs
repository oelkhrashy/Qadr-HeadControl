using System;
using System.Linq;

namespace Qadr.Core;

public sealed class MouseActions
{
	public static readonly MouseModeDefinition[] Modes = new MouseModeDefinition[11]
	{
		new MouseModeDefinition("left", "Left click", "Rest or gesture on the target to left-click."),
		new MouseModeDefinition("right", "Right click", "Your next click opens the target's context menu."),
		new MouseModeDefinition("double", "Double-click", "Your next click performs a double-click, then returns to left click."),
		new MouseModeDefinition("drag", "Drag / drop", "Click the item to hold it, move to its destination, then click again to drop."),
		new MouseModeDefinition("middle", "Middle click", "Your next click presses the mouse wheel button."),
		new MouseModeDefinition("scroll-up", "↑ Scroll up", "Rest on content to scroll repeatedly. Move to stop; choose Left click to leave scroll mode."),
		new MouseModeDefinition("scroll-down", "↓ Scroll down", "Rest on content to scroll repeatedly. Move to stop; choose Left click to leave scroll mode."),
		new MouseModeDefinition("scroll-left", "← Scroll left", "Rest on content to scroll sideways. Move to stop; choose Left click to leave scroll mode."),
		new MouseModeDefinition("scroll-right", "→ Scroll right", "Rest on content to scroll sideways. Move to stop; choose Left click to leave scroll mode."),
		new MouseModeDefinition("back", "Back", "Your next click sends the mouse Back button to the application."),
		new MouseModeDefinition("forward", "Forward", "Your next click sends the mouse Forward button to the application.")
	};

	public string Mode { get; private set; } = "left";

	public bool Dragging { get; private set; }

	public bool Scrolling => Mode.StartsWith("scroll-", StringComparison.Ordinal);

	public string Help
	{
		get
		{
			if (!Dragging)
			{
				return Modes.First((MouseModeDefinition m) => m.Id == Mode).Hint;
			}
			return "Dragging — move to the destination and click to drop. Esc releases the button.";
		}
	}

	public bool Select(string id)
	{
		if (!Modes.Any((MouseModeDefinition m) => m.Id == id))
		{
			return false;
		}
		Mode = id;
		return true;
	}

	public void Reset()
	{
		Mode = "left";
		Dragging = false;
	}

	public MousePlan Trigger(string source, int notches = 1, double doubleGap = 0.06)
	{
		if (Dragging)
		{
			Reset();
			return new MousePlan("Dropped — left click", new MouseStep[1]
			{
				new MouseStep("up")
			});
		}
		bool flag;
		switch (source)
		{
		case "right":
		case "double":
		case "drag":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		string text = (flag ? source : Mode);
		if (text == "drag")
		{
			Dragging = true;
			return new MousePlan("Dragging — click again to drop", new MouseStep[1]
			{
				new MouseStep("down")
			});
		}
		if (text.StartsWith("scroll-", StringComparison.Ordinal))
		{
			flag = ((text == "scroll-left" || text == "scroll-right") ? true : false);
			bool flag2 = flag;
			flag = ((text == "scroll-up" || text == "scroll-right") ? true : false);
			int num = (flag ? 1 : (-1));
			return new MousePlan(text.Replace('-', ' '), new MouseStep[1]
			{
				new MouseStep(flag2 ? "horizontal" : "vertical", "left", num * Math.Clamp(notches, 1, 5))
			});
		}
		Mode = "left";
		switch (text)
		{
		case "right":
		case "middle":
		case "back":
		case "forward":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		string text2 = (flag ? text : "left");
		if (text == "double")
		{
			return new MousePlan("Double-click", new MouseStep[4]
			{
				new MouseStep("down"),
				new MouseStep("up"),
				new MouseStep("down", "left", 0, doubleGap),
				new MouseStep("up", "left", 0, doubleGap)
			});
		}
		return new MousePlan(text2 + " click", new MouseStep[2]
		{
			new MouseStep("down", text2),
			new MouseStep("up", text2)
		});
	}
}
