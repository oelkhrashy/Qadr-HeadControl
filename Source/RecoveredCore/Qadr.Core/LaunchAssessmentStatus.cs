namespace Qadr.Core;

public sealed record LaunchAssessmentStatus(string Phase, int Attempted, int Passed, int Skipped, int Total = 5, int Round = 1)
{
	public bool Required
	{
		get
		{
			if (Phase != "complete")
			{
				return Phase != "disabled";
			}
			return false;
		}
	}
}
