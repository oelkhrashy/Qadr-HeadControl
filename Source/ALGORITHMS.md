# Studying Qadr's movement and clicks

**Current movement revision: analog-wave-3 (4 November 2026).** [ANALOG-MOVEMENT.md](ANALOG-MOVEMENT.md) describes the active relative/adaptive vector blend and calibration; [POINTER-STABILITY.md](POINTER-STABILITY.md) describes temporal smoothing, and [CROSS-AXIS-NOISE.md](CROSS-AXIS-NOISE.md) describes the active coordinate bands. The per-axis algorithm below is still its low-confidence precision branch; it no longer describes the complete default travel response.

## Application boundary

The packaged Python vision worker performs face inference. Its result contains head geometry, a face transformation matrix, expression scores, and the original capture timestamp. `Features.Extract` converts it into a typed `Observation`.

The Engine applies tracking-quality and freshness gates, runs `MotionController`, then feeds `CursorTrajectory` and mouse-action planning. Guardian supervises the Engine and owns the global stop mechanism. Inference does not directly inject mouse events.

`Qadr.Core.dll` and `Qadr.Engine.dll` are rebuilt in the recovery update. The other supplied application components and the packaged inference worker are retained from the ZIP.

## Relative head movement

Files: `Qadr.Core/RelativeHeadAxis.cs` and the relative/adaptive branch in `Qadr.Core/MotionController.cs`.

1. Use the calibrated profile to measure yaw/pitch displacement, or scale-normalized translation for a user calibrated to shifting.
2. Normalize by the learned range and apply configured direction reversal.
3. Use `AdaptiveAxisFilter` to reject isolated spikes and smooth the signal. Its cutoff rises during deliberate movement. Timing uses the interval between captured frames.
4. Use `RelativeRest` to detect a locally stable head position while distinguishing a slow coherent correction from rest.
5. Apply a hysteresis band that follows the last accepted position. Its base cap is two projected screen pixels before steadiness and precision scaling; the final cap is six pixels. The band becomes smaller during deliberate travel.
6. Emit the change in filtered position, scaled by screen dimensions and sensitivity:

```text
pixels_per_unit = screen_axis_size * 0.7 * sensitivity
cursor_delta = (filtered_position - preceding_position) * pixels_per_unit
```

The relative mapper has no central joystick dead zone: small corrections work at the calibrated neutral position as well as away from it. It does not infer the user's intended target or attract the pointer to the screen centre.

The bounded hysteresis band scales with `PrecisionNoiseMultiplier / 0.75`, so the existing startup tuner's precision adjustment affects this movement path. The 0.75 reference preserves the previous response. This is a steadiness parameter, not a learnt facial-expression model.

Vertical balance uses the calibrated downward range for both upward and downward displacement. This retains the existing downward gain and gives equal angular/translation changes the same sensitivity in both directions. Horizontal range selection is unchanged. Settings validation enables vertical balance once for older saved preferences, records `VerticalBalanceVersion = 1`, and then respects later explicit changes to that preference.

At a confirmed head stop, remaining filter displacement is discarded and the next movement starts from the current accepted pose. Rejected travel is not accumulated or replayed. Existing optional reach boost and speed limiting remain downstream; begin with reach boost 1.

`CursorTrajectory` interpolates accepted displacement between camera observations, preserves fractional coordinates, and bounds the cursor to the configured desktop. It also discards unfinished interpolation after observations become stale. Negative monitor coordinates remain valid.

## Left-click expression state

File: `Qadr.Core/Gesture.cs`.

```text
Stable neutral for 80 ms -> armed
Cross activation threshold -> start hold
Maintain expression above release threshold for hold duration -> one click
Continue holding -> anchored, no additional click
Stable neutral for 80 ms -> release and rearm
```

The release threshold is below the activation threshold. This is hysteresis: small changes near activation do not repeatedly toggle the state. A capture gap or invalid sample cancels pending intent and requires neutral again. Smile uses a 0.28-second hold; the existing mouth-click path uses 0.34 seconds. The existing configurable cooldown still applies.

This is one complete click per gesture, not a smile-down/smile-up drag controller. Qadr's separate drag mode remains available.

## Right-click eye state

File: `Qadr.Core/EyeClick.cs`.

```text
Stable open eyes -> armed
Selected eye gesture -> accumulate deliberate closed time
Required hold reached -> qualified
Stable reopening for 60 ms -> one right-click
Fresh neutral -> ready for another gesture
```

Either-eye mode supports a deliberate wink. Both-eye attempts require at least 0.4 seconds, reducing activation by ordinary short blinks. A specific-eye mode rejects bilateral closures. A closure longer than 2.5 seconds is canceled. Starting/recovering with eyes already closed cannot trigger a click.

MotionController gives an eye gesture priority over a partly completed left-click expression. It anchors pointer movement during expression gestures and discards contaminated motion-filter history after release/settling.

## Tracking recovery

`RecoveryAlignment` runs only while the Engine returns from lost tracking. It captures the saved neutral yaw/pitch, eye midpoint, face outline and available nose geometry. Its reference stays fixed while the returning face moves. Whole-face visibility, scale, position, head angle and available nose location must pass before the Engine collects a new resting window.

The review update accepts 0.10-radian angle and 0.08-image-unit per-axis position tolerances. A fresh neutral four-second hold outside that alignment offers hands-free adoption. The countdown appears in the existing hints and resets on pose/nose movement, invalid or missing observations and active gestures. This is an automatic fallback, not proof of user intent; holding an unintended posture still can adopt it. Bounds and scale checks are never bypassed. `RecoveryExpression` checks pre-loss effective activation thresholds so a held smile/wink cannot become the new expression baseline.

`ResumeRest` requires at least 0.6 seconds and 10 fresh, locally stable observations. It computes a median neutral pose and face box. The Engine applies that same median to `MotionController.Rebase` and `FacePositioning.UseComfortablePosition`, keeping the pointer model and face guide consistent. The existing startup-pointer plan centres the cursor once after automatic recovery in normal control; assessment mode retains its existing exception. Pending trajectory/gesture state is discarded.

The alignment guard remains pending until the resting window completes. It is inactive during normal pointing, so diagonals and comfortable head movement stay free. An explicit posture update establishes a new reference. Escape/Stop cancels automatic recovery. Missing result intervals use the same alignment path as explicit no-face results.

See `../TEST-RESULTS-AND-RECOVERY.md` for the case matrix, observed results, metric meanings and manual checks still required. The existing face outline/status hints are used; the nose reference is numeric rather than a newly drawn nose crosshair.

## Tests worth studying

- Movement through neutral and away from neutral.
- Equal travel for symmetric turns.
- Comparable travel at 10, 30 and 60 tracking FPS.
- No movement from an isolated pose spike.
- No coasting after a confirmed stop.
- One click from a long held smile; a new click only after release.
- Short blinks, deliberate winks, bilateral holds, and overlong closures.
- No clicks from an expression already active when tracking returns.
- Conflicting smile and wink without a double action.
- Stale interpolation, mode changes, and negative monitor coordinates.
- Recovery with off-centre faces, turned heads, displaced noses and missing result intervals.
- Stop during loss remaining stopped after an aligned face returns.
- Process integration in `--no-input` mode using the supplied Guardian and rebuilt Engine/Core.

These checks establish controller behavior for their inputs. Live camera noise, recognition quality, physical input delivery and user comfort require separate hands-on evaluation.
