using System;

namespace Qadr.Core;

public sealed record Settings
{
	public int TrackingFps { get; init; } = 30;

	public bool MotionDiagnosticsOnStartup { get; init; }

	public bool DirectResponse { get; init; } = true;

	public bool HandsFreeVoice { get; init; } = true;

	public bool SpokenGuidance { get; init; }

	public double Sensitivity { get; init; } = 1.0;

	public double RelativeSpeedLimit { get; init; }

	public double RelativeTravelBoost { get; init; } = 1.0;

	public double SteadyHold { get; init; } = 0.75;

	public double CentreAnchoring { get; init; } = 0;

	public double Responsiveness { get; init; } = 0.38;

	public double Deadzone { get; init; } = 0.035;

	public bool PuckerDrag { get; init; }

	public bool PuckerLearned { get; init; }

	public double PuckerThreshold { get; init; } = 0.35;

	public double PuckerRestLevel { get; init; }

	public bool MouthScroll { get; init; }

	public bool ScrollGestureLearned { get; init; }

	public double ScrollGestureThreshold { get; init; } = 0.35;

	public double ScrollGestureRestLevel { get; init; }

	public double ToggleGestureHold { get; init; } = 0.35;

	public bool EyebrowDoubleClick { get; init; } = true;

	public bool EyebrowLearned { get; init; }

	public double EyebrowThreshold { get; init; } = 0.35;

	public double EyebrowRestLevel { get; init; }

	public double EyebrowHold { get; init; } = 0.3;

	public bool PersonalizeMovement { get; init; } = true;

	public bool SubtleSmile { get; init; } = true;

	public double SmileThreshold { get; init; } = 0.48;

	public bool SmileLearned { get; init; }

	public bool EyesLearned { get; init; }

	public double SmileRestLevel { get; init; }

	public double EyeRestLevel { get; init; }

	public double MouthRestLevel { get; init; }

	public double MouthThreshold { get; init; } = 0.5;

	public double BlinkThreshold { get; init; } = 0.45;

	public string ClickMethod { get; init; } = "dwell";

	public string RightClickMethod { get; init; } = "either";

	public double RightClickHold { get; init; } = 0.3;

	public double RightClickCooldown { get; init; } = 0.45;

	public bool BalanceHorizontal { get; init; }

	public bool BalanceVertical { get; init; } = true;

	// Migrate existing profiles once; subsequent explicit UI choices remain valid.
	public int VerticalBalanceVersion { get; init; }

	public double DwellSeconds { get; init; } = 1.2;

	public double DwellRadius { get; init; } = 24.0;

	public double DwellMaximumRadius { get; init; } = 96.0;

	public double PrecisionNoiseMultiplier { get; init; } = 0.75;

	public double PrecisionMaximumBand { get; init; } = 0.3;

	public double DwellNoiseProjection { get; init; } = 0.25;

	public double DwellNoiseMultiplier { get; init; } = 3.0;

	public double DwellLockSeconds { get; init; } = 0.22;

	public double EyeReleaseRatio { get; init; } = 0.7;

	public double ClickCooldown { get; init; } = 0.8;

	public double GestureAnchorSeconds { get; init; } = 0.08;

	public double GestureOnsetRatio { get; init; } = 0.4;

	public double GestureSettleSeconds { get; init; } = 0.12;

	public double ClickLearningRestSeconds { get; init; } = 1.5;

	public double ClickLearningHoldSeconds { get; init; } = 0.4;

	public double ClickLearningSeparation { get; init; } = 0.08;

	public double ScrollRepeatSeconds { get; init; } = 0.25;

	public int ScrollNotches { get; init; } = 1;

	public double PositionMinimumScale { get; init; } = 0.055;

	public double PositionMaximumScale { get; init; } = 0.34;

	public double PositionMinimumHeight { get; init; } = 0.17;

	public double PositionMaximumHeight { get; init; } = 0.88;

	public double PositionSettleSeconds { get; init; } = 0.8;

	public double PositionScaleTolerance { get; init; } = 0.12;

	public double PositionCentreTolerance { get; init; } = 0.045;

	public double PositionAngleTolerance { get; init; } = 0.1;

	public double PositionGuidePadding { get; init; } = 1.18;

	public bool InvertX { get; init; }

	public bool InvertY { get; init; }

	public string Mapping { get; init; } = "relative";

	public string MovementAlgorithm { get; init; } = "adaptive";

	public Settings Validate()
	{
		Settings settings = this with
		{
			BalanceVertical = VerticalBalanceVersion < 1 || BalanceVertical,
			VerticalBalanceVersion = 1,
			TrackingFps = ((TrackingFps == 60) ? 60 : 30),
			PuckerThreshold = Clamp(PuckerThreshold, 0.05, 0.95, 0.35),
			PuckerRestLevel = Clamp(PuckerRestLevel, 0.0, 0.9, 0.0),
			ScrollGestureThreshold = Clamp(ScrollGestureThreshold, 0.05, 0.95, 0.35),
			ScrollGestureRestLevel = Clamp(ScrollGestureRestLevel, 0.0, 0.9, 0.0),
			ToggleGestureHold = Clamp(ToggleGestureHold, 0.2, 0.8, 0.35),
			MouthScroll = (MouthScroll && ClickMethod != "mouth"),
			EyebrowThreshold = Clamp(EyebrowThreshold, 0.05, 0.95, 0.35),
			EyebrowRestLevel = Clamp(EyebrowRestLevel, 0.0, 0.9, 0.0),
			EyebrowHold = Clamp(EyebrowHold, 0.2, 0.8, 0.3),
			Sensitivity = Clamp(Sensitivity, 0.3, 2.5, 1.0),
			Responsiveness = Clamp(Responsiveness, 0.05, 1.0, 0.38),
			RelativeSpeedLimit = Clamp(RelativeSpeedLimit, 0.0, 3000.0, 0.0),
			RelativeTravelBoost = Clamp(RelativeTravelBoost, 1.0, 4.0, 1.0),
			SteadyHold = Clamp(SteadyHold, 0.0, 1.0, 0.75),
			CentreAnchoring = Clamp(CentreAnchoring, 0.0, 1.0, 0),
			Deadzone = Clamp(Deadzone, 0.01, 0.12, 0.035),
			SmileThreshold = Clamp(SmileThreshold, (SubtleSmile && SmileLearned) ? 0.02 : 0.1, 0.9, 0.48),
			MouthThreshold = Clamp(MouthThreshold, 0.2, 0.9, 0.5),
			BlinkThreshold = Clamp(BlinkThreshold, 0.2, 0.95, 0.45),
			SmileRestLevel = Clamp(SmileRestLevel, 0.0, 0.9, 0.0),
			EyeRestLevel = Clamp(EyeRestLevel, 0.0, 0.9, 0.0),
			MouthRestLevel = Clamp(MouthRestLevel, 0.0, 0.9, 0.0),
			DwellSeconds = Clamp(DwellSeconds, 0.6, 3.0, 1.2),
			DwellRadius = Clamp(DwellRadius, 12.0, 48.0, 24.0),
			DwellMaximumRadius = Clamp(DwellMaximumRadius, 24.0, 128.0, 96.0),
			DwellNoiseMultiplier = Clamp(DwellNoiseMultiplier, 1.0, 5.0, 3.0),
			DwellLockSeconds = Clamp(DwellLockSeconds, 0.1, 0.5, 0.22),
			PrecisionNoiseMultiplier = Clamp(PrecisionNoiseMultiplier, 0.1, 2.0, 0.75),
			PrecisionMaximumBand = Clamp(PrecisionMaximumBand, 0.05, 0.5, 0.3),
			DwellNoiseProjection = Clamp(DwellNoiseProjection, 0.1, 1.0, 0.25),
			EyeReleaseRatio = Clamp(EyeReleaseRatio, 0.4, 0.9, 0.7),
			ClickCooldown = Clamp(ClickCooldown, 0.4, 2.0, 0.8),
			GestureAnchorSeconds = Clamp(GestureAnchorSeconds, 0.0, 0.2, 0.08),
			GestureOnsetRatio = Clamp(GestureOnsetRatio, 0.2, 0.8, 0.4),
			GestureSettleSeconds = Clamp(GestureSettleSeconds, 0.05, 0.4, 0.12),
			ClickLearningRestSeconds = Clamp(ClickLearningRestSeconds, 1.0, 5.0, 1.5),
			ClickLearningHoldSeconds = Clamp(ClickLearningHoldSeconds, 0.3, 1.0, 0.4),
			ClickLearningSeparation = Clamp(ClickLearningSeparation, 0.05, 0.3, 0.08),
			ScrollRepeatSeconds = Clamp(ScrollRepeatSeconds, 0.12, 1.0, 0.25),
			ScrollNotches = Math.Clamp(ScrollNotches, 1, 5),
			PositionMinimumScale = Clamp(PositionMinimumScale, 0.045, 0.1, 0.055),
			PositionMaximumScale = Clamp(PositionMaximumScale, 0.25, 0.5, 0.34),
			PositionMinimumHeight = Clamp(PositionMinimumHeight, 0.12, 0.3, 0.17),
			PositionMaximumHeight = Clamp(PositionMaximumHeight, 0.7, 0.94, 0.88),
			PositionSettleSeconds = Clamp(PositionSettleSeconds, 0.4, 3.0, 0.8),
			PositionScaleTolerance = Clamp(PositionScaleTolerance, 0.05, 0.25, 0.12),
			PositionCentreTolerance = Clamp(PositionCentreTolerance, 0.02, 0.1, 0.045),
			PositionAngleTolerance = Clamp(PositionAngleTolerance, 0.04, 0.2, 0.1),
			PositionGuidePadding = Clamp(PositionGuidePadding, 1.05, 1.4, 1.18),
			ClickMethod = (new ReadOnlySpan<string>(new string[4] { "dwell", "smile", "mouth", "none" }).Contains(ClickMethod) ? ClickMethod : "dwell"),
			RightClickMethod = (new ReadOnlySpan<string>(new string[5] { "either", "left", "right", "both", "none" }).Contains(RightClickMethod) ? RightClickMethod : "either"),
			RightClickHold = Clamp(RightClickHold, 0.25, 1.5, 0.3),
			RightClickCooldown = Clamp(RightClickCooldown, 0.25, 2.0, 0.45)
		};
		Settings settings2 = settings;
		string mapping = Mapping;
		bool flag = ((mapping == "rate" || mapping == "absolute") ? true : false);
		return settings with { Mapping = flag ? Mapping : "relative", MovementAlgorithm = MovementAlgorithm == "classic" ? "classic" : "adaptive" };
		static double Clamp(double n, double low, double high, double fallback)
		{
			if (!double.IsFinite(n))
			{
				return fallback;
			}
			return Math.Clamp(n, low, high);
		}
	}
}
