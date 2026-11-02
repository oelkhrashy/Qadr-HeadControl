namespace Qadr.Core;

public sealed record SetupPresentation(bool Setup, int Step, string Heading, string Instruction, string FaceState, string Checks)
{
	public static SetupPresentation From(EngineStatus s)
	{
		bool flag;
		switch (s.State)
		{
		case "Starting":
		case "Tutorial":
		case "Calibrating":
		case "Positioning":
		case "ClickSetup":
		case "LearnClick":
		case "TutorialChoice":
		case "ChooseCamera":
		case "LearnRest":
		case "Ready":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = flag;
		int num;
		string state;
		if (s.State == "LearnClick")
		{
			num = 3;
		}
		else
		{
			int num2;
			if (s.State == "Ready")
			{
				num2 = 4;
			}
			else
			{
				state = s.State;
				flag = ((state == "Calibrating" || state == "ClickSetup") ? true : false);
				num2 = ((!flag) ? 1 : 2);
			}
			num = num2;
		}
		int num3 = num;
		FacePosition position = s.Position;
		string text;
		if (s.Face)
		{
			if ((object)position != null && position.Ready)
			{
				text = "Locked";
			}
			else
			{
				text = ((position?.Code == "adapting") ? "Fitting" : "Found");
			}
		}
		else
		{
			text = "No face";
		}
		switch (s.State)
		{
		case "Starting":
			state = "Wait while your camera opens";
			break;
		case "LearnRest":
			state = "Rest naturally while Qadr learns your steadiness";
			break;
		case "ChooseCamera":
			state = "Choose your camera by voice or manually";
			break;
		case "Positioning":
			state = position?.Code switch
			{
				"find" => "Bring your face into view", 
				"closer" => "Bring the camera a little closer", 
				"farther" => "Move the camera a little farther away", 
				"frame" => "Keep your whole face in view", 
				"right" => "Move gently right →", 
				"left" => "Move gently left ←", 
				"up" => "Move gently up ↑", 
				"down" => "Move gently down ↓", 
				"angle" => "Return to your comfortable angle", 
				_ => "Hold comfortably while the guide fits", 
			};
			break;
		case "Calibrating":
			if (s.Cue == "rest")
			{
				state = "Relax and hold comfortably";
			}
			else
			{
				state = ((s.Cue == "vertical") ? "Look gently up and down ↑ ↓" : "Turn gently left and right ← →");
			}
			break;
		case "LearnClick":
		{
			string cue = s.Cue;
			flag = ((cue == "pucker" || cue == "scroll") ? true : false);
			if (flag)
			{
				if (s.Title.StartsWith("Now"))
				{
					state = "Relax your mouth";
				}
				else
				{
					state = ((s.Cue == "pucker") ? "Pucker gently, hold, then relax" : "Open your mouth comfortably, hold, then relax");
				}
			}
			else if (s.Cue == "brows")
			{
				state = (s.Title.StartsWith("Now") ? "Relax your eyebrows" : "Raise your eyebrows gently and hold");
			}
			else if (s.Cue == "rest")
			{
				state = "Relax your face and keep your eyes open";
			}
			else if (s.Title.StartsWith("Now"))
			{
				state = "Relax and reopen your eyes";
			}
			else
			{
				state = ((s.Cue == "smile") ? "Smile gently, hold, then relax" : "Close one or both eyes, hold, then reopen");
			}
			break;
		}
		case "Ready":
			state = "Settle comfortably; control starts automatically";
			break;
		case "Tracking":
			state = "Control on · Esc stops the mouse";
			break;
		case "Degraded":
		case "Lost":
			state = "Tracking paused · return your face to view";
			break;
		case "Stopped":
			state = "Control stopped · choose Resume when ready";
			break;
		default:
			state = s.Title;
			break;
		}
		string instruction = state;
		string text2;
		if (flag2)
		{
			text2 = num3 switch
			{
				1 => "Position your face", 
				2 => "Learn your comfortable movement", 
				3 => "Learn your click gesture", 
				_ => "Return to rest", 
			};
		}
		else
		{
			if (s.State == "Tracking")
			{
				state = "Control on";
			}
			else
			{
				string cue = s.State;
				flag = ((cue == "Lost" || cue == "Degraded") ? true : false);
				state = (flag ? "Tracking paused" : "Control stopped");
			}
			text2 = state;
		}
		string heading = text2;
		string value;
		if (s.Face)
		{
			value = ((position?.Code == "frame") ? "Framing · keep your whole face visible" : "Framing · face detected");
		}
		else
		{
			value = "Framing · bring your face into view";
		}
		if (!s.Face)
		{
			text2 = "Distance · waiting for geometry";
		}
		else
		{
			state = position?.Code;
			flag = ((state == "closer" || state == "farther") ? true : false);
			text2 = (flag ? "Distance · adjust camera" : ("Distance · " + ((position?.Code == "adapting") ? "fitting" : "supported view")));
		}
		string value2 = text2;
		string text3;
		if (text == "Locked")
		{
			text3 = "position learned";
		}
		else
		{
			text3 = ((text == "Fitting") ? "hold comfortably" : "waiting for a stable position");
		}
		string value3 = "Steadiness · " + text3;
		return new SetupPresentation(flag2, num3, heading, instruction, text, $"Face · {text}\n\n{value}\n\n{value2}\n\n{value3}\n\nTracking · {s.State}");
	}
}
