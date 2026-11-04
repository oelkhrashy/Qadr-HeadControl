# Pointer stabilization — analog-stable-2

**Historical radial-band design.** Revision **analog-wave-3** keeps the temporal smoothing described below and retains its radial envelope while adding independent bounded coordinate suppression. See [CROSS-AXIS-NOISE.md](CROSS-AXIS-NOISE.md) for the active final-stage equations and limitations.

`PointerStability` follows the existing relative/adaptive precision/vector blend. It smooths the two-dimensional displacement together and adds a bounded radial resting band. It uses capture time and the current profile noise, confidence and existing steadiness/precision settings. The supplied UI and alternative mappings are retained.

## Equations and behavior

```text
strength = clamp(SteadyHold, 0, 1)
intent = clamp(motionConfidence, 0, 1)
tau = (0.045 - 0.025*intent) * strength / 0.75
alpha = 1 - exp(-captureInterval / tau)
target += relativeDelta
filtered += alpha * (target - filtered)

band = min(3, 0.2*projectedProfileNoisePixels) * strength
band = min(6, band * clamp(precision, 0.1, 2) / 0.75)
band *= 1 - 0.97*intent

d = filtered - emitted
output = d * max(0, 1 - band/length(d))
emitted += output
```

At the existing default settings, the added time constant ranges from 20ms during strong deliberate changes to 45ms near rest. This is a filter parameter, not measured end-to-end latency. Faster response on clear motion avoids the larger loss of short corrections observed with the rejected fixed-45ms candidate.

The additional band is at most 2.25 pixels at default steadiness/precision, reducing to 0.0675 pixels at full confidence. Its maximum under the supported tuning range is six pixels. The same coefficient and radial multiplier act on both coordinates, preserving freedom to move diagonally. It does not infer an intended straight line or constrain the cursor to an axis.

The band follows the last emitted position rather than the learned head neutral, so it applies at any comfortable head angle. Small disturbances inside it produce no additional displacement. Very small intended movement below the band can also be suppressed. This tradeoff is explicit; intentional motion and involuntary motion cannot be perfectly distinguished from webcam pose alone.

Confirmed per-axis stops discard that axis's pending filter motion. Both-axis stops, a reset or a capture gap discard all pending motion. No prediction continues during tracking loss. If the emitted vector is inside its band and confidence is low, native interpolation is also marked stopped. Set the existing SteadyHold setting to zero to bypass this added stage; the earlier vector controller still applies.

## Verification

`StabilityBenchmark` adds noisy rest at neutral and away from neutral, noisy diagonals, direction reversal and capture-sampled step response at 10/30/60 FPS. The original movement, fine correction, vertical symmetry, gesture and recovery checks still run. Scale-normalized translation symmetry uses a 0.00001 pixel numerical tolerance; the added stateful filter exposed a roughly 0.00000055 pixel rounding difference in the old stricter check.

An optional matched comparison uses the preceding delivered Core:

```powershell
dotnet Source/HeadControl.Tests/bin/Release/net10.0/HeadControl.Tests.dll --stability-benchmark Verification/Before-Stability/Qadr.Core.dll Verification/stability-noise-benchmark.json
```

It compares 24 noisy/rest/turn scenarios, three step probes and 300 mixed samples with the extra stage bypassed. During constant intended travel, `TravelVelocityJitterPxPerSecond` is RMS velocity variation around the mean output velocity. It describes controller roughness, not physical cursor velocity after native interpolation. Corner/reversal velocity changes are intentional, so their roughness values are not scored as jitter reduction.

Cross-track RMS, endpoint error, retained travel, hold RMS/drift and step response must be reviewed alongside roughness. Filtering can make a path calmer while reducing its endpoint reach. The step response is sampled at camera intervals and measured against each controller's own final travel; it is not a measurement of camera-to-cursor latency. The separate tests require useful step travel too.

Historical-pose replay remains an open-loop comparison: the user cannot adapt or correct the new path. Some trial drift or cross-track results can worsen even when aggregate noise decreases. Current live validation is required before claiming increased usability or a percentage improvement in accuracy.
