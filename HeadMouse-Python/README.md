# HeadMouse-Python: a steady pointer to try, and what we measured

Hi Omar. Joe and I spent an evening measuring head-pointer steadiness on his laptop, with your Qadr build and ours on the same day, same person, same camera, same room. This folder lets you try our pointer, and the C# changes in this pull request bring the fixes and the main idea into Qadr. Every number below comes from the tools in `tools/`.

## Try it (about 2 minutes)

Windows 10/11, Python 3.11, a webcam. From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File HeadMouse-Python\run-live-test.ps1
```

1. **Calibration (about a minute).** Look at the window in the middle of the screen and hold still. Turn left, right, up and down as far as is comfortable and hold each, then smile three times. Each step only counts once it **sees** you do it; it says so if you moved, turned the wrong way, or didn't move far enough.
2. **Pointer live.** Head position maps to the main screen. It never clicks.
3. **Ctrl+Alt+T** runs the 18-task movement test, a port of your MovementLab protocol scored to your APPLICATION-GOALS thresholds. **Ctrl+Alt+G** cycles the glide (smooth, balanced, direct). **Hold Esc** to stop.

Reports go to `%APPDATA%\HeadMouse\` (numbers only, no images).

## What the pointer does (`headmouse/stabilizer.py`)

The face tracking is the same as yours: MediaPipe Face Landmarker, 0.10.21. What's different comes after it:

1. **Skip repeated frames.** Only 614 of 1,829 frames from this laptop camera were new pictures (10.1 real fps, each repeated 3×).
2. **One Euro filter** on yaw and pitch.
3. **Sustained parking**, with thresholds from calibration. The output holds while the head moves slower than this person's measured still-speed. It releases on movement sustained for 0.12 s, on one clearly fast frame, or on drift beyond 2× their sway, and the ignored sway is never jumped to.
4. **Learned cross-axis and smile correction.** Pitch along the left/right sweeps (and yaw along the nods), plus how much a smile drags the pose, are learned in calibration and subtracted. While a smile is on, the cursor holds where it was just before the smile.
5. **Position mapping** onto the main screen. The cursor glides between real pictures at 120 Hz.

## What we measured

**Still test: 30 s, six pre-processing variants on the same frames** (`tools/still_test.py`):
- The model's own noise is small: 0.04 to 0.08 deg, and strongly correlated frame to frame (not white).
- What moves is the person: 0.5 deg yaw and 1.1 deg pitch over 30 s, 9 to 15× the model noise. Mostly slow posture sway; about 31% of pitch is breathing.
- Cropping, 1280×720, CLAHE and bilateral smoothing changed the noise by about 10% either way.

**Pre-processing won't fix steadiness.** Telling sway from intent by speed does.

**Qadr earlier that evening** (from its own `movement-reports`):
- In 74 one-second windows where the head moved under 0.6 deg, the cursor still travelled 63 px/s on median.
- The controller emitted motion on 389 of 400 frames and declared both axes stopped on 2.
- Calibration had measured about 8 deg of pitch range, so 1 deg of nod was roughly 90 px.

**Same evening, same machine:**

| | Qadr (Nov 2 build) | HeadMouse |
|---|---|---|
| Cursor drift in short still moments (head under 1.5 deg/s, 0.6 s or longer) | 81.5 px/s (7 moments) | 14.9 px/s (46 moments) |
| Holds of 2 s or more | none in the 71 s run | 9 holds, 22 s, 0 px |
| 18-task long-stroke cross-track RMS, right / left | run ended without a scored report | 53 / 24 px |
| 3 s rest: total RMS / drift | not saved | 7.1 / 18.6 px |

Your 1 November recording had 82 / 42 px cross-track and 13.9 / 43.7 px on the 3 s rest. **Neither app passes your thresholds** (8 px cross-track, 6 px drift).

**Cross-axis coupling depends on posture.** Turning 20 deg shifted pitch by up to about 5 deg, with a different shape in each of five calibrations; in one session it was 4.8 deg turning right against 1.4 deg left. Learning it from one sweep per side cut vertical drift on held-out quick turns from about 258 px to 107 px. Better, not solved.

**Smiling moves the pose:** 2.7 to 5.4 deg of pitch per unit of smile score, across five calibrations.

## The C# changes in this pull request

All 155 existing checks still pass, plus 18 new ones (173 in total), built with .NET SDK 10.0.403. Each fix carries a comment explaining why.

| Change | Files |
|---|---|
| Wink right-click needs a learned wink (`EyesLearned`); "either" now means one eye only, so a slow both-eye blink never right-clicks ("both" still works when chosen) | `MotionController.cs`, `EyeClick.cs` |
| Dwell can't click where Qadr itself moved the pointer (startup or recovery centring) until the user has moved it | `MotionController.cs`, `Controller.cs` |
| Calibration, lessons and camera switches clear the old recovery alignment | `Controller.cs` |
| The face tracker may restart up to 3 times in 10 minutes, not once per session | `WorkerRestartPolicy.cs`, `Controller.cs` |
| Routine replay asks before OK, Remove, Transfer, Post, unnamed (icon-only) buttons and their Arabic equivalents | `RoutineSafety.cs`, `RoutineService.cs` |
| Six double-encoded dashes in user-visible strings | `Controller.cs` |
| **StillnessPark**: the parking idea for your relative pointer, with still-speed and sway now measured by calibration's rest step | `StillnessPark.cs`, `Calibration.cs`, `CalibrationDetails.cs`, `MotionController.cs`, `Settings.cs` |

**`ParkWhenStill` is off by default,** because it conflicts with your F07 fine-correction goal. Your test "fine horizontal motion through neutral" moves 0.23 deg in one second, which is slower and smaller than normal resting sway, so parking holds it. In the new tests, parking cuts resting-sway drift from 64 to 20 px/s at 10 fps and from 65 to 12 px/s at 30 fps, while a deliberate turn travels the same (663 vs 661 px). Turn it on, recalibrate (so the rest speed is measured), and decide.

Not covered: the Shell crash we saw (`NullReferenceException` in `EngineConnection.Send` from `MainWindow.EndAssessmentUi` on window activation) is in Shell code that isn't in this repo.

## Open problems in ours

- Up/down wobble on rightward strokes (53 px RMS): calibration needs more than one sweep per side.
- A small calibrated down range makes up/down oversensitive.
- A very slow, tiny start can't be told from sway, so it waits for about 1 deg.
- At 10 real fps the smooth glide adds about 90 ms of delay (about 33 ms on a 30 fps camera).

**Suggested next step:** both apps through the same 18-task protocol, same machine, same person, same day.
