namespace Qadr.Core;

public sealed class LaunchAssessmentFlow(bool enabled = true)
{
	public const int TargetCount = 5;

	public const double TargetSeconds = 8.0;

	private string phase = (enabled ? "pending" : "disabled");

	private int attempted;

	private int passed;

	private int skipped;

	private int round = 1;

	public string NextId => ((char)(((round == 1) ? 65 : 70) + attempted)).ToString();

	public LaunchAssessmentStatus Status => new LaunchAssessmentStatus(phase, attempted, passed, skipped, 5, round);

	public bool Required => Status.Required;

	public bool Ready()
	{
		string text = phase;
		if ((!(text == "pending") && !(text == "interrupted")) || 1 == 0)
		{
			return false;
		}
		phase = "ready";
		return true;
	}

	public bool Begin()
	{
		if (phase != "ready")
		{
			return false;
		}
		round = 1;
		attempted = (passed = (skipped = 0));
		phase = "running";
		return true;
	}

	public bool BeginValidation()
	{
		if (phase != "running" || attempted != 5)
		{
			return false;
		}
		round = 2;
		attempted = (passed = (skipped = 0));
		phase = "validating";
		return true;
	}

	public bool Record(AssessmentResult result)
	{
		string text = phase;
		bool flag = ((text == "running" || text == "validating") ? true : false);
		if (!flag || attempted >= 5 || result.Id != NextId || result.ExpectedAction != "move")
		{
			return false;
		}
		attempted++;
		if (result.Completed)
		{
			passed++;
		}
		else
		{
			skipped++;
		}
		return true;
	}

	public bool Finish()
	{
		string text = phase;
		bool flag = ((text == "running" || text == "validating") ? true : false);
		if (!flag || attempted != 5)
		{
			return false;
		}
		phase = "complete";
		return true;
	}

	public void Interrupt()
	{
		if (Required)
		{
			phase = "interrupted";
		}
	}
}
