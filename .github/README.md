# Qadr HeadControl

Qadr is a Windows x64 head-controlled mouse learning project. It maps webcam-derived head observations to cursor movement and supports smile/mouth left-clicking, deliberate wink right-clicking, calibration and tracking recovery.

The current revision is `analog-wave-3` (4 November 2026). It keeps Qadr's existing interface and combines relative precision/vector movement with additional capture-timed travel smoothing and bounded coordinate noise bands using independent movement evidence. Free diagonals remain available. Calibration collects comfortable left, right, up and down movements twice and stores noise/range evidence. See [the movement equations](../Source/ANALOG-MOVEMENT.md) and [the active noise-band equations and tradeoffs](../Source/CROSS-AXIS-NOISE.md).

After tracking loss, control waits for valid stable tracking. A changed comfortable posture can be adopted after a four-second stable hold, followed by the resting check. Active click gestures prevent adoption.

## Source

- [RecoveredCore](../Source/RecoveredCore): movement, gestures, calibration, smoothing and recovery gates.
- [RecoveredEngine](../Source/RecoveredEngine): camera/worker coordination and recovery state transitions.
- [MovementLab](../Source/MovementLab): guided pointing and stillness measurements.
- [HeadControl.Tests](../Source/HeadControl.Tests): automated controller and process checks.
- [Algorithms](../Source/ALGORITHMS.md): implementation notes for study.
- [Analysis](../Source/Analysis): local recording analysis tools. Supply your own recordings; none are included.

The matching Core and Engine were recovered from the application assemblies supplied for this project and modified. This repository does not contain the complete original application source. The original Shell, Guardian, packaged vision worker, runtime and face models are not included in the source repository. It cannot build the entire runnable application from source alone. Smyle Mouse source code was not used.

## Build and run controller checks

Install a compatible .NET 10 SDK, then run from the repository root:

```powershell
dotnet restore .\Source\HeadControl.Tests\HeadControl.Tests.csproj --configfile .\Source\NuGet.Offline.Config -p:NuGetAudit=false
dotnet build .\Source\HeadControl.Tests\HeadControl.Tests.csproj --no-restore -c Release
dotnet .\Source\HeadControl.Tests\bin\Release\net10.0\HeadControl.Tests.dll
```

The controller and test projects have no third-party NuGet package dependencies. Offline restore requires the SDK's targeting packs to be installed. Building the Windows Engine also requires matching Windows SDK/WPF reference packs.

The delivery scripts `Build-Controller.ps1`, `Build-Recovery.ps1`, `Build-Movement-Test.ps1` and `Package-Delivery.ps1` are for a complete local delivery layout with `Application/` and, where required, original comparison assemblies in `Verification/`. They are not a standalone full-app build pipeline for this source-only checkout.

## Verification and goals

The local delivery passed 155 controller checks, 156 public-interface checks and 23 synthetic process checks. An 18-case horizontal wave comparison, two 24-scenario matched benchmarks, three step probes, a 300-sample bypass comparison and two historical pose replays compare the revised movement with the preceding delivery. Filtering reduces synthetic variation but can reduce reach or worsen some path/drift metrics. These are offline checks; current live comfort, native input accuracy and end-to-end performance remain unmeasured. Original comparison binaries and personal recordings are excluded here, so a source-only checkout cannot reproduce every comparison.

The target is successful qualification of every supported function. Overall real-world accuracy is unmeasured. Synthetic pass rates do not establish live accuracy, physical cursor injection, native click correctness or comfort. Horizontal drift and resting steadiness remain areas for live validation. The current recovery revision still needs live validation.

## Local data and distribution

The source upload excludes application binaries, settings, calibration, numeric head traces, recordings, personal test results, verification backups and generated build files. These remain in the existing local application folder. Reusable synthetic observations within test source are included.

A runnable Windows package is a separate distribution artifact. This repository does not currently include a downloadable binary release.

## License status

A project-wide license has not been selected. No redistribution rights for supplied binaries or third-party components are asserted by this README. A future binary release needs its own reviewed contents and applicable notices.
