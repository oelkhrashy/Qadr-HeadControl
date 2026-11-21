# Qadr implemented pipelines

## Startup

Shell/App.xaml.cs OnLaunched creates MainWindow. MainWindow constructs the emerald pages and starts EngineConnection. EngineConnection.Start launches Guardian; Guardian/Program.cs registers F8, Ctrl+Alt+Esc and F6 and supervises Engine in a Windows job. Engine/Program.cs Controller starts VisionClient and Media Foundation enumeration through CameraCapture.Discover.

One device opens through OpenCamera. Multiple devices produce ChooseCamera; Shell/StartupGuidance.cs ShowCameraPicker displays numbered manual choices and PromptCameras speaks them before requesting the temporary camera grammar. Core/CameraVoice.cs Resolve strictly resolves supported ordinals. Engine Voice opens only a discovered selected device. Temporary listening stops after selection.

CameraStarted calls StartCalibration. ProcessObservation uses FacePositioning.Check, QualityGate.Accept, Calibration.Feed, any required ClickLearning lesson, BeginReturn and stable return before RequestResume. StartupFlow allows this chain once on normal launch. F8/failure cancels it. Guardian checks stop generation, user-stop epoch, acknowledged stop and heartbeat before enabling input. Shell stamps restart/setup commands before IPC; Engine rejects stale intent.

## Pointer and activation

MediaFoundation reads BGRA frames; CameraCapture.Write writes one shared-memory latest-frame slot with capture timestamp and sequence lock. vision/worker.py main mirrors the full frame and calls MediaPipe Face Landmarker. It returns landmarks, blendshapes, transformation matrix, capture time and a bounded preview. The Python worker has no input APIs.

Engine ProcessObservation calls Core/Features.Extract, QualityGate and FacePositioning. During calibration, Calibration.Feed uses robust rest statistics and directional ranges. During control, MotionController.Feed normalizes by personal range/scale, uses capture-time filter intervals, One Euro filtering, noise hysteresis and relative/rate mapping. CursorTrajectory retains fractions and Engine's roughly 8 ms pump calls gated NativeInput.Move.

Smile/mouth onset now freezes before the full trigger threshold, using a baseline-relative ratio and pre-onset cursor history. Gesture.Feed still qualifies held triggers/cooldown. EyeClick qualifies closure and fires on reopening. Dwell manages its precision anchor and once-per-rest activation. Frozen output resets the pending trajectory; release settling discards contaminated mapping history.

MouseActions.Trigger produces pure event plans. Engine StartMouse queues them at the anchor, and PumpMouse checks the gate for every native move/button/wheel event. ClearMouse releases owned input and clears queues/repeat on stop/loss/distance/mode change. Shell MousePalette publishes only loaded, visible, enabled target rectangles; Engine verifies actual root window and Shell ownership.

## Refitting

FacePositioning.UseComfortablePosition saves FittedObservation and increments Revision. After QualityGate accepts the frame, Engine consumes a new revision and calls MotionController.Rebase with that exact snapshot when a profile exists outside calibration. It clears pending motion/actions and requires relaxed return. This synchronizes refit clocks without replacing initial calibration's robust statistics. MotionController.AdaptDistance remains a standalone fallback.

## Routines

RoutineService.Hooks queues left/right release and optional key-release notifications. Collect queries UI Automation, excludes password/unknown fields and stores selectors/delays. Recording is not yet a complete mouse-event stream.

Run retains the confirmation stop epoch through its background task. Find requires one process/window and one matching semantic element. Missing/ambiguous targets refuse every action. Invoke/Value require their patterns; a click acts at the resolved visible element centre. Input refusal is reported as failure. Stop prevents new gated operations; an already invoked external UIA action cannot be rolled back.

## Differences from the review sketch

Capture timestamps, One Euro, distance normalization, noise handling, directional ranges and fractional motion already existed. Rigid-only pose, velocity-dependent gain, a new precision-state controller, full event recording and a live task measurement harness are not implemented by merely adding these pipelines. Numeric Replay.cs tests mapping/activation/trajectory after feature extraction and opens no hardware.
