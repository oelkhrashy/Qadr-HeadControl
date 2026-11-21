# Project rules

## Data
- Never upload, copy into a package, or modify anything in `D:/Codex_Agents/Qadr/data` (profiles, movement recordings).

## User settings
- Never overwrite the user's saved settings or learned bindings.
- New settings must default to current behaviour and be reversible.

## Runtime
- Keep processing CPU-only.
- Escape and the voice stop command must keep working.

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
