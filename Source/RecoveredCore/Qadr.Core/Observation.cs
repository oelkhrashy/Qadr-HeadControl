using System.Text.Json.Serialization;

namespace Qadr.Core;

public sealed record Observation(double Yaw, double Pitch, double Smile, double Mouth, double Blink, double Timestamp, double X = 0.5, double Y = 0.5, double Scale = 0.2, double LeftEye = -1.0, double RightEye = -1.0, double FaceX = 0.5, double FaceY = 0.5, double FaceWidth = 0.32, double FaceHeight = 0.5, double Eyebrow = 0.0, double Pucker = 0.0)
{
	[JsonIgnore]
	public bool IsUsable
	{
		get
		{
			if (double.IsFinite(Pucker))
			{
				double pucker = Pucker;
				if (pucker >= 0.0 && pucker <= 1.0 && double.IsFinite(Eyebrow))
				{
					pucker = Eyebrow;
					if (pucker >= 0.0 && pucker <= 1.0 && double.IsFinite(Timestamp) && double.IsFinite(Yaw) && double.IsFinite(Pitch) && double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Scale))
					{
						pucker = Scale;
						if (pucker > 0.0 && pucker <= 1.0 && double.IsFinite(Smile))
						{
							pucker = Smile;
							if (pucker >= 0.0 && pucker <= 1.0 && double.IsFinite(Mouth))
							{
								pucker = Mouth;
								if (pucker >= 0.0 && pucker <= 1.0 && double.IsFinite(Blink))
								{
									pucker = Blink;
									if (pucker >= 0.0 && pucker <= 1.0 && double.IsFinite(LeftEye))
									{
										if (LeftEye != -1.0)
										{
											pucker = LeftEye;
											if (!(pucker >= 0.0) || !(pucker <= 1.0))
											{
												goto IL_026b;
											}
										}
										if (double.IsFinite(RightEye))
										{
											if (RightEye != -1.0)
											{
												pucker = RightEye;
												if (!(pucker >= 0.0) || !(pucker <= 1.0))
												{
													goto IL_026b;
												}
											}
											if (double.IsFinite(FaceX) && double.IsFinite(FaceY) && double.IsFinite(FaceWidth) && double.IsFinite(FaceHeight) && FaceWidth > 0.0)
											{
												return FaceHeight > 0.0;
											}
										}
									}
								}
							}
						}
					}
				}
			}
			goto IL_026b;
			IL_026b:
			return false;
		}
	}
}
