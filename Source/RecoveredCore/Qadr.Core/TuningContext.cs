namespace Qadr.Core;

public sealed record TuningContext(string Calibration, string Mapping, string Algorithm, int Width, int Height)
{
	public static TuningContext Create(Profile p, Settings s, int width, int height)
	{
		return new TuningContext(Storage.DeviceKey(Protocol.Encode(new
		{
			RelativeMappingRevision = 3,
			CoordinateVersion = p.CoordinateVersion,
			Modes = p.Modes,
			Ranges = p.Ranges,
			Noise = p.Noise,
			Responsiveness = s.Responsiveness,
			Deadzone = s.Deadzone,
			RelativeSpeedLimit = s.RelativeSpeedLimit,
			BalanceHorizontal = s.BalanceHorizontal,
			BalanceVertical = s.BalanceVertical,
			DirectResponse = s.DirectResponse,
			PrecisionMaximumBand = s.PrecisionMaximumBand,
			InvertX = s.InvertX,
			InvertY = s.InvertY,
			PersonalizeMovement = s.PersonalizeMovement
		})), s.Mapping, s.MovementAlgorithm, width, height);
	}
}
