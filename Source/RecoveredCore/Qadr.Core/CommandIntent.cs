namespace Qadr.Core;

public static class CommandIntent
{
	public static bool RequiresEpoch(Command command)
	{
		bool flag;
		switch (command.Type)
		{
		case "adjustMovement":
		case "undoAdjustment":
		case "assessmentSkip":
		case "movementTuningUndo":
		case "launchAssessmentBegin":
		case "launchAssessmentFinish":
		case "assessmentTarget":
		case "assessmentClick":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = flag || (command.Type == "motionRecord" && command.Enabled);
		if (!flag2)
		{
			bool flag3;
			switch (command.Type)
			{
			case "record":
			case "camera":
			case "resume":
			case "recentre":
			case "useDwell":
			case "confirm":
			case "refresh":
			case "learnRest":
			case "resetRest":
			case "calibrate":
			case "learnDrag":
			case "learnEyes":
			case "learnEyebrows":
			case "tutorialStart":
			case "tutorialBack":
			case "tutorialNext":
			case "tutorialSkip":
			case "comfortablePosition":
			case "learnScroll":
			case "learnSmile":
			case "restoreProfile":
			case "diagnosticVoice":
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			flag2 = flag3;
		}
		bool flag4 = flag2;
		if (!flag4)
		{
			bool flag3;
			switch (command.Type)
			{
			case "voice":
			case "cameraVoice":
			case "tutorialVoice":
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			flag4 = flag3 && command.Enabled;
		}
		return flag4;
	}

	public static bool IsCurrent(Command command, int epoch)
	{
		if (RequiresEpoch(command))
		{
			return command.UserStopEpoch == epoch;
		}
		return true;
	}
}
