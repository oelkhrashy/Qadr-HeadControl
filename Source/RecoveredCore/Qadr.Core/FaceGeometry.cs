using System.Text.Json.Serialization;

namespace Qadr.Core;

public sealed record FaceGeometry(double EyeX, double EyeY, double EyeScale, double NoseX, double NoseY, double Left, double Top, double Right, double Bottom, double OutsideFraction)
{
	[JsonIgnore]
	public bool IsValid
	{
		get
		{
			if (double.IsFinite(EyeX) && double.IsFinite(EyeY) && double.IsFinite(EyeScale) && double.IsFinite(NoseX) && double.IsFinite(NoseY) && double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) && double.IsFinite(Bottom) && double.IsFinite(OutsideFraction))
			{
				double eyeScale = EyeScale;
				if (eyeScale >= 0.045 && eyeScale <= 0.65 && Right > Left && Bottom > Top)
				{
					eyeScale = OutsideFraction;
					if (eyeScale >= 0.0)
					{
						return eyeScale <= 0.08;
					}
					return false;
				}
			}
			return false;
		}
	}
}
