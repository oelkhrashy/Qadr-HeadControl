using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Qadr.Core;

public sealed class MotionController
{
	private AnalogHeadMotion analogMotion = new();

	private readonly StillnessPark park = new();

	public bool Parked => park.Parked;
	private ExpressionRest? expressionRest;

	private Settings? effectiveSource;

	private Settings? effectiveSettings;
	private bool effectiveLab;

	public const double AbsoluteReach = 1.15;

	private OneEuro[] filters = new OneEuro[2]
	{
		new OneEuro(),
		new OneEuro()
	};

	private AdaptiveAxisFilter[] adaptiveFilters = new AdaptiveAxisFilter[2]
	{
		new AdaptiveAxisFilter(),
		new AdaptiveAxisFilter()
	};

	private Gesture smile = new Gesture(0.28);

	private Gesture mouth = new Gesture();

	private EyeClick eyes = new EyeClick();

	private EyebrowClick eyebrows = new EyebrowClick();

	private ReleaseGesture dragGesture = new ReleaseGesture();

	private ReleaseGesture scrollGesture = new ReleaseGesture();

	private double? lastTime;

	private double? distanceSince;

	private double? dwellSince;

	private double trackingScale;

	private double distanceScale;

	private double dwellX;

	private double dwellY;

	private double gestureX;

	private double gestureY;

	private double clicksUntil;

	private Observation? distanceRest;

	private bool dwellFired;

	// After Qadr itself moves the pointer (startup or recovery centring), the
	// pointer is somewhere the user did not choose. Dwell stays disarmed until
	// the user has moved it at least two dwell radii.
	private bool dwellNeedsMove;

	private (double X, double Y)? dwellMoveOrigin;

	public void DisarmDwellUntilMoved()
	{
		dwellNeedsMove = true;
		dwellMoveOrigin = null;
		dwellSince = null;
		dwellFired = false;
	}

	private bool wasGesture;

	private bool expressionHeld;

	private double expressionReleased = double.NegativeInfinity;

	private double eyeReleased = double.NegativeInfinity;

	private double eyeSettleSeconds;

	private double[] lastMapped = new double[2];

	private double[] heldSignal = new double[2];

	private double[] dwellMapped = new double[2];

	private readonly Queue<(double Time, double X, double Y)> history = new Queue<(double, double, double)>();

	private readonly RelativeTravelIntent travelIntent = new RelativeTravelIntent();

	private readonly SteadyHold steadyHold = new SteadyHold();

	private readonly RelativeRest[] relativeRest = new RelativeRest[2]
	{
		new RelativeRest(),
		new RelativeRest()
	};

	private readonly bool[] relativeHeld = new bool[2];

	public Settings Settings { get; set; } = new Settings();

	public Profile? Profile { get; private set; }

	public double[] Normalized { get; private set; } = new double[2];

	public RestNoiseModel PersonalModel { get; set; } = new RestNoiseModel(1, 0L);

	public Settings EffectiveSettings
	{
		get
		{
			bool lab=MovementLabTrace.MovementOnly;
			if(lab!=effectiveLab){effectiveLab=lab;effectiveSettings=null;Reset();}
			if ((object)effectiveSource != Settings || (object)effectiveSettings == null)
			{
				effectiveSource = Settings;
				effectiveSettings = expressionRest?.Apply(Settings) ?? Settings;
				// Pause click-induced cursor locks only during an explicit movement
				// trial, otherwise a dwell-locked pointer would score false steadiness.
				if(lab)effectiveSettings=effectiveSettings with{ClickMethod="none",RightClickMethod="none",EyebrowDoubleClick=false,PuckerDrag=false,MouthScroll=false};
			}
			return effectiveSettings;
		}
	}

	public double MotionConfidence => Settings.Mapping == "relative" && Settings.MovementAlgorithm == "adaptive"
		? analogMotion.MotionConfidence
		: Math.Max(adaptiveFilters[0].MotionConfidence, adaptiveFilters[1].MotionConfidence);

	public (double X, double Y, double Width, double Height)? ControlArea { get; set; }

	public double Width { get; set; } = 1920.0;

	public double Height { get; set; } = 1080.0;

	public double RightProgress => eyes.Progress;

	public double LeftProgress { get; private set; }

	public bool HoldingClick
	{
		get
		{
			if (!(Settings.ClickMethod == "dwell"))
			{
				if (!(Settings.ClickMethod == "smile"))
				{
					if (Settings.ClickMethod == "mouth")
					{
						return mouth.Active;
					}
					return false;
				}
				return smile.Active;
			}
			return dwellFired;
		}
	}

	public void SetExpressionRest(IEnumerable<Observation> observations)
	{
		expressionRest = ExpressionRest.From(observations);
		effectiveSettings = null;
	}

	public void SetProfile(Profile p)
	{
		p = p.Upgrade();
		if (!p.IsValid)
		{
			throw new InvalidDataException("Invalid movement profile");
		}
		Profile = p.WithBalancedDirections();
		park.Configure(Profile);
		trackingScale = p.Scale;
		expressionRest = null;
		effectiveSettings = null;
		distanceSince = null;
		Reset();
	}

	public void Rebase(Observation o)
	{
		Profile profile = Profile;
		if ((object)profile != null)
		{
			Profile = profile with
			{
				Center = new double[2] { o.X, o.Y },
				Neutral = new double[2] { o.Yaw, o.Pitch },
				Scale = o.Scale
			};
			trackingScale = o.Scale;
			distanceSince = null;
			distanceRest = null;
			Reset();
		}
	}

	private void ResetFilters()
	{
		analogMotion = new AnalogHeadMotion();
		park.Reset();
		filters = new OneEuro[2]
		{
			new OneEuro(),
			new OneEuro()
		};
		adaptiveFilters = new AdaptiveAxisFilter[2]
		{
			new AdaptiveAxisFilter(),
			new AdaptiveAxisFilter()
		};
		travelIntent.Reset();
		steadyHold.Reset();
		RelativeRest[] array = relativeRest;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Reset();
		}
		Array.Clear(relativeHeld);
	}

	public void Reset()
	{
		ResetFilters();
		smile = new Gesture(0.28);
		mouth = new Gesture();
		eyes = new EyeClick();
		eyebrows = new EyebrowClick();
		dragGesture = new ReleaseGesture();
		scrollGesture = new ReleaseGesture();
		lastTime = (dwellSince = null);
		dwellFired = (wasGesture = false);
		lastMapped = new double[2];
		heldSignal = new double[2];
		LeftProgress = 0.0;
		Normalized = new double[2];
		history.Clear();
		clicksUntil = 0.0;
		expressionHeld = false;
		expressionReleased = (eyeReleased = double.NegativeInfinity);
	}

	public bool AdaptDistance(Observation o)
	{
		Profile profile = Profile;
		if ((object)profile == null)
		{
			return false;
		}
		double num = o.Scale / ((trackingScale > 0.0) ? trackingScale : profile.Scale);
		if (num > 0.65 && num < 1.5)
		{
			trackingScale += (o.Scale - trackingScale) * 0.03;
			distanceSince = null;
			distanceRest = null;
			return false;
		}
		if ((num <= 0.65 || num >= 1.5) && (num < 0.65 || num > 1.5 || (Math.Abs(o.Yaw - profile.Neutral[0]) < 0.14 && Math.Abs(o.Pitch - profile.Neutral[1]) < 0.14)))
		{
			if (!distanceSince.HasValue || (object)distanceRest == null || Math.Abs(o.Scale - distanceScale) / o.Scale > 0.05 || Math.Abs(o.Yaw - distanceRest.Yaw) > Math.Max(0.03, profile.Noise[0] * 5.0) || Math.Abs(o.Pitch - distanceRest.Pitch) > Math.Max(0.03, profile.Noise[1] * 5.0) || Math.Abs(o.X - distanceRest.X) > 0.02 || Math.Abs(o.Y - distanceRest.Y) > 0.02)
			{
				distanceSince = o.Timestamp;
				distanceScale = o.Scale;
				distanceRest = o;
			}
			else if (o.Timestamp - distanceSince >= 0.85)
			{
				Profile = profile with
				{
					Center = new double[2] { o.X, o.Y },
					Neutral = new double[2] { o.Yaw, o.Pitch },
					Scale = o.Scale
				};
				trackingScale = o.Scale;
				distanceSince = null;
				distanceRest = null;
			}
			Reset();
			return true;
		}
		distanceSince = null;
		return false;
	}

	public PointerOutput Feed(Observation o, double x, double y, bool suppressLeft = false)
	{
		var output=FeedCore(o,x,y,suppressLeft);
		MovementLabTrace.Record(o,output,Profile,EffectiveSettings,PersonalModel,Width,Height);
		return output;
	}

	private PointerOutput FeedCore(Observation o, double x, double y, bool suppressLeft)
	{
		if (!o.IsUsable)
		{
			Reset();
			return new PointerOutput(0.0, 0.0, null, x, y, 0.0, DistanceChanged: false, Frozen: true);
		}
		if (lastTime.HasValue && o.Timestamp <= lastTime.Value)
		{
			return new PointerOutput(0.0, 0.0, null, x, y, LeftProgress, DistanceChanged: false, Frozen: true);
		}
		if (lastTime.HasValue && o.Timestamp - lastTime.Value > 0.2)
		{
			Reset();
		}
		Profile profile = Profile;
		if ((object)profile == null)
		{
			return new PointerOutput(0.0, 0.0, null, x, y, 0.0);
		}
		if (AdaptDistance(o))
		{
			return new PointerOutput(0.0, 0.0, null, x, y, 0.0, DistanceChanged: true, Frozen: true);
		}
		double num = (lastTime.HasValue ? (o.Timestamp - lastTime.Value) : 0.0);
		lastTime = o.Timestamp;
		Settings settings = EffectiveSettings;
		history.Enqueue((o.Timestamp, x, y));
		while (history.Count > 1 && o.Timestamp - history.Peek().Time > Math.Max(0.3, settings.GestureAnchorSeconds + 0.1))
		{
			history.Dequeue();
		}
		bool active = eyes.Active;
		bool qualified = eyes.Qualified;
		// Right-click by eye only once the user has taught Qadr their wink.
		// The default RightClickMethod is "either", and without this gate a
		// slow blink right-clicked before any lesson had been done.
		bool flag = settings.EyesLearned && eyes.Feed((o.LeftEye < 0.0) ? o.Blink : o.LeftEye, (o.RightEye < 0.0) ? o.Blink : o.RightEye, settings, o.Timestamp);
		if (active && !eyes.Active)
		{
			eyeReleased = o.Timestamp;
			eyeSettleSeconds = ((flag | qualified) ? settings.GestureSettleSeconds : 0.0);
		}
		bool flag2 = settings.PuckerDrag && settings.PuckerLearned && o.Pucker >= settings.PuckerThreshold;
		bool flag3 = settings.MouthScroll && settings.ScrollGestureLearned && settings.ClickMethod != "mouth" && o.Mouth >= settings.ScrollGestureThreshold;
		bool flag4 = settings.EyebrowDoubleClick && settings.EyebrowLearned && o.Eyebrow >= settings.EyebrowThreshold;
		bool flag5 = (settings.ClickMethod == "smile" && o.Smile >= settings.SmileThreshold) || (settings.ClickMethod == "mouth" && o.Mouth >= settings.MouthThreshold);
		bool num2 = dragGesture.Active || scrollGesture.Active;
		bool flag6 = dragGesture.Feed(o.Pucker, settings.PuckerThreshold, settings.PuckerRestLevel, settings.ToggleGestureHold, o.Timestamp, settings.PuckerDrag && settings.PuckerLearned, eyes.Active | flag | flag3 | flag4 | flag5);
		bool flag7 = scrollGesture.Feed(o.Mouth, settings.ScrollGestureThreshold, settings.ScrollGestureRestLevel, settings.ToggleGestureHold, o.Timestamp, settings.MouthScroll && settings.ScrollGestureLearned && settings.ClickMethod != "mouth", eyes.Active | flag | flag2 | flag4 | flag5);
		if (num2 && !dragGesture.Active && !scrollGesture.Active)
		{
			eyeReleased = o.Timestamp;
			eyeSettleSeconds = settings.GestureSettleSeconds;
		}
		bool flag8 = dragGesture.Active || scrollGesture.Active;
		if (flag8)
		{
			smile = new Gesture(0.28);
			mouth = new Gesture();
		}
		bool active2 = eyebrows.Active;
		bool flag9 = settings.EyebrowDoubleClick && settings.EyebrowLearned && o.Eyebrow >= settings.EyebrowThreshold;
		bool conflict = ((flag8 || eyes.Active) | flag) || (settings.ClickMethod == "smile" && o.Smile >= settings.SmileThreshold) || (settings.ClickMethod == "mouth" && o.Mouth >= settings.MouthThreshold);
		bool flag10 = eyebrows.Feed(o.Eyebrow, settings, o.Timestamp, conflict);
		flag9 = eyebrows.Active;
		if (active2 && !eyebrows.Active)
		{
			eyeReleased = o.Timestamp;
			eyeSettleSeconds = settings.GestureSettleSeconds;
		}
		if (flag9)
		{
			smile = new Gesture(0.28);
			mouth = new Gesture();
		}
		// Competing right-click intent cancels a partly completed left gesture.
		if (eyes.Active || flag || suppressLeft)
		{
			smile = new Gesture(0.28);
			mouth = new Gesture();
		}
		bool flag11 = !suppressLeft && !flag8 && !flag9 && !eyes.Active && !flag;
		string clickMethod;
		if (flag11)
		{
			clickMethod = settings.ClickMethod;
			bool flag12 = ((clickMethod == "smile") ? smile.Feed(o.Smile, settings.SmileThreshold, o.Timestamp, settings.SmileRestLevel, settings.EyeReleaseRatio, settings.ClickCooldown) : (clickMethod == "mouth" && mouth.Feed(o.Mouth, settings.MouthThreshold, o.Timestamp, settings.MouthRestLevel, settings.EyeReleaseRatio, settings.ClickCooldown)));
			flag11 = flag12;
		}
		bool flag13 = flag11;
		clickMethod = settings.ClickMethod;
		flag11 = ((clickMethod == "smile") ? smile.Active : (clickMethod == "mouth" && mouth.Active));
		bool flag14 = flag11;
		flag11 = settings.Mapping != "rate";
		if (flag11)
		{
			clickMethod = settings.ClickMethod;
			bool flag12 = ((clickMethod == "dwell" || clickMethod == "smile") ? true : false);
			flag11 = flag12;
		}
		bool flag15 = flag11 && settings.SmileLearned;
		bool flag16 = flag15 && o.Smile >= settings.SmileRestLevel + (settings.SmileThreshold - settings.SmileRestLevel) * 0.7;
		flag14 |= flag16;
		clickMethod = settings.ClickMethod;
		flag11 = ((clickMethod == "smile" || clickMethod == "mouth") ? true : false);
		bool num3 = flag11 | flag15;
		if (num3 & flag14)
		{
			expressionHeld = true;
			expressionReleased = double.NegativeInfinity;
		}
		else if (expressionHeld && !flag14)
		{
			expressionHeld = false;
			expressionReleased = o.Timestamp;
		}
		bool flag17 = num3 && !expressionHeld && o.Timestamp - expressionReleased < settings.GestureSettleSeconds;
		bool flag18 = o.Timestamp - eyeReleased < eyeSettleSeconds;
		bool flag19 = (((((((dragGesture.Active || scrollGesture.Active) | flag8 | flag6 | flag7) || eyebrows.Active) | flag9 | flag10) || eyes.Active) | flag14) || expressionHeld) | flag17 | flag18;
		if (flag19 && !wasGesture)
		{
			(double, double, double) tuple = history.Peek();
			double num4 = o.Timestamp - settings.GestureAnchorSeconds;
			foreach (var item in history)
			{
				if (item.Time > num4)
				{
					break;
				}
				tuple = item;
			}
			gestureX = tuple.Item2;
			gestureY = tuple.Item3;
		}
		wasGesture = flag19;
		if (!flag19 && (double.IsFinite(expressionReleased) || double.IsFinite(eyeReleased)))
		{
			ResetFilters();
			lastMapped = new double[2];
			heldSignal = new double[2];
			num = 0.0;
			expressionReleased = (eyeReleased = double.NegativeInfinity);
		}
		Span<double> span = stackalloc double[2];
		Span<double> span2 = stackalloc double[2];
		Span<bool> span3 = stackalloc bool[2];
		Span<double> span4 = stackalloc double[2];
		Span<double> analogSignal = stackalloc double[2];
		Span<double> analogNoise = stackalloc double[2];
		bool analog = settings.Mapping == "relative" && settings.MovementAlgorithm == "adaptive";
		for (int i = 0; i < 2; i++)
		{
			double num5 = profile.Signal(o, i);
			double num6 = profile.Ranges[i][(!(num5 < 0.0)) ? 1u : 0u];
			if ((i == 0) ? settings.BalanceHorizontal : settings.BalanceVertical)
			{
				// Vertical balance uses the downward range for both signs, retaining
				// the existing down response while making up equally sensitive.
				// Horizontal balancing retains its previous geometric-mean mapping.
				num6 = i == 1 ? profile.Ranges[1][1] : Math.Sqrt(profile.Ranges[0][0] * profile.Ranges[0][1]);
			}
			double num7 = ((settings.Mapping == "rate") ? 1.8 : ((double)((settings.Mapping == "absolute") ? 3 : ((settings.RelativeTravelBoost > 1.0) ? 32 : 8))));
			double num8 = Math.Clamp(num5 / num6, 0.0 - num7, num7) * (double)((!((i == 0) ? settings.InvertX : settings.InvertY)) ? 1 : (-1));
			double num9 = ((settings.PersonalizeMovement && PersonalModel.Valid) ? PersonalModel.Noise(i, profile.Noise[i]) : profile.Noise[i]);
			double num10 = Math.Clamp(num9 * 3.0 / num6 + settings.Deadzone, 0.065, 0.32);
			bool flag20 = settings.MovementAlgorithm == "adaptive" || settings.Mapping == "absolute";
			double noise = num9 / num6;
			if (analog)
			{
				analogSignal[i] = num8;
				analogNoise[i] = noise;
				continue;
			}
			double num11 = (flag20 ? adaptiveFilters[i].Feed(num8, num, noise, settings.Responsiveness, settings.DirectResponse, settings.Mapping != "rate") : filters[i].Feed(num8, (num == 0.0) ? 0.033 : num, settings.Responsiveness));
			Normalized[i] = num11;
			double filtered = num11;
			double num12 = (flag20 ? adaptiveFilters[i].AcceptedInput : num8);
			bool flag21 = settings.Mapping == "relative" && relativeRest[i].Feed(num12, filtered, noise, o.Timestamp, num);
			bool flag22 = flag21 && !relativeHeld[i];
			span3[i] = flag21 && (!flag22 || !relativeRest[i].CanFinish);
			relativeHeld[i] = flag21;
			if (flag21)
			{
				num11 = num12;
				if (flag20)
				{
					adaptiveFilters[i].Settle();
				}
				else
				{
					filters[i].Settle(num12);
				}
			}
			double num13 = Math.Clamp(num9 / num6 * settings.PrecisionNoiseMultiplier, 0.0003, settings.PrecisionMaximumBand);
			if (flag20)
			{
				num13 *= 1.0 - 0.85 * adaptiveFilters[i].MotionConfidence;
			}
			if (num == 0.0)
			{
				heldSignal[i] = num11;
			}
			else if (Math.Abs(num11 - heldSignal[i]) > num13)
			{
				heldSignal[i] = num11 - Math.CopySign(num13, num11 - heldSignal[i]);
			}
			num11 = heldSignal[i];
			double x2 = Math.Max(0.0, Math.Abs(num11) - num10) / (1.0 - num10);
			span2[i] = ((settings.Mapping == "relative" && i == 1) ? Math.CopySign((Math.Max(0.0, Math.Abs(num11) - num10) + 0.35 * Math.Min(Math.Abs(num11), num10)) / (1.0 - num10), num11) : Math.CopySign(x2, num11));
			if (settings.Mapping == "rate")
			{
				span[i] = ((Math.Abs(num12) <= num10) ? 0.0 : Math.CopySign(Math.Min(1.35, Math.Pow(x2, 1.65)) * ((i == 0) ? Width : Height) * 0.68 * settings.Sensitivity * num, num11));
			}
			else if (settings.Mapping == "absolute")
			{
				span4[i] = Math.Clamp(span2[i] * 1.15 * settings.Sensitivity, -1.0, 1.0);
			}
			else
			{
				span[i] = ((num > 0.0 && !span3[i]) ? ((span2[i] - lastMapped[i]) * ((i == 0) ? Width : Height) * 0.7 * settings.Sensitivity) : 0.0);
			}
			lastMapped[i] = span2[i];
		}
		if (analog && settings.ParkWhenStill)
		{
			(analogSignal[0], analogSignal[1]) = park.Feed(o.Timestamp, analogSignal[0], analogSignal[1]);
		}
		if (analog)
		{
			var displacement = analogMotion.Feed(analogSignal[0], analogSignal[1], num,
				o.Timestamp, analogNoise[0], analogNoise[1], Width * .7 * settings.Sensitivity,
				Height * .7 * settings.Sensitivity, settings.Responsiveness,
				settings.DirectResponse, settings.SteadyHold, settings.PrecisionNoiseMultiplier);
			span[0] = displacement.X; span[1] = displacement.Y;
			span2[0] = Normalized[0] = lastMapped[0] = analogMotion.PositionX;
			span2[1] = Normalized[1] = lastMapped[1] = analogMotion.PositionY;
			span3[0] = analogMotion.StoppedX; span3[1] = analogMotion.StoppedY;
		}
		if (settings.Mapping == "relative" && settings.RelativeTravelBoost > 1.0 && num > 0.0)
		{
			double num14 = analog ? analogMotion.MotionConfidence : 1.0;
			num14 *= travelIntent.Feed(span[0], span[1], num, Width, Height, settings.Sensitivity);
			(double, double) tuple3 = RelativeReach.Apply(span[0], span[1], num, Width, Height, settings.Sensitivity, settings.RelativeTravelBoost, num14);
			span[0] = tuple3.Item1;
			span[1] = tuple3.Item2;
		}
		if (settings.Mapping == "relative" && settings.RelativeSpeedLimit > 0.0 && num > 0.0)
		{
			double num15 = Math.Sqrt(span[0] * span[0] + span[1] * span[1]);
			double num16 = settings.RelativeSpeedLimit * num;
			if (num15 > num16)
			{
				span[0] *= num16 / num15;
				span[1] *= num16 / num15;
			}
		}
		if (settings.Mapping == "relative" && settings.MovementAlgorithm == "classic" && settings.CentreAnchoring > 0.0 && num > 0.0)
		{
			(double, double, double, double)? controlArea = ControlArea;
			if (controlArea.HasValue)
			{
				(double, double, double, double) valueOrDefault = controlArea.GetValueOrDefault();
				double num17 = 1.0 - Math.Clamp(settings.CentreAnchoring, 0.0, 1.0);
				for (int j = 0; j < 2; j++)
				{
					double num18;
					if (j == 0)
					{
						(num18, _, _, _) = valueOrDefault;
					}
					else
					{
						num18 = valueOrDefault.Item2;
					}
					double num19 = num18;
					double num20 = ((j == 0) ? valueOrDefault.Item3 : valueOrDefault.Item4);
					double num21 = ((j == 0) ? x : y);
					double num22 = Math.Clamp(num19 + (num20 - 1.0) / 2.0 + span2[j] * ((j == 0) ? Width : Height) * 0.7 * settings.Sensitivity, num19, num19 + num20 - 1.0) - num21;
					if (span[j] * num22 < 0.0)
					{
						span[j] *= num17;
					}
					else if (Math.Abs(span[j]) > Math.Abs(num22))
					{
						span[j] = num22 + (span[j] - num22) * num17;
					}
				}
			}
		}
		if ((flag19 | flag) || num == 0.0)
		{
			span.Clear();
		}
		string text;
		if (flag)
		{
			text = "right";
		}
		else if (flag6)
		{
			text = "drag";
		}
		else if (flag7)
		{
			text = "scroll-toggle";
		}
		else if (flag10)
		{
			text = "double";
		}
		else
		{
			text = ((flag13 && !eyes.Active) ? "left" : null);
		}
		bool flag23 = flag19 | flag;
		double num23 = 0.0;
		bool flag24 = settings.Mapping == "rate" && Math.Abs(span[0]) + Math.Abs(span[1]) > 5.0 * num;
		if (flag24)
		{
			dwellSince = null;
			dwellFired = false;
		}
		if ((((((dragGesture.Active || scrollGesture.Active) | flag8 | flag6 | flag7) || eyebrows.Active) | flag9 | flag10 | flag14) || eyes.Qualified) | flag)
		{
			dwellSince = null;
			dwellFired = false;
			clicksUntil = o.Timestamp + settings.ClickCooldown;
		}
		else if ((eyes.Active | flag18) && dwellSince.HasValue)
		{
			dwellSince += num;
		}
		if (!suppressLeft && !flag24 && settings.ClickMethod == "dwell" && !flag19 && !flag && o.Timestamp >= clicksUntil)
		{
			double num24 = Math.Max(Width * profile.Noise[0] / profile.Ranges[0].Min(), Height * profile.Noise[1] / profile.Ranges[1].Min()) * 0.7 * settings.Sensitivity * settings.DwellNoiseProjection;
			double num25 = Math.Max(settings.DwellRadius, Math.Min(settings.DwellMaximumRadius, num24 * settings.DwellNoiseMultiplier));
			if (dwellNeedsMove)
			{
				dwellMoveOrigin ??= (x, y);
				double mx = x - dwellMoveOrigin.Value.X, my = y - dwellMoveOrigin.Value.Y;
				if (mx * mx + my * my > 4.0 * num25 * num25)
				{
					dwellNeedsMove = false;
					dwellMoveOrigin = null;
				}
				else
				{
					dwellSince = null;
					dwellFired = false;
				}
			}
			bool flag25 = false;
			for (int k = 0; k < 2; k++)
			{
				flag25 |= Math.Abs(span2[k] - dwellMapped[k]) > Math.Max(num25 / (((k == 0) ? Width : Height) * 0.7 * settings.Sensitivity), settings.DwellNoiseMultiplier * profile.Noise[k] / Math.Min(profile.Ranges[k][0], profile.Ranges[k][1]));
			}
			double num26 = x - dwellX;
			double num27 = y - dwellY;
			if (dwellNeedsMove)
			{
			}
			else if ((!dwellSince.HasValue || num26 * num26 + num27 * num27 > num25 * num25) | flag25)
			{
				dwellX = x;
				dwellY = y;
				span2.CopyTo(dwellMapped);
				dwellSince = o.Timestamp;
				dwellFired = false;
			}
			else if (!dwellFired)
			{
				num23 = Math.Clamp((o.Timestamp - dwellSince.Value) / settings.DwellSeconds, 0.0, 1.0);
				if (num23 >= 1.0)
				{
					text = "left";
					dwellFired = true;
					gestureX = dwellX;
					gestureY = dwellY;
				}
			}
			if (!dwellNeedsMove && (dwellFired || o.Timestamp - dwellSince!.Value >= settings.DwellLockSeconds))
			{
				span.Clear();
				flag23 = true;
			}
		}
		clickMethod = settings.ClickMethod;
		double leftProgress;
		if (clickMethod == "smile")
		{
			leftProgress = smile.Progress;
		}
		else
		{
			leftProgress = ((!(clickMethod == "mouth")) ? num23 : mouth.Progress);
		}
		LeftProgress = leftProgress;
		double num28 = span[0];
		double num29 = span[1];
		string text2 = text;
		double num30;
		if (text == "left" && settings.ClickMethod == "dwell")
		{
			num30 = dwellX;
		}
		else
		{
			num30 = ((flag19 | flag) ? gestureX : x);
		}
		double dx = num28;
		double dy = num29;
		string click = text2;
		double clickX = num30;
		double clickY;
		if (text == "left" && settings.ClickMethod == "dwell")
		{
			clickY = dwellY;
		}
		else
		{
			clickY = ((flag19 | flag) ? gestureY : y);
		}
		return new PointerOutput(dx, dy, click, clickX, clickY, LeftProgress, DistanceChanged: false, flag23, settings.Mapping == "rate", (!flag23 && num > 0.0) ? (span[0] / num) : 0.0, (!flag23 && num > 0.0) ? (span[1] / num) : 0.0, span3[0], span3[1], settings.Mapping == "absolute", span4[0], span4[1]);
	}
}
