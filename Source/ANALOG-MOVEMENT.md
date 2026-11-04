# Analog pointing — precision/vector branch introduced in analog-1

Revision **analog-stable-2** follows this branch with [PointerStability](POINTER-STABILITY.md). The equations below describe the preceding blend, which remains active. See that document for the additional travel smoothing, bounded hold band, reset behavior and tradeoffs.

This revision keeps the supplied Qadr Shell, Guardian, camera worker and click gestures. It combines Qadr's relative pointing and tracking recovery with a longer, repeated four-direction calibration, informed by the separate HeadMouse review. It does not import HeadMouse's Python runtime or default joystick mapping. No proprietary Smyle Mouse code is used.

## Movement equations

For each capture, subtract the learned neutral from yaw/pitch (or use face translation when that is the person's stronger signal), then divide by the profile range. Existing vertical balancing still uses a common comfortable range for both signs. Screen gain is `0.7 * screenExtent * sensitivity` independently for X and Y. These existing gains account for the display's aspect ratio.

`AnalogHeadMotion` feeds each normalized coordinate into the existing outlier/confidence estimator. For the vector branch it uses the greater of their adaptive cutoffs and one capture-timed low-pass coefficient:

```text
alpha = 1 / (1 + 1 / (2*pi*cutoff*dt))
filteredVector += alpha * (acceptedVector - filteredVector)
pixelDifference = (filteredVector - heldVector) * gainVector
emittedVector = pixelDifference * max(0, 1 - noiseBand / length(pixelDifference))
```

The radial noise band is bounded, reduces with motion confidence and follows the last emitted position. The same multiplier acts on both components. It has no axis snapping or forced straight line, and no minimum travel speed. Different outlier rejection times can still affect the path; this is not a promise of exact geometric direction on arbitrary noisy inputs.

The established `RelativeHeadAxis` precision/rest branch runs in parallel. The controller blends into the vector branch using one common scalar:

```text
u = clamp((max(axisConfidence) - 0.10) / 0.15, 0, 1)
blend = u*u*(3 - 2*u)
delta = precisionDelta + blend * (vectorDelta - precisionDelta)
```

This keeps the prior precision response when confidence is low and shares the response across both coordinates during deliberate travel. There is no explicit horizontal/vertical selection. Confidence and thresholds are heuristics requiring live evaluation, not recognition probabilities.

Both branches discard unissued filter remainder at a confirmed stop. The first sample or a capture gap seeds the current input and emits zero. The existing native trajectory, gesture freeze/rearm, explicit Stop and Engine recovery gates remain in use. The existing optional relative reach boost now reads confidence from the active relative controller; its default factor of 1 makes it inactive.

This is **relative displacement**, like moving a physical mouse: stopping your head stops the cursor at its current location, even when your head is away from neutral. Continuous joystick speed control remains a separate existing mapping option.

## Calibration through the existing interface

Five seconds of stable rest estimates neutral position, scale, facial rest levels and four-channel resting noise. Then guided right, left, up and down movements are collected twice with relaxed returns between them. A normal sequence takes about 35 seconds. Capture timestamps drive every stage; duplicate timestamps do not advance it, and a gap restarts the interrupted stage.

The profile stores measured signed ranges, directional sample counts, rest RMS/P95, repeated-range consistency and any unmeasured direction. A one-sided accessibility fallback is explicitly labelled; a motionless face cannot create a completed profile. Existing profiles without this additive metadata still load. The supplied UI consumes the same feedback contract. Cancelling does not overwrite an existing profile.

`CalibrationDetails` arrays use rest channels `[yaw, inverted pitch, horizontal shift, vertical shift]` and direction order `[left, right, up, down]`. Repeat consistency is the smallest/largest measured range across valid attempts; zero means insufficient repeated observations, not zero accuracy. Raw measured ranges remain separate from the balanced runtime ranges.

## Verification and comparison

`HeadControl.Tests` includes twelve diagonal cases at 10/30/60 FPS with unequal axis noise, stopping away from neutral, new and legacy profile persistence, all four measured directions, translation mode, explicit missing-direction fallback, insufficient motion, duplicate timestamps and interrupted calibration. The process suite drives the actual supplied Guardian and rebuilt Engine in `--no-input` mode through the guided calibration and verifies saved metadata. It also retains recovery and click regression cases.

Optional comparisons, from a complete delivery layout:

```powershell
dotnet Source/HeadControl.Tests/bin/Release/net10.0/HeadControl.Tests.dll --analog-benchmark Verification/Before-Analog/Qadr.Core.dll Verification/analog-benchmark.json
dotnet Source/HeadControl.Tests/bin/Release/net10.0/HeadControl.Tests.dll --analog-recording Verification/Before-Analog/Qadr.Core.dll <head.jsonl> <report.json> <output.json>
```

The benchmark has 24 matched pose scenarios. Historical replay integrates controller displacement with identical settings, profile and pose times, with gesture locking disabled equally for both candidates. It does not simulate native interpolation, screen-edge limits or a person's corrections. It cannot establish live target accuracy, latency or comfort.

New opt-in test traces and guided summaries carry `controllerRevision` and the loaded Core SHA256, so later tests can be attributed to their actual controller. Ordinary use does not enable numeric tracing.
