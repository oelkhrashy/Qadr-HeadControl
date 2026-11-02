namespace Qadr.Core;

public sealed class StartupFlow
{
	private readonly bool enabled;

	public bool Pending { get; private set; }

	public StartupFlow(bool enabled)
	{
		this.enabled = enabled;
		Pending = enabled;
	}

	public void Begin()
	{
		Pending = enabled;
	}

	public void Cancel()
	{
		Pending = false;
	}

	public bool Advance()
	{
		return Pending;
	}

	public void Started()
	{
		Pending = false;
	}
}
