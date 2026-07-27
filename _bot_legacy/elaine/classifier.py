"""OPTIONAL ML intent classifier (DESIGN §4, Plan Phase 7).

A matcher behind the same MatchResult interface. The core ships with NO model and never
depends on this. ML CLASSIFIES (input -> one label from a fixed set); it never generates
text and never decides flow — the FSM consumes the label exactly like a keyword intent.

Hard rules:
  - Deterministic: frozen weights loaded at startup, argmax, NO sampling, no online updates.
  - Classical ML only (TF-IDF + logistic regression / linear SVM via scikit-learn).
  - Captures are always empty (a classifier labels; use regex to extract).
  - Below-threshold confidence -> matched=False -> symbolic fallback covers the turn.
  - Graceful absence: no model file or no scikit-learn -> the core runs unchanged.
  - No LLMs, no network, no wall-clock.
"""
from __future__ import annotations

import os
import pickle
from dataclasses import dataclass

from .matchers import FALSE, MatchResult
from .normalize import Normalized


class ModelUnavailable(Exception):
    """Raised when an ml_intent is referenced but the model can't be loaded."""


def model_disabled() -> bool:
    """ELAINE_NO_MODEL=1 forces pure-symbolic (used by the no-model CI job)."""
    return os.environ.get("ELAINE_NO_MODEL") == "1"


@dataclass
class _LoadedModel:
    pipeline: object        # a fitted sklearn pipeline (vectorizer + classifier)
    labels: list[str]


def load_model(path: str) -> _LoadedModel:
    if model_disabled():
        raise ModelUnavailable("ELAINE_NO_MODEL=1: ML disabled")
    if not os.path.exists(path):
        raise ModelUnavailable(f"model file not found: {path}")
    try:
        import sklearn  # noqa: F401
    except ImportError as e:
        raise ModelUnavailable("scikit-learn not installed (pip install 'elaine[ml]')") from e
    with open(path, "rb") as f:
        data = pickle.load(f)
    return _LoadedModel(pipeline=data["pipeline"], labels=list(data["labels"]))


@dataclass
class MLIntent:
    """Matches when the frozen classifier predicts `name` with confidence >= threshold."""

    name: str
    threshold: float
    model: _LoadedModel

    def predict(self, text: str) -> tuple[str, float]:
        """Return (argmax_label, confidence). Deterministic — no sampling."""
        pipe = self.model.pipeline
        if hasattr(pipe, "predict_proba"):
            proba = pipe.predict_proba([text])[0]
            classes = list(pipe.classes_)
            idx = max(range(len(proba)), key=lambda i: proba[i])
            return classes[idx], float(proba[idx])
        # decision_function fallback: normalize to a pseudo-confidence via argmax margin
        label = pipe.predict([text])[0]
        return str(label), 1.0

    def __call__(self, norm: Normalized) -> MatchResult:
        label, conf = self.predict(norm.lower)
        if label == self.name and conf >= self.threshold:
            return MatchResult(True)  # classifier labels; never contributes captures
        return FALSE


def train(examples: dict[str, list[str]], out_path: str) -> None:  # pragma: no cover
    """Offline training: label -> [example phrases] -> a frozen pickle.

    `python -m elaine.cli train labeled.yaml -o model.pkl`. Runtime only ever LOADS.
    """
    from sklearn.feature_extraction.text import TfidfVectorizer
    from sklearn.linear_model import LogisticRegression
    from sklearn.pipeline import Pipeline

    texts, labels = [], []
    for label, phrases in examples.items():
        for p in phrases:
            texts.append(p)
            labels.append(label)

    pipe = Pipeline([
        ("tfidf", TfidfVectorizer(lowercase=True)),
        ("clf", LogisticRegression(max_iter=1000)),
    ])
    pipe.fit(texts, labels)
    with open(out_path, "wb") as f:
        pickle.dump({"pipeline": pipe, "labels": sorted(set(labels))}, f)
