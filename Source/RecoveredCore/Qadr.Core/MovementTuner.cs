using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public static class MovementTuner
{
	private static bool Finite(double? n)
	{
		if (n.HasValue)
		{
			double valueOrDefault = n.GetValueOrDefault();
			if (double.IsFinite(valueOrDefault))
			{
				return valueOrDefault >= 0.0;
			}
		}
		return false;
	}

	private static bool Usable(AssessmentResult r)
	{
		if (r.Completed && r.ExpectedAction == "move" && r.TrackingLosses == 0 && r.Samples >= 10)
		{
			double durationMs = r.DurationMs;
			if (durationMs >= 600.0 && durationMs <= 8500.0 && double.IsFinite(r.PathLengthPx) && r.PathLengthPx >= 40.0 && double.IsFinite(r.PathEfficiency))
			{
				durationMs = r.PathEfficiency;
				if (durationMs >= 0.0 && durationMs <= 1.0 && r.Overshoots >= 0 && r.Entries >= 1 && Finite(r.FirstEntryMs) && r.FirstEntryMs <= r.DurationMs && Finite(r.FinalCentreErrorPx))
				{
					return Finite(r.InsideJitterRmsPx);
				}
			}
		}
		return false;
	}

	private static AssessmentResult[] Directional(IReadOnlyList<AssessmentResult> results)
	{
		return results.Skip(1).Where(Usable).ToArray();
	}

	public static bool WithinEnvelope(MovementParameters value, MovementParameters anchor)
	{
		if (value.Valid && anchor.Valid && value.Sensitivity >= Math.Max(0.3, anchor.Sensitivity * 0.7) - 1E-09 && value.Sensitivity <= Math.Min(2.5, anchor.Sensitivity * 1.3) + 1E-09 && value.TravelBoost >= Math.Max(1.0, anchor.TravelBoost * 0.7) - 1E-09 && value.TravelBoost <= Math.Min(4.0, anchor.TravelBoost * 1.3) + 1E-09 && value.PrecisionNoise >= Math.Max(0.1, anchor.PrecisionNoise * 0.7) - 1E-09)
		{
			return value.PrecisionNoise <= Math.Min(2.0, anchor.PrecisionNoise * 1.3) + 1E-09;
		}
		return false;
	}

	private static MovementParameters Bound(MovementParameters v, MovementParameters a)
	{
		return new MovementParameters(Math.Clamp(v.Sensitivity, Math.Max(0.3, a.Sensitivity * 0.7), Math.Min(2.5, a.Sensitivity * 1.3)), Math.Clamp(v.TravelBoost, Math.Max(1.0, a.TravelBoost * 0.7), Math.Min(4.0, a.TravelBoost * 1.3)), Math.Clamp(v.PrecisionNoise, Math.Max(0.1, a.PrecisionNoise * 0.7), Math.Min(2.0, a.PrecisionNoise * 1.3)));
	}

	public static MovementTuningStatus Propose(Settings settings, MovementParameters anchor, IReadOnlyList<AssessmentResult> baseline, bool synthetic = false)
	{
		MovementParameters before = MovementParameters.From(settings);
		if (synthetic)
		{
			return Keep("Diagnostic input cannot train a personal movement profile.");
		}
		if (!before.Valid || !anchor.Valid || baseline.Count != 5 || baseline.Where((AssessmentResult r, int i) => r.Id != ((char)(65 + i)).ToString() || r.ExpectedAction != "move").Any())
		{
			return Keep("Incomplete movement evidence; previous settings kept.");
		}
		if (baseline.Any((AssessmentResult r) => r.TrackingLosses != 0))
		{
			return Keep("Tracking was interrupted; previous settings kept.");
		}
		AssessmentResult[] array = Directional(baseline);
		if (array.Length < 3)
		{
			return Keep("Not enough reliably reached targets; previous settings kept.");
		}
		double num = array.Average((AssessmentResult r) => r.Overshoots);
		double num2 = array.Average((AssessmentResult r) => r.InsideJitterRmsPx.Value);
		double num3 = array.Average((AssessmentResult r) => r.PathEfficiency);
		string adjustment;
		string message;
		MovementParameters v;
		if (num >= 0.75 && num3 < 0.9)
		{
			v = before with
			{
				Sensitivity = before.Sensitivity * 0.9
			};
			adjustment = "precision";
			message = "Trying slightly slower movement to reduce overshoots. Repeat the same five targets.";
		}
		else if (num2 >= 8.0 && settings.MovementAlgorithm == "adaptive")
		{
			v = before with
			{
				PrecisionNoise = before.PrecisionNoise * 1.1
			};
			adjustment = "steadiness";
			message = "Trying a slightly stronger precision band. Repeat the same five targets.";
		}
		else
		{
			if (!(settings.Mapping == "relative") || !(num <= 0.25) || !(num3 >= 0.8) || !(array.Average((AssessmentResult r) => r.FirstEntryMs.Value) >= 2000.0))
			{
				return Keep("Your measured movement does not support a useful adjustment. Previous settings kept.");
			}
			v = before with
			{
				TravelBoost = before.TravelBoost * 1.1
			};
			adjustment = "reach";
			message = "Trying slightly easier travel while keeping slow corrections unchanged. Repeat the same five targets.";
		}
		v = Bound(v, anchor);
		if (v == before)
		{
			return Keep("Your personal adjustment limit was reached; previous settings kept.");
		}
		return new MovementTuningStatus("validating", message, before, v, null, adjustment);
		MovementTuningStatus Keep(string message2)
		{
			return new MovementTuningStatus("kept", message2, before, null, before);
		}
	}

	public static MovementTuningStatus Validate(MovementTuningStatus proposal, IReadOnlyList<AssessmentResult> baseline, IReadOnlyList<AssessmentResult> validation, bool sameGeometry)
	{
		(AssessmentResult Before, AssessmentResult After)[] pairs;
		if (!(proposal.Phase != "validating"))
		{
			MovementParameters proposed = proposal.Proposed;
			if ((object)proposed != null && proposed.Valid && baseline.Count == 5 && validation.Count == 5 && sameGeometry)
			{
				if (validation.Where((AssessmentResult r, int i) => r.Id != ((char)(70 + i)).ToString() || r.ExpectedAction != "move" || r.TrackingLosses != 0).Any() || Enumerable.Count(validation, (AssessmentResult r) => r.Completed) < Enumerable.Count(baseline, (AssessmentResult r) => r.Completed))
				{
					return Reject("Tracking or target completion became worse; previous settings restored.");
				}
				pairs = (from p in baseline.Skip(1).Select((AssessmentResult r, int i) => (Before: r, After: validation[i + 1]))
					where Usable(p.Before)
					select p).ToArray();
				if (pairs.Length < 3 || pairs.Any(((AssessmentResult Before, AssessmentResult After) p) => !Usable(p.After)))
				{
					return Reject("Too few comparable targets; previous settings restored.");
				}
				double num = Mean((AssessmentResult r) => r.DurationMs, after: false);
				double num2 = Mean((AssessmentResult r) => r.DurationMs, after: true);
				double num3 = Mean((AssessmentResult r) => r.Overshoots, after: false);
				double num4 = Mean((AssessmentResult r) => r.Overshoots, after: true);
				double num5 = Mean((AssessmentResult r) => r.InsideJitterRmsPx.Value, after: false);
				double num6 = Mean((AssessmentResult r) => r.InsideJitterRmsPx.Value, after: true);
				bool flag = num2 <= num * 1.15 + 100.0 && num6 <= num5 * 1.15 + 2.0 && num4 <= num3 && Mean((AssessmentResult r) => r.FinalCentreErrorPx.Value, after: true) <= Mean((AssessmentResult r) => r.FinalCentreErrorPx.Value, after: false) + 5.0 && Mean((AssessmentResult r) => r.PathEfficiency, after: true) >= Mean((AssessmentResult r) => r.PathEfficiency, after: false) - 0.08;
				if (!(flag & (proposal.Adjustment switch
				{
					"precision" => num4 <= num3 * 0.75 && num4 < num3, 
					"steadiness" => num6 <= num5 * 0.8, 
					"reach" => num2 <= num * 0.9, 
					_ => false, 
				})))
				{
					return Reject("The repeat check did not show a reliable improvement. Previous settings restored.");
				}
				return proposal with
				{
					Phase = "applied",
					Message = "The repeat check improved the measured result. Your movement settings were saved automatically.",
					Applied = proposal.Proposed
				};
			}
		}
		return Reject("The comparison was incomplete or changed; previous settings restored.");
		double Mean(Func<AssessmentResult, double> metric, bool after)
		{
			return pairs.Average(((AssessmentResult Before, AssessmentResult After) p) =>
			{
				Func<AssessmentResult, double> func = metric;
				AssessmentResult arg;
				if (!after)
				{
					(arg, _) = p;
				}
				else
				{
					arg = p.After;
				}
				return func(arg);
			});
		}
		MovementTuningStatus Reject(string message)
		{
			return proposal with
			{
				Phase = "reverted",
				Message = message,
				Applied = proposal.Before
			};
		}
	}
}
