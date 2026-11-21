# Project rules

## Data
- Never upload, copy into a package, or modify anything in `D:/Codex_Agents/Qadr/data` (profiles, movement recordings).

## User settings
- Never overwrite the user's saved settings or learned bindings.
- New settings must default to current behaviour and be reversible.

## Runtime
- Keep processing CPU-only.
- Escape and the voice stop command must keep working.

## Build & test
- No .NET SDK is on PATH. Use the pinned SDK 10.0.401 at `D:\Codex_Agents\Qadr\work\toolchain\dotnet\dotnet.exe` (set `DOTNET_ROOT` to that folder).
- To verify, run only the restore/build/test steps from `build.ps1`; running the whole script also publishes over the packaged app in `outputs/Qadr-Windows`.

## Git & GitHub
- Remote `origin`: https://github.com/oelkhrashy/Qadr-HeadControl
- This folder is pushed to branch `native`; local `main` tracks `origin/native`.
- `origin/main` (code under `Source/`) has an unrelated history and layout. Don't merge into it, pull from it or push to it without asking.
- The user has given standing permission to act in this folder without asking (2026-11-21): commit reviewed work and push to `native`. Still never push to, merge into or pull from `origin/main`, never force-push, and never touch the data folder.

## Tuning
- Change one gain, filter or gesture threshold at a time.
- Report before/after results on the same recordings.
- The camera actually runs at ~16.65 Hz (~60 ms/frame). Judge timing constants in frames, not milliseconds.

## Validation
- Passing synthetic tests doesn't prove usability, comfort or camera accuracy.

## Working style
- Keep answers short.
- Read only the files needed.
- Codex also edits this project; before changing code, check what changed recently and don't undo Codex's work without asking.
