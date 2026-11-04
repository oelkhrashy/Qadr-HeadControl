# Cross-axis noise suppression — analog-wave-3

This revision changes the final noise-band stage in `PointerStability`, after the existing relative precision/vector blend. Earlier travel/rest smoothing is described in [POINTER-STABILITY.md](POINTER-STABILITY.md), and the preceding blend in [ANALOG-MOVEMENT.md](ANALOG-MOVEMENT.md).

The common capture-timed smoothing is retained:

```text
intent = clamp(max(axisConfidenceX, axisConfidenceY), 0, 1)
tau = (0.045 - 0.025*intent) * clamp(SteadyHold, 0, 1) / 0.75
alpha = 1 - exp(-dt/tau)
target += relativeDelta
filtered += alpha*(target-filtered)
```

The previous radial band used maximum confidence and projected noise. The new final stage retains that radial envelope and additionally applies independent bounded hysteresis for each coordinate:

```text
band_i = min(3, max(0, normalizedNoise_i * screenGain_i) * 0.2)
band_i *= clamp(SteadyHold, 0, 1) * clamp(precision, 0.1, 2) / 0.75
band_i = min(6, band_i) * (1 - 0.97*clamp(axisConfidence_i, 0, 1))
delta_i = filtered_i - emitted_i
coordinateMagnitude_i = max(0, abs(delta_i) - band_i)
radialBand = Band(max(projectedNoiseX, projectedNoiseY), max(confidenceX, confidenceY))
radialPortion = max(0, 1 - radialBand/length(delta))
output_i = sign(delta_i) * min(coordinateMagnitude_i, abs(delta_i)*radialPortion)
emitted_i += output_i
```

Only the added band is independent; both coordinates retain the same temporal smoothing coefficient. `AnalogHeadMotion` supplies each axis's confidence and projected noise without changing its shared precision/vector blend. A confident X movement therefore cannot shrink Y's noise band. No direction mode is chosen; both components remain available, and sustained Y displacement can cross its bounded band during an X stroke. The response is nonlinear, so a tiny intended diagonal component can be delayed/suppressed and its slope can change near the bands. Tests establish bounded behavior on the supplied cases, not exact geometric preservation for every input.

Default maximum band is 2.25 pixels **per coordinate**; the prior 2.25px radial band was a vector radius. Each current band can reach six pixels under the supported tuning range. Existing zero steadiness bypass, confirmed-stop cancellation, capture-gap seeding and gesture/recovery reset paths remain in place.

## Tests

`WaveMovementTests.Tests` adds 18 small pitch-oscillation horizontal cases at 10/30/60 FPS, in both directions and three frequencies. Their synthetic pitch amplitude is 0.00025 radians plus a one-sixth secondary oscillation. Acceptance requires <1px cross-track RMS, <2px peak deviation and useful horizontal reach. Six additional cases cover very shallow diagonals and vertical corrections during sideways travel. All existing checks still run.

The larger-wave comparison uses 0.0015 radians plus a one-sixth secondary oscillation. It saves every result before checking that RMS does not worsen and horizontal reach differs by no more than one pixel against the supplied baseline. Absolute path metrics use the actual cursor position at 0.5s, when intended horizontal travel begins. The prior rest period is not part of the stroke geometry. Larger-wave residuals are measured outcomes; they are not required to be zero or labelled perfectly straight.

```powershell
dotnet Source/HeadControl.Tests/bin/Release/net10.0/HeadControl.Tests.dll --wave-benchmark Verification/Before-Wave-Smoothing/Qadr.Core.dll Verification/wave-benchmark.json
dotnet Source/HeadControl.Tests/bin/Release/net10.0/HeadControl.Tests.dll --stability-benchmark Verification/Before-Wave-Smoothing/Qadr.Core.dll Verification/wave-noise-benchmark.json
```

The second command also compares rest/diagonals/turns, three step probes and 300 samples with the stabilization stage bypassed. These offline measurements cannot label the intention of a real pitch change, model user corrections or prove physical-cursor smoothness. Fresh current-build guided recordings are necessary for live validation.
