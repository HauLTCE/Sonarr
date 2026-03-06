"""Tests for core_types.py — PADVector, RelationshipVector, Stimulus."""

import sys
import os
# sys.path handled by pytest

from sonarr.brain.core_types import PADVector, RelationshipVector, Stimulus, _clamp


class TestClamp:
    def test_within_range(self):
        assert _clamp(0.5) == 0.5

    def test_above_max(self):
        assert _clamp(2.0) == 1.0

    def test_below_min(self):
        assert _clamp(-3.0) == -1.0


class TestPADVector:
    def test_clamping(self):
        v = PADVector(2.0, -5.0, 0.5)
        assert v.pleasure == 1.0
        assert v.arousal == -1.0
        assert v.dominance == 0.5

    def test_addition(self):
        a = PADVector(0.3, 0.2, 0.1)
        b = PADVector(0.4, 0.5, 0.6)
        c = a + b
        assert abs(c.pleasure - 0.7) < 1e-9
        assert abs(c.arousal - 0.7) < 1e-9
        assert abs(c.dominance - 0.7) < 1e-9

    def test_addition_clamped(self):
        a = PADVector(0.8, 0.8, 0.8)
        b = PADVector(0.5, 0.5, 0.5)
        c = a + b
        assert c.pleasure == 1.0
        assert c.arousal == 1.0
        assert c.dominance == 1.0

    def test_scalar_multiply(self):
        v = PADVector(0.5, -0.5, 0.0)
        r = v * 2.0
        assert r.pleasure == 1.0  # clamped
        assert r.arousal == -1.0  # clamped

    def test_magnitude(self):
        v = PADVector(1.0, 0.0, 0.0)
        assert abs(v.magnitude() - 1.0) < 1e-9

    def test_lerp(self):
        a = PADVector(0.0, 0.0, 0.0)
        b = PADVector(1.0, 1.0, 1.0)
        mid = a.lerp(b, 0.5)
        assert abs(mid.pleasure - 0.5) < 1e-9
        assert abs(mid.arousal - 0.5) < 1e-9

    def test_as_tuple(self):
        v = PADVector(0.1, 0.2, 0.3)
        assert v.as_tuple() == (0.1, 0.2, 0.3)


class TestRelationshipVector:
    def test_clamping(self):
        r = RelationshipVector(trust=5.0, fear=-5.0)
        assert r.trust == 1.0
        assert r.fear == -1.0

    def test_update(self):
        r = RelationshipVector(trust=0.5)
        r.update("trust", 0.3)
        assert abs(r.trust - 0.8) < 1e-9

    def test_update_clamped(self):
        r = RelationshipVector(trust=0.9)
        r.update("trust", 0.5)
        assert r.trust == 1.0

    def test_update_invalid_dimension(self):
        r = RelationshipVector()
        try:
            r.update("nonexistent", 0.1)
            assert False, "Should have raised ValueError"
        except ValueError:
            pass

    def test_as_dict(self):
        r = RelationshipVector(trust=0.1, fear=0.2)
        d = r.as_dict()
        assert d["trust"] == 0.1
        assert d["fear"] == 0.2


class TestStimulus:
    def test_intensity_clamped(self):
        s = Stimulus("e1", "insult", intensity=5.0)
        assert s.intensity == 1.0
        s2 = Stimulus("e1", "insult", intensity=-1.0)
        assert s2.intensity == 0.0
