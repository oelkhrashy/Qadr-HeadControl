using System.Linq;

namespace Qadr.Core;

public sealed class StartupPointer
{
	private int? pending;

	public bool Completed { get; private set; }

	public bool Pending => pending.HasValue;

	public void Arm(int epoch, bool recentre = false)
	{
		if (recentre)
		{
			Completed = false;
		}
		if (!Completed)
		{
			pending = epoch;
		}
	}

	public void Cancel()
	{
		pending = null;
	}

	public (double X, double Y)? Plan(int epoch, bool inputReady, double left, double top, double width, double height)
	{
		if (pending.HasValue && pending != epoch)
		{
			pending = null;
			return null;
		}
		if (Completed || !pending.HasValue || !inputReady || !new double[4] { left, top, width, height }.All(double.IsFinite) || width < 1.0 || height < 1.0)
		{
			return null;
		}
		return (left + (width - 1.0) / 2.0, top + (height - 1.0) / 2.0);
	}

	public void Complete()
	{
		if (pending.HasValue)
		{
			Completed = true;
			pending = null;
		}
	}
}
