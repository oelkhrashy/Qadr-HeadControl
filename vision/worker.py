"""Replaceable inference sidecar. It cannot move the pointer or inject keys.

Frames use a versioned, one-slot BGRA mapping with a sequence lock. Results
carry the capture timestamp; stdout is exclusively the JSON protocol.
"""
from __future__ import annotations
import argparse
import os
import json
import math
import mmap
import queue
import struct
import sys
import threading
import time
from pathlib import Path

CAPACITY = 64 + 640 * 480 * 4
PREVIEW_INTERVAL = 1 / 30
target_fps = 30
def configured_fps(value):
    return 60 if type(value) is int and value == 60 else 30
output_lock = threading.Lock()
stop = threading.Event()
speech_stop = threading.Event()
commands: list[str] = []
custom_phrases = None
voice_thread = None
voice_context = (0, 0)

def current_voice_audio(captured_context, recognizer_context):
    """Reject pre-stop and pre-narration audio instead of relabeling its intent."""
    return captured_context == recognizer_context == voice_context
# Keep numerical libraries from creating a thread per logical CPU.
for key,value in {"OPENBLAS_NUM_THREADS":"1","OMP_NUM_THREADS":"2","MKL_NUM_THREADS":"1","NUMEXPR_NUM_THREADS":"1"}.items():
    os.environ[key] = value

def emit(kind, **values):
    with output_lock:
        print(json.dumps(dict(version=1, type=kind, **values), separators=(",", ":"), allow_nan=False), flush=True)

def face_geometry(points):
    """Summarize precisely the coordinates consumed by Qadr.Core.Features.

    Validate every landmark (including z), preserve full bounds and the fraction
    outside the image, and leave pose/blendshape interpretation in the Engine.
    """
    if len(points) < 264:
        return None
    left = top = math.inf
    right = bottom = -math.inf
    outside = 0
    for point in points:
        if not all(math.isfinite(v) for v in (point.x, point.y, point.z)):
            return None
        left, right = min(left, point.x), max(right, point.x)
        top, bottom = min(top, point.y), max(bottom, point.y)
        outside += not (0 <= point.x <= 1 and 0 <= point.y <= 1)
    a, b, nose = points[33], points[263], points[1]
    dx, dy = a.x - b.x, a.y - b.y
    return dict(eyeX=(a.x + b.x) / 2, eyeY=(a.y + b.y) / 2,
                eyeScale=math.sqrt(dx * dx + dy * dy), noseX=nose.x, noseY=nose.y,
                left=left, top=top, right=right, bottom=bottom,
                outsideFraction=outside / len(points))

class PreviewPublisher:
    """One replaceable preview job; image encoding cannot block inference."""
    def __init__(self, encode, publish, now=time.perf_counter, start=True):
        self.encode, self.publish, self.now = encode, publish, now
        self.jobs = queue.Queue(maxsize=1)
        self.closed = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        if start: self.thread.start()

    def submit(self, frame, sequence, captured):
        try: self.jobs.get_nowait()
        except queue.Empty: pass
        try: self.jobs.put_nowait((frame, sequence, captured))
        except queue.Full: pass

    def run(self):
        while not self.closed.is_set():
            try: frame, sequence, captured = self.jobs.get(timeout=.05)
            except queue.Empty: continue
            if self.now() - captured > .2: continue
            try:
                encoded = self.encode(frame)
                if encoded is not None and not self.closed.is_set() and self.now() - captured <= .2:
                    self.publish("preview", sequence=sequence, captured=captured, preview=encoded)
            except Exception:
                # Preview failure must not stop or queue facial observations.
                continue

    def close(self):
        self.closed.set()
        if self.thread.is_alive(): self.thread.join(timeout=.25)


def speech(model_path):
    try:
        import sounddevice as sd
        from vosk import Model, KaldiRecognizer, SetLogLevel
        SetLogLevel(-1)
        model = Model(str(model_path))
        audio = queue.Queue(maxsize=4)
        rate = int(sd.query_devices(kind="input")["default_samplerate"])
        def receive(data, frames, timing, status):
            if audio.full():
                try: audio.get_nowait()
                except queue.Empty: pass
            try: audio.put_nowait((bytes(data), voice_context))
            except queue.Full: pass
        previous = None
        emit("voice_status", text="Listening for English commands")
        with sd.RawInputStream(samplerate=rate, blocksize=2000, dtype="int16", channels=1, callback=receive):
            while not stop.is_set() and not speech_stop.is_set():
                latest = (tuple(commands), None if custom_phrases is None else tuple(custom_phrases), voice_context)
                if latest != previous:
                    phrases = list(latest[1]) if latest[1] is not None else ["stop the app", "resume", "resume the app", "computer stop", "computer confirm", "computer pause pointer", "computer resume pointer", "computer start recording", "computer stop recording"]
                    if latest[1] is None: phrases.extend("computer run " + name for name in latest[0])
                    recognizer = KaldiRecognizer(model, rate, json.dumps(phrases + ["[unk]"]))
                    previous = latest
                try: data, captured_context = audio.get(timeout=.1)
                except queue.Empty: continue
                if not current_voice_audio(captured_context, latest[2]): continue
                if recognizer.AcceptWaveform(data):
                    phrase = json.loads(recognizer.Result()).get("text", "")
                    if phrase in phrases and current_voice_audio(captured_context, latest[2]):
                        emit("voice", text=phrase, userStopEpoch=latest[2][0], voiceRevision=latest[2][1])
        emit("voice_status", text="Microphone off")
    except Exception as exc: emit("voice_error", text=str(exc))

def listen(model):
    global commands, custom_phrases, voice_thread, voice_context, target_fps
    try:
        for line in sys.stdin:
            obj = json.loads(line)
            if obj.get("version") != 1: continue
            if obj.get("type") == "voice":
                commands = obj.get("commands", [])[:100]
                custom_phrases = obj.get("phrases")
                voice_context = (int(obj.get("userStopEpoch", 0)), int(obj.get("voiceRevision", 0)))
                if obj.get("enabled"):
                    if speech_stop.is_set() and voice_thread is not None and voice_thread.is_alive(): voice_thread.join(timeout=1)
                    if voice_thread is None or not voice_thread.is_alive():
                        speech_stop.clear()
                        voice_thread = threading.Thread(target=speech, args=(model,), daemon=True)
                        voice_thread.start()
                    elif speech_stop.is_set(): emit("voice_error", text="Microphone is still stopping. Choose Listen again.")
                else: speech_stop.set()
            elif obj.get("type") == "performance": target_fps = configured_fps(obj.get("fps"))
            elif obj.get("type") == "shutdown": break
    except Exception as exc: emit("error", text="Worker command error: " + str(exc))
    finally: stop.set(); speech_stop.set()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--session")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--compact-results", action="store_true")
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--speech-model", type=Path, required=True)
    args = parser.parse_args()
    import cv2
    cv2.setNumThreads(1)
    import numpy as np
    import mediapipe as mp
    if args.self_test:
        options = mp.tasks.vision.FaceLandmarkerOptions(base_options=mp.tasks.BaseOptions(model_asset_path=str(args.model)), output_face_blendshapes=True, output_facial_transformation_matrixes=True)
        with mp.tasks.vision.FaceLandmarker.create_from_options(options) as detector:
            result = detector.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.zeros((480,640,3),dtype=np.uint8)))
        from vosk import Model, KaldiRecognizer, SetLogLevel
        SetLogLevel(-1)
        speech_model = Model(str(args.speech_model))
        KaldiRecognizer(speech_model, 16000, json.dumps(["computer stop", "[unk]"]))
        emit("self_test", text="Face inference and English speech model loaded", landmarks=[] if not result.face_landmarks else None)
        return
    if not args.session: parser.error("--session is required outside self-test")
    threading.Thread(target=listen, args=(args.speech_model,), daemon=True).start()
    # Engine creates the map before starting this process.
    with mmap.mmap(-1, CAPACITY, tagname=args.session + ".frames", access=mmap.ACCESS_READ) as frames:
        options = mp.tasks.vision.FaceLandmarkerOptions(
            base_options=mp.tasks.BaseOptions(model_asset_path=str(args.model)),
            running_mode=mp.tasks.vision.RunningMode.VIDEO, num_faces=1,
            min_face_detection_confidence=.6, min_face_presence_confidence=.6, min_tracking_confidence=.6,
            output_face_blendshapes=True, output_facial_transformation_matrixes=True)
        previous = -1
        last_timestamp = -1
        preview_at = 0
        inference_at = 0
        def encode_preview(rgb):
            import base64
            small = cv2.resize(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), (320, 240))
            ok, jpeg = cv2.imencode(".jpg", small, [cv2.IMWRITE_JPEG_QUALITY, 60])
            return base64.b64encode(jpeg).decode("ascii") if ok else None
        previews = PreviewPublisher(encode_preview, emit)
        with mp.tasks.vision.FaceLandmarker.create_from_options(options) as detector:
            emit("ready")
            while not stop.is_set():
                seq = struct.unpack_from("<q", frames, 0)[0]
                if seq <= 0 or seq == previous or seq & 1: stop.wait(.004); continue
                remaining = 1 / target_fps - (time.perf_counter() - inference_at)
                if remaining > 0:
                    stop.wait(min(.002, remaining)); continue
                width, height, stride = struct.unpack_from("<iii", frames, 8)
                captured = struct.unpack_from("<d", frames, 24)[0]
                if not (0 < width <= 640 and 0 < height <= 480 and stride == width * 4):
                    emit("error", text="Invalid frame layout"); return
                pixels = frames[64:64 + stride * height]
                if struct.unpack_from("<q", frames, 0)[0] != seq: continue
                previous = seq
                if time.perf_counter() - captured > .25: continue
                frame = np.frombuffer(pixels, dtype=np.uint8).reshape(height, width, 4)
                rgb = cv2.cvtColor(cv2.flip(frame, 1), cv2.COLOR_BGRA2RGB)
                stamp = max(last_timestamp + 1, int(captured * 1000)); last_timestamp = stamp
                # Submit the current image before face inference; display need not wait for it.
                if captured - preview_at >= 1 / target_fps:
                    previews.submit(rgb, seq, captured); preview_at = captured
                started = time.perf_counter(); inference_at = started
                result = detector.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), stamp)
                emit("result", sequence=seq, captured=captured,
                    landmarks=[[float(p.x), float(p.y), float(p.z)] for p in result.face_landmarks[0]] if result.face_landmarks and not args.compact_results else [],
                    geometry=face_geometry(result.face_landmarks[0]) if result.face_landmarks and args.compact_results else None,
                    blendshapes={c.category_name: float(c.score) for c in result.face_blendshapes[0]} if result.face_blendshapes else {},
                    matrix=result.facial_transformation_matrixes[0].flatten().tolist() if result.facial_transformation_matrixes else [],
                    preview=None, inferenceMs=(time.perf_counter()-started)*1000)
        previews.close()
    stop.set()

if __name__ == "__main__":
    try: main()
    except Exception as exc: emit("error", text=str(exc)); sys.exit(1)
