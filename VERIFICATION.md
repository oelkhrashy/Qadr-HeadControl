# Current release: Qadr 0.4.26

See D:/Codex_Agents/Qadr/outputs/Qadr-Experience-0.4.26/RELEASE_NOTES.md and VERIFICATION.json for current changes and limits.

# Current update: Qadr 0.4.25

See D:/Codex_Agents/Qadr/outputs/Qadr-Stability-0.4.25/RELEASE_NOTES.md and VERIFICATION.json.

# Qadr 0.4.24 - simpler control and reversible adjustments

## Control screen

The camera preview remains visible at compact sizes. Everyday controls are
Pause/Resume, Mouse tools, Help me adjust, and More. The inline mouse-action
palette is hidden; the existing right-side dock retains all mouse actions.
More contains camera selection, gesture learning and recentre. Existing setup
guidance and recovery actions remain available. Mode feedback prominently shows
Left click, Right click, Holding an item or Scrolling, with a short next action.
Paused and tracking-paused states explicitly say that control is not active.
Screen-reader live feedback is attached to mode and adjustment result text.

## Help me adjust

- The pointer is too fast: reduces sensitivity by 10%, bounded by existing limits.
- I cannot reach the edges: adds 0.25 to screen reach, bounded at 4x.
- The pointer shakes at rest: increases the precision noise band by 10%, bounded
  at its existing maximum. This can make very small corrections harder; Undo is
  offered so the user can evaluate comfort.
- Undo last adjustment restores only the previous sensitivity, screen reach and
  precision-band values. It preserves current gesture settings, inversion,
  calibration, routines and other preferences. Repeated adjustments keep one
  previous step. Undo is available during this app session and is invalidated
  when movement values change elsewhere. It is separate from automatic-test Undo.

Apply and Undo are handled and persisted by Engine. The panel waits for Engine
status to show the result. It is disabled during tests, gesture lessons, routine
playback, dragging or scrolling. Escape invalidates stale queued adjustment
commands; neither Apply nor Undo resumes stopped control. The pinned Undo button
stays available while panel text can scroll. Existing settings-change reset and
mouse-release safeguards are reused. No new pointer injection or gestures added.

## Preserved behaviour

No startup default or owner setting is rewritten by installing this release.
The existing 45% speed / 3.5x reach / 900 px/s owner settings remain as saved.
Motion.cs, AdaptiveAxisFilter, RelativeRest, RelativeReach, travel intent,
Positioning, facial gesture implementation, personal noise learning, automatic
tuning and numeric recorder are byte-identical to the source before this update.
The recorder retains its controller-version label 0.4.23; BUILD.json identifies
the UI release as 0.4.24. Existing Python worker, pinned dependencies, protocol
and model assets are unchanged. Core contracts add an optional adjustment status
and two stop-epoch-checked commands. These do not alter pose interpretation.

## Verification

541 C# checks each in Adaptive and Classic; 545 with watchdog; 26 automatic tuning
and adjustment process checks; 20 launch, 60 hands-free, 16 tutorial and 11 worker
checks; 15 Python tests; Python-to-C# compact protocol verification; frozen worker
model self-test; 41 rendered layouts including four mode states and two adjustment
panels. Process tests cover adjustment persistence, Undo persistence, preserving
a later gesture preference, and rejecting stale adjustment commands after Escape.

The two recent real recordings (120429 and 125955) replay 1,476 control samples
against the 0.4.23 baseline. Travel, per-axis travel, speed-limit events, peak speed
and appended-stop metrics match exactly. The source comparison also confirms
unchanged movement/gesture implementation. This verifies software preservation;
it does not establish live camera accuracy, user comfort, real gesture outcomes
or a fresh-PC installation. No native pointer input is injected by these tests.

Customer installer excludes owner settings, calibration, routines and reports.
Unsigned Windows x64 testing release. Desktop shortcut and Run-Qadr.cmd target
this release after activation; the old runnable release is retired.


Open D:\Codex_Agents\Qadr\Run-Qadr.cmd or the Qadr desktop shortcut.
Customer ZIP: D:\Codex_Agents\Qadr\outputs\Qadr-Simple-0.4.24\Qadr-Customer-0.4.24-Windows-x64.zip
