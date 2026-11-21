"""Dependency-free checks for the worker's compact geometry. No camera access."""
import json
import math
from pathlib import Path
from types import SimpleNamespace
import unittest

from worker import face_geometry


def landmarks():
    points = [SimpleNamespace(x=.5 + math.sin(i) * .12,
                              y=.5 + math.cos(i) * .18,
                              z=math.sin(i) * .02) for i in range(478)]
    points[33] = SimpleNamespace(x=.4, y=.5, z=0)
    points[263] = SimpleNamespace(x=.6, y=.5, z=0)
    points[1] = SimpleNamespace(x=.54, y=.55, z=0)
    return points


class GeometryTests(unittest.TestCase):
    def test_eye_and_nose_geometry(self):
        g = face_geometry(landmarks())
        self.assertEqual((g["eyeX"], g["eyeY"], g["noseX"], g["noseY"]), (.5, .5, .54, .55))
        self.assertAlmostEqual(g["eyeScale"], .2)

    def test_full_face_bounds(self):
        points = landmarks()
        g = face_geometry(points)
        self.assertEqual((g["left"], g["top"], g["right"], g["bottom"]),
                         (min(p.x for p in points), min(p.y for p in points),
                          max(p.x for p in points), max(p.y for p in points)))

    def test_clipped_fraction_preserved(self):
        points = landmarks()
        for p in points[:40]:
            p.y = 1.1
        self.assertEqual(face_geometry(points)["outsideFraction"], 40 / 478)

    def test_nonfinite_coordinates_anywhere_rejected(self):
        for axis in ("x", "y", "z"):
            for invalid in (math.nan, math.inf, -math.inf):
                points = landmarks()
                setattr(points[-1], axis, invalid)
                self.assertIsNone(face_geometry(points))

    def test_insufficient_landmarks(self):
        self.assertIsNone(face_geometry([]))
        self.assertIsNone(face_geometry(landmarks()[:263]))

    def test_deterministic_summary(self):
        self.assertEqual(face_geometry(landmarks()), face_geometry(landmarks()))

    def test_summary_round_trip(self):
        g = face_geometry(landmarks())
        self.assertEqual(json.loads(json.dumps(g, allow_nan=False)), g)


def write_fixture(path):
    points = landmarks()
    common = dict(version=1, type="result", sequence=2, captured=1,
                  blendshapes={"eyeBlinkLeft": .7, "eyeBlinkRight": .1,
                               "mouthSmileLeft": .4, "mouthSmileRight": .6, "browInnerUp": .6, "mouthPucker": .4})
    common["blendshapes"].update({f"unusedBlendshape{i:02d}": .5 + math.sin(i) * .5 for i in range(46)})
    common["matrix"] = [1 if i in (0, 5, 10, 15) else 0 for i in range(16)]
    data = dict(full=dict(common, landmarks=[[p.x, p.y, p.z] for p in points]),
                compact=dict(common, landmarks=[], geometry=face_geometry(points)))
    Path(path).write_text(json.dumps(data, allow_nan=False, separators=(",", ":")), encoding="utf-8")



class VoiceContextTests(unittest.TestCase):
    def test_current_audio_is_accepted(self):
        import worker
        worker.voice_context = (4, 8)
        self.assertTrue(worker.current_voice_audio((4, 8), (4, 8)))

    def test_audio_before_stop_or_narration_is_rejected(self):
        import worker
        worker.voice_context = (4, 8)
        self.assertFalse(worker.current_voice_audio((3, 8), (4, 8)))
        self.assertFalse(worker.current_voice_audio((4, 7), (4, 8)))
        worker.voice_context = (5, 9)
        self.assertFalse(worker.current_voice_audio((4, 8), (4, 8)))


class PreviewTests(unittest.TestCase):
    def test_performance_mode_accepts_only_supported_integer_rates(self):
        from worker import configured_fps
        self.assertEqual(configured_fps(60), 60)
        for value in (30, 0, 120, None, "60", True, 60.0):
            self.assertEqual(configured_fps(value), 30)

    def test_preview_budget_matches_thirty_fps(self):
        from worker import PREVIEW_INTERVAL
        self.assertAlmostEqual(PREVIEW_INTERVAL, 1 / 30)

    def test_latest_job_replaces_old_jobs(self):
        from worker import PreviewPublisher
        publisher = PreviewPublisher(lambda x: x, lambda *a, **k: None, start=False)
        for i in range(100): publisher.submit(str(i), i, 1)
        self.assertEqual(publisher.jobs.qsize(), 1)
        self.assertEqual(publisher.jobs.get_nowait()[1], 99)
        publisher.close()

    def test_slow_preview_does_not_block_submit(self):
        import threading, time
        from worker import PreviewPublisher
        began, release = threading.Event(), threading.Event()
        def encode(frame): began.set(); release.wait(1); return frame
        publisher = PreviewPublisher(encode, lambda *a, **k: None)
        try:
            publisher.submit('first', 1, time.perf_counter()); self.assertTrue(began.wait(.5))
            started = time.perf_counter()
            for i in range(50): publisher.submit('newest', i, time.perf_counter())
            self.assertLess(time.perf_counter() - started, .1)
            self.assertLessEqual(publisher.jobs.qsize(), 1)
        finally: release.set(); publisher.close()

    def test_stale_preview_never_encodes(self):
        import time
        from worker import PreviewPublisher
        calls = []
        publisher = PreviewPublisher(lambda frame: calls.append(frame), lambda *a, **k: calls.append('sent'), now=lambda: 10)
        publisher.submit('stale', 1, 9); time.sleep(.07); publisher.close()
        self.assertEqual(calls, [])

    def test_fresh_preview_has_separate_message_type(self):
        import threading
        from worker import PreviewPublisher
        sent, done = [], threading.Event()
        def publish(kind, **values): sent.append((kind, values)); done.set()
        publisher = PreviewPublisher(lambda frame: 'encoded', publish, now=lambda: 10)
        try:
            publisher.submit('frame', 7, 9.99); self.assertTrue(done.wait(.5))
            self.assertEqual(sent[0][0], 'preview'); self.assertEqual(sent[0][1]['sequence'], 7)
        finally: publisher.close()

if __name__ == "__main__":
    import sys
    if len(sys.argv) == 3 and sys.argv[1] == "--fixture":
        write_fixture(sys.argv[2])
    else:
        unittest.main()
