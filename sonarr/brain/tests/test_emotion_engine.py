"""Tests for emotion_engine.py — PAD emotion model."""

import sys
import os
# sys.path handled by pytest

from sonarr.brain.core_types import PADVector, Stimulus
from sonarr.brain.emotion_engine import EmotionEngine, classify_pad


class TestClassifyPAD:
    def test_all_positive(self):
        assert classify_pad(PADVector(0.5, 0.5, 0.5)) == "Exuberant"

    def test_all_negative(self):
        assert classify_pad(PADVector(-0.1, -0.1, -0.1)) == "Bored"

    def test_hostile(self):
        assert classify_pad(PADVector(-0.5, 0.5, 0.5)) == "Hostile"

    def test_anxious(self):
        assert classify_pad(PADVector(-0.5, 0.5, -0.5)) == "Anxious"

    def test_relaxed(self):
        assert classify_pad(PADVector(0.5, -0.5, 0.5)) == "Relaxed"


class TestEmotionEngine:
    def test_starts_neutral(self):
        ee = EmotionEngine()
        s = ee.get_state()
        assert s.pleasure == 0.0
        assert s.arousal == 0.0
        assert s.dominance == 0.0

    def test_map_known_stimulus(self):
        ee = EmotionEngine()
        stim = Stimulus("e1", "greeting", 0.5)
        impulse = ee.map_stimulus_to_pad(stim)
        assert impulse.pleasure > 0  # greetings are positive

    def test_map_unknown_stimulus(self):
        ee = EmotionEngine()
        stim = Stimulus("e1", "unknown_event_xyz", 0.5)
        impulse = ee.map_stimulus_to_pad(stim)
        assert impulse.pleasure == 0.0
        assert impulse.arousal == 0.0
        assert impulse.dominance == 0.0

    def test_apply_impulse(self):
        ee = EmotionEngine(reactivity=1.0)
        impulse = PADVector(0.5, 0.0, 0.0)
        ee.apply_impulse(impulse, intensity=1.0)
        assert ee.state.pleasure > 0.0

    def test_apply_impulse_with_reactivity(self):
        ee1 = EmotionEngine(reactivity=1.0)
        ee2 = EmotionEngine(reactivity=2.0)
        impulse = PADVector(0.3, 0.0, 0.0)
        ee1.apply_impulse(impulse, 1.0)
        ee2.apply_impulse(impulse, 1.0)
        assert ee2.state.pleasure > ee1.state.pleasure

    def test_decay_toward_neutral(self):
        ee = EmotionEngine(decay_rate=1.0)
        ee.state = PADVector(0.8, 0.8, 0.8)
        ee.decay(5.0)  # large dt
        assert ee.state.pleasure < 0.1
        assert ee.state.arousal < 0.1

    def test_decay_zero_dt(self):
        ee = EmotionEngine()
        ee.state = PADVector(0.5, 0.5, 0.5)
        ee.decay(0.0)
        assert ee.state.pleasure == 0.5  # no change

    def test_process_stimulus(self):
        ee = EmotionEngine(reactivity=1.0)
        stim = Stimulus("e1", "insult", 0.8)
        impulse = ee.process_stimulus(stim)
        assert impulse.pleasure < 0  # insult is negative pleasure
        assert ee.state.pleasure < 0  # state shifted negative

    def test_reset(self):
        ee = EmotionEngine()
        ee.state = PADVector(0.9, 0.9, 0.9)
        ee.reset()
        assert ee.state.pleasure == 0.0

    def test_classify(self):
        ee = EmotionEngine()
        ee.state = PADVector(0.5, 0.5, 0.5)
        assert ee.classify() == "Exuberant"
