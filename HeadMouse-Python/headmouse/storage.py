"""Where everything he saves lives, and how it is written so it survives.

One rule above the rest: a bad file must never become a state he cannot get
out of. So every load degrades to "nothing saved" rather than raising, and
every save is atomic with a backup, so a crash or a power cut mid-write
leaves the previous good copy in place.

Files live in %APPDATA%\\HeadMouse (or ~/.headmouse elsewhere), never in the
program folder: an installed program folder is read-only for a normal user,
and settings written next to the code are lost on every update.

Each file is a small JSON envelope:
    {"format": 1, "sha256": <hash of the payload>, "payload": {...}}
The hash catches a truncated or hand-mangled file. A file that fails it is
moved aside as <name>.corrupt-<timestamp> (kept, never deleted, so nothing
is silently lost) and the .bak copy is tried next.
"""

from __future__ import annotations

import hashlib
import json
import os
import tempfile
import time

APP_DIR_NAME = "HeadMouse"
FORMAT = 1


def data_dir() -> str:
    """Per-user data folder, created on first use. HEADMOUSE_DATA overrides
    it, which is how the self-test keeps its files out of the real one."""
    override = os.environ.get("HEADMOUSE_DATA")
    if override:
        base = override
    elif os.name == "nt" and os.environ.get("APPDATA"):
        base = os.path.join(os.environ["APPDATA"], APP_DIR_NAME)
    else:
        base = os.path.join(os.path.expanduser("~"), ".headmouse")
    os.makedirs(base, exist_ok=True)
    return base


def path_for(name: str) -> str:
    return os.path.join(data_dir(), name)


def _digest(payload) -> str:
    raw = json.dumps(payload, sort_keys=True, separators=(",", ":"),
                     ensure_ascii=False).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def save_json(name_or_path: str, payload) -> str:
    """Atomic write: temp file in the same folder, fsync, keep the previous
    good file as .bak, then os.replace. Returns the final path."""
    path = (name_or_path if os.path.isabs(name_or_path)
            else path_for(name_or_path))
    folder = os.path.dirname(path) or "."
    os.makedirs(folder, exist_ok=True)
    env = {"format": FORMAT, "saved_at": time.strftime("%Y-%m-%d %H:%M:%S"),
           "sha256": _digest(payload), "payload": payload}
    fd, tmp = tempfile.mkstemp(prefix=".tmp-", suffix=".json", dir=folder)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            json.dump(env, fh, indent=2, ensure_ascii=False)
            fh.flush()
            os.fsync(fh.fileno())
        if os.path.exists(path) and _read_valid(path) is not None:
            try:
                os.replace(path, path + ".bak")
            except OSError:
                pass
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            try:
                os.remove(tmp)
            except OSError:
                pass
    return path


def _read_valid(path: str):
    """Payload if the file is a valid envelope (or a plain legacy JSON
    object), else None. Never raises."""
    try:
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
    except Exception:
        return None
    if isinstance(data, dict) and "payload" in data and "sha256" in data:
        if data.get("sha256") != _digest(data["payload"]):
            return None
        return data["payload"]
    # A legacy file written before the envelope existed: accept a plain
    # object so an old profile.json is not thrown away on upgrade.
    if isinstance(data, dict):
        return data
    return None


def _quarantine(path: str) -> None:
    try:
        os.replace(path, f"{path}.corrupt-{time.strftime('%Y%m%d-%H%M%S')}")
    except OSError:
        pass


def load_json(name_or_path: str, default=None):
    """The saved payload, the backup if the main file is damaged, or
    `default`. A damaged file is set aside, never deleted."""
    path = (name_or_path if os.path.isabs(name_or_path)
            else path_for(name_or_path))
    if os.path.exists(path):
        got = _read_valid(path)
        if got is not None:
            return got
        _quarantine(path)
    bak = path + ".bak"
    if os.path.exists(bak):
        got = _read_valid(bak)
        if got is not None:
            return got
    return default
