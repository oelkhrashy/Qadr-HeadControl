namespace Qadr.Core;

public sealed class SpeechCommandGate
{
	private bool speaking;

	private double until;

	public void Set(bool active, double now)
	{
		speaking = active;
		until = now + 0.6;
	}

	public bool Blocked(double now)
	{
		if (!speaking)
		{
			return now < until;
		}
		return true;
	}
}
