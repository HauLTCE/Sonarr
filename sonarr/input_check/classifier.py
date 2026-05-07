"""
Message classifier for the Sonarr bot.

Two-tier classification architecture:
  Tier 1 (Retrieval): Sentence embeddings — fast cosine similarity, picks top 10 candidates
  Tier 2 (Verification): Three verifier models run concurrently on the top 10
    - facebook/bart-large-mnli          (encoder-decoder, MNLI)
    - MoritzLaurer/DeBERTa-v3-base-mnli-fever-anli (disentangled attention, MNLI+FEVER+ANLI)
    - roberta-large-mnli                (standard transformer, MNLI)
  
  All model inference runs async via run_in_executor to avoid blocking the event loop.
"""

import os
import re
import logging
import asyncio
import threading
from concurrent.futures import ThreadPoolExecutor

import nltk
from nltk.sentiment.vader import SentimentIntensityAnalyzer
import spacy
import numpy as np
from sentence_transformers import SentenceTransformer, CrossEncoder, util
from transformers import pipeline as hf_pipeline
from rank_bm25 import BM25Okapi

from .keywords import STOPWORDS

logger = logging.getLogger("bot")

# Dedicated thread pool for ML inference (prevents blocking the Discord event loop)
_ml_executor = ThreadPoolExecutor(max_workers=3, thread_name_prefix="ml-inference")


class MessageClassifier:
    """
    Two-tier message classification engine.

    Tier 1 — Retrieval (Sentence-Transformers):
        Embeds the message, computes cosine similarity against all 120+ label
        embeddings, returns the top 15 candidates. Sub-millisecond, O(1).

    Tier 2 — Verification (3 models, concurrent):
        Each verifier runs on the top 10 candidates concurrently and returns
        (best_category, score). The final decision merges all verifier votes.
        Models: BART-MNLI, DeBERTa-v3-base, RoBERTa-large-mnli.
    """

    # ── Configuration ────────────────────────────────────────────────
    TIER1_TOP_N = 5               # Number of candidates from embedding retrieval
    TIER2_MIN_SCORE = 0.3          # Minimum verifier score to be considered
    TIER2_OVERRIDE_SCORE = 0.7     # Verifier must beat this to override embedding
    EMBED_ONLY_THRESHOLD = 0.35    # Min embedding score when no verifier is available
    EMBED_EARLY_EXIT = 0.55        # Skip Tier 2 entirely if embedding is this confident
    TIER2_CANDIDATE_FLOOR = 0.2    # Don't send candidates below this to verifiers
    # ─────────────────────────────────────────────────────────────────

    def __init__(self):
        self.stopwords = STOPWORDS
        self.sentiment_analyzer = SentimentIntensityAnalyzer()
        self._last_modifiers = {}
        self.nlp = None

        # Tier 1: Embedding model
        self.embedder = None
        self.label_embeddings = None
        self.label_names = []
        self.label_descriptions = []
        self.bm25 = None
        self.cross_encoder = None

        # Tier 2: Verifier models (list — supports multiple)
        self._verifiers = []  # List of (name, callable) pairs

        # Load labels dynamically from response files
        self._discover_labels()

        # Load heavy models in a background thread
        threading.Thread(target=self._load_models, daemon=True).start()

    # ── Label Discovery ──────────────────────────────────────────────

    def _discover_labels(self):
        """Auto-discover category labels from response file LABEL metadata."""
        try:
            from sonarr.responses import COLD_LABELS
            self.label_names = list(COLD_LABELS.keys())
            self.label_descriptions = list(COLD_LABELS.values())
            logger.info(f"Auto-discovered {len(self.label_names)} category labels from response files.")
        except ImportError:
            logger.warning("Could not import COLD_LABELS, falling back to empty label set.")
            self.label_names = []
            self.label_descriptions = []

    # ── Model Loading ────────────────────────────────────────────────

    def _load_models(self):
        """Load all ML models (runs in background thread on init)."""
        # spaCy
        try:
            self.nlp = spacy.load('en_core_web_sm')
            logger.info("Loaded spaCy NLP model successfully.")
        except Exception as e:
            logger.error(f"Failed to load spaCy model: {e}")

        # Tier 1: Sentence embeddings & BM25
        try:
            self.embedder = SentenceTransformer('all-MiniLM-L6-v2')
            if self.label_descriptions:
                self.label_embeddings = self.embedder.encode(
                    self.label_descriptions, convert_to_tensor=True
                )
                
                # Initialize BM25
                tokenized_corpus = [doc.lower().split() for doc in self.label_descriptions]
                self.bm25 = BM25Okapi(tokenized_corpus)
                
            logger.info("Loaded sentence-transformers embedder, BM25, and pre-computed label embeddings.")
        except Exception as e:
            logger.error(f"Failed to load sentence-transformers or BM25: {e}")

        # Tier 1.5: Cross-Encoder
        try:
            self.cross_encoder = CrossEncoder('cross-encoder/ms-marco-MiniLM-L-6-v2')
            logger.info("Loaded Cross-Encoder (ms-marco-MiniLM-L-6-v2).")
        except Exception as e:
            logger.error(f"Failed to load Cross-Encoder: {e}")

        # Tier 2: Load all three verifier models
        tier2_models = [
            ("BART",    "facebook/bart-large-mnli"),
            ("DeBERTa", "MoritzLaurer/DeBERTa-v3-base-mnli-fever-anli"),
            ("RoBERTa", "roberta-large-mnli"),
        ]
        for name, model_id in tier2_models:
            try:
                model = hf_pipeline("zero-shot-classification", model=model_id, device=-1)
                self._verifiers.append((name, model))
                logger.info(f"Loaded Tier 2 verifier: {name} ({model_id})")
            except Exception as e:
                logger.error(f"Failed to load Tier 2 verifier {name} ({model_id}): {e}")

    # ── Text Processing ──────────────────────────────────────────────

    def normalize_message(self, text: str) -> str:
        """Normalize message for consistent hashing."""
        text = text.lower().strip()
        text = re.sub(r'[^\w\s]', '', text)
        text = re.sub(r'\s+', ' ', text)
        return text

    def count_words(self, text: str) -> int:
        """Count words in message."""
        normalized = self.normalize_message(text)
        return len(normalized.split())

    def preprocess_quirks(self, text: str) -> str:
        """Handle typing quirks to prevent simple filter bypasses."""
        text = text.lower()
        text = re.sub(r'(?<=[a-z])\.(?=[a-z])', '', text)
        text = re.sub(r'(.)\1{2,}', r'\1\1', text)
        return text

    def expand_contractions(self, text: str) -> str:
        """Expand a small set of common contractions relevant for intent detection."""
        replacements = {
            "what's": "what is",
            "whats": "what is",
            "who's": "who is",
            "whos": "who is",
            "it's": "it is",
            "im": "i am",
            "i'm": "i am",
            "can't": "cannot",
            "dont": "do not",
            "don't": "do not",
        }

        out = text
        for k, v in replacements.items():
            out = re.sub(rf"\b{re.escape(k)}\b", v, out, flags=re.IGNORECASE)
        return out

    # ── Tier 1: Hybrid Retrieval (Embedding + Keywords) ────────────

    def _keyword_scores(self, message: str) -> dict:
        """
        Compute keyword match scores for all categories.
        Returns {category_name: score} where score is 0.0-1.0.
        Fast — just string matching, no ML.
        """
        from .keywords import KEYWORD_MAP

        words = set(self.normalize_message(message).split()) - self.stopwords
        msg_lower = message.lower()
        scores = {}

        for category, keywords in KEYWORD_MAP.items():
            if category not in self.label_names:
                continue  # Only score categories that have response files

            hits = 0
            for kw in keywords:
                # Multi-word keywords: check substring
                if " " in kw:
                    if kw in msg_lower:
                        hits += 1.5  # Multi-word matches are more specific → extra weight
                else:
                    if kw in words:
                        hits += 1.0

            if hits > 0:
                # Normalize: cap at 1.0, scale so 2+ hits gives a strong signal
                scores[category] = min(1.0, hits / 2.0)

        return scores

    def _embedding_retrieve(self, message: str) -> list:
        """
        Tier 1: Keyword-boosted hybrid retrieval.

        Combines embedding cosine similarity with keyword match scores:
          hybrid_score = (1 - α) × embedding_score + α × keyword_score
          where α = 0.3 (keyword weight)

        This ensures categories with strong keyword signals get into the top N
        even if the embedding model alone would have ranked them lower.
        Returns top N (category_name, hybrid_score) pairs.
        """
        if not self.embedder or self.label_embeddings is None:
            return []

        # Embedding scores (fast cosine similarity)
        msg_embedding = self.embedder.encode(message, convert_to_tensor=True)
        scores = util.cos_sim(msg_embedding, self.label_embeddings)[0]
        scores_np = scores.cpu().numpy()

        # Keyword scores (instant string matching)
        kw_scores = self._keyword_scores(message)

        # BM25 scores
        bm25_scores = np.zeros(len(self.label_names))
        if self.bm25:
            tokenized_query = message.lower().split()
            raw_bm25 = self.bm25.get_scores(tokenized_query)
            max_bm25 = np.max(raw_bm25) if len(raw_bm25) > 0 else 0
            if max_bm25 > 0:
                bm25_scores = raw_bm25 / max_bm25

        # Blend: hybrid = 0.5 × embedding + 0.3 × bm25 + 0.2 × keyword
        EMBED_WEIGHT = 0.5
        BM25_WEIGHT = 0.3
        KEYWORD_WEIGHT = 0.2
        
        hybrid_scores = np.zeros(len(self.label_names))
        for i, name in enumerate(self.label_names):
            kw = kw_scores.get(name, 0.0)
            hybrid_scores[i] = (
                EMBED_WEIGHT * scores_np[i]
                + BM25_WEIGHT * bm25_scores[i]
                + KEYWORD_WEIGHT * kw
            )

        # Pick top N by hybrid score
        top_indices = np.argsort(hybrid_scores)[::-1][:self.TIER1_TOP_N]

        results = []
        for idx in top_indices:
            results.append((self.label_names[idx], float(hybrid_scores[idx])))
        return results

    def _cross_encoder_rerank(self, message: str, candidates: list) -> list:
        """
        Tier 1.5: Cross-Encoder Re-ranking.
        Takes top N from Tier 1, pairs them with the query, and scores them.
        Returns re-ranked candidates with sigmoid normalized scores.
        """
        if not self.cross_encoder or not candidates:
            return candidates

        try:
            # Prepare pairs: (message, category_description)
            pairs = []
            for name, _ in candidates:
                idx = self.label_names.index(name)
                desc = self.label_descriptions[idx]
                pairs.append((message, desc))

            # Score pairs (returns logits)
            logits = self.cross_encoder.predict(pairs)
            
            # Apply sigmoid to normalize to 0.0 - 1.0
            sigmoid_scores = 1 / (1 + np.exp(-np.array(logits)))

            # Reconstruct and sort candidates
            reranked = []
            for i, (name, _) in enumerate(candidates):
                reranked.append((name, float(sigmoid_scores[i])))

            reranked.sort(key=lambda x: x[1], reverse=True)
            # Return top 5 to keep Tier 2 fast
            return reranked[:5]
        except Exception as e:
            logger.error(f"Cross-encoder error: {e}")
            return candidates

    # ── Tier 2: Verifier Models ──────────────────────────────────────

    def _run_verifier(self, verifier_fn, message: str, candidates: list) -> tuple:
        """
        Run a single zero-shot verifier on the candidate list.
        Returns (best_category, confidence_score).
        Runs synchronously — meant to be called via run_in_executor.
        """
        if not candidates:
            return (None, 0.0)

        try:
            candidate_descriptions = [
                self.label_descriptions[self.label_names.index(name)]
                for name, _ in candidates
            ]
            hypothesis_template = "This is an example of {}."

            result = verifier_fn(
                message,
                candidate_labels=candidate_descriptions,
                hypothesis_template=hypothesis_template,
                multi_label=False
            )

            best_readable = result['labels'][0]
            best_score = result['scores'][0]

            # Map back to category name
            desc_to_name = {
                self.label_descriptions[self.label_names.index(name)]: name
                for name, _ in candidates
            }
            best_category = desc_to_name.get(best_readable)

            return (best_category, best_score)
        except Exception as e:
            logger.error(f"Verifier error: {e}")
            if candidates:
                return candidates[0]
            return (None, 0.0)

    async def _run_tier2_async(self, message: str, candidates: list) -> list:
        """
        Run all Tier 2 verifiers concurrently via thread executors.
        Returns a list of (verifier_name, best_category, score) results.
        """
        if not self._verifiers:
            return []

        loop = asyncio.get_running_loop()

        # Launch all verifiers concurrently in the ML thread pool
        tasks = []
        for name, verifier_fn in self._verifiers:
            future = loop.run_in_executor(
                _ml_executor,
                self._run_verifier, verifier_fn, message, candidates
            )
            tasks.append((name, future))

        # Await all concurrently
        results = []
        for name, future in tasks:
            try:
                category, score = await future
                results.append((name, category, score))
            except Exception as e:
                logger.error(f"[Tier2] {name} failed: {e}")

        return results

    def _resolve_tier2_votes(self, top_candidates: list, verifier_results: list) -> tuple:
        """
        Merge Tier 2 verifier votes with Tier 1/1.5 embedding/cross-encoder result.
        
        Strategy: Weighted Final Score
        Final Score = (Embedder Score * 0.4) + (Verifier Confidence * 0.6)
        
        Returns (category, confidence, log_reason).
        """
        embed_category = top_candidates[0][0]
        embed_score = top_candidates[0][1]
        
        embed_scores_dict = {name: score for name, score in top_candidates}

        # Filter to viable results
        viable = [(name, cat, score) for name, cat, score in verifier_results
                   if cat and score > self.TIER2_MIN_SCORE]

        if not viable:
            # No verifier had a strong opinion → trust embedding
            return (embed_category, embed_score, "no verifier above threshold")

        best_final_score = embed_score * 0.4
        best_category = embed_category
        best_reason = f"Embed wins (no strong verifier blend): {embed_category} ({best_final_score:.3f})"

        for v_name, v_cat, v_score in viable:
            e_score = embed_scores_dict.get(v_cat, 0.0)
            blended = (e_score * 0.4) + (v_score * 0.6)
            
            if blended > best_final_score:
                best_final_score = blended
                best_category = v_cat
                best_reason = f"BLENDED ({v_name}): {v_cat} | embed={e_score:.3f}, verifier={v_score:.3f} -> final={blended:.3f}"

        return (best_category, best_final_score, best_reason)

    # ── Main Classification ──────────────────────────────────────────

    async def async_classify(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """
        Full async classification pipeline.
        
        Tier 1: Embedding retrieval (in executor)
        Tier 2: Verifier models (concurrent, in executor)
        
        Returns (category, confidence, modifiers).
        """
        logger.info(f"[Classify] Input: '{message[:100]}{'...' if len(message) > 100 else ''}'")

        processed_message = self.preprocess_quirks(message)
        processed_message = self.expand_contractions(processed_message)
        if is_reply_to_bot and not re.search(r"\b(you|u|ur|your|yours|bot|sonar|sonarr)\b", processed_message):
            processed_message = "bot, " + processed_message

        # 0. Fast intent overrides (no ML needed — instant)
        normalized_for_intent = self.normalize_message(processed_message)

        if re.search(r"\d+\s*[\+\-\*/]\s*\d+", message):
            return ("question_general", 2, {})

        if re.search(r"^(define|meaning of|what does)\b", normalized_for_intent):
            return ("request_action", 2, {})

        if re.search(r"\b(ignore all previous|ignore prior|disregard all|you are now|act as if|pretend you are|system prompt|override instructions|new instructions|forget everything)\b", normalized_for_intent):
            return ("bot_injection", 2, {})

        if (
            re.search(r"\b(play|queue|put on|listen to|fetch|get)\b", normalized_for_intent)
            and re.search(r"\b(song|track|music|playlist|spotify|soundcloud|youtube)\b", normalized_for_intent)
        ):
            return ("request_action", 2, {})

        if re.search(r"\b(do you know|you know|what do you know about|tell me about|who is this person|who is this)\b", normalized_for_intent):
            return ("question_general", 2, {})

        if re.search(r"[√∑π∞≠≤≥∫]", message) or re.search(r"\b(integral|derivative|matrix|calculus|algebra|geometry|trigonometry)\b", normalized_for_intent):
            return ("question_general", 2, {})

        if re.search(r"\b(physics|chemistry|biology|astronomy|quantum|atom|molecule|dna|neuron|gravity|relativity)\b", normalized_for_intent):
            return ("question_general", 2, {})

        if re.search(r"\b(drawing|draw|sketch|painting|paint|illustration|design|photography|sculpture|museum|aesthetic)\b", normalized_for_intent):
            return ("question_general", 2, {})

        if re.search(r"\b(genre|album|artist|band|lyrics|melody|beat)\b", normalized_for_intent):
            return ("question_general", 2, {})

        if re.search(r"\b(blame|flame|roast|drag|call out|expose|cook)\b", normalized_for_intent):
            return ("user_insult", 2, {})

        # 1. NLP Sentiment & Extraction (lightweight — run inline)
        sentiment = self.sentiment_analyzer.polarity_scores(processed_message)
        if sentiment['compound'] >= 0.05:
            sentiment_label = "positive"
        elif sentiment['compound'] <= -0.05:
            sentiment_label = "negative"
        else:
            sentiment_label = "neutral"

        entities = {}
        root_verb = None
        if self.nlp:
            doc = self.nlp(processed_message)
            entities = {ent.label_: ent.text for ent in doc.ents}
            for token in doc:
                if token.dep_ == "ROOT" and token.pos_ == "VERB":
                    root_verb = token.lemma_
                    break

        modifiers = {
            'sentiment': sentiment,
            'sentiment_label': sentiment_label,
            'entities': entities,
            'root_verb': root_verb
        }

        # 2. Tier 1: Embedding retrieval (async)
        if self.embedder and self.label_embeddings is not None:
            loop = asyncio.get_running_loop()
            top_candidates = await loop.run_in_executor(
                _ml_executor, self._embedding_retrieve, processed_message
            )

            if top_candidates:
                # 2.5: Tier 1.5 Cross-Encoder re-ranking
                if self.cross_encoder:
                    top_candidates = await loop.run_in_executor(
                        _ml_executor, self._cross_encoder_rerank, processed_message, top_candidates
                    )

                embed_category = top_candidates[0][0]
                embed_score = top_candidates[0][1]

                # Early exit: embedding is very confident → skip Tier 2 entirely
                if embed_score >= self.EMBED_EARLY_EXIT:
                    logger.info(f"[Classify] Early exit (embed={embed_score:.3f}): {embed_category}")
                    return (embed_category, 3, modifiers)

                # 3. Tier 2: Run all verifiers concurrently (async)
                if self._verifiers:
                    # Filter out weak candidates before sending to verifiers
                    strong_candidates = [
                        (name, score) for name, score in top_candidates
                        if score >= self.TIER2_CANDIDATE_FLOOR
                    ]
                    if not strong_candidates:
                        strong_candidates = top_candidates[:1]  # Always keep at least the top pick

                    verifier_results = await self._run_tier2_async(
                        processed_message, strong_candidates
                    )

                    if verifier_results:
                        final_cat, final_score, reason = self._resolve_tier2_votes(
                            top_candidates, verifier_results
                        )
                        logger.info(f"[Classify] {reason}")
                        return (final_cat, 3, modifiers)

                # No verifiers available — embedding-only fallback
                if embed_score > self.EMBED_ONLY_THRESHOLD:
                    logger.info(f"[Classify] Embedding-only match: {embed_category} (score={embed_score:.3f})")
                    return (embed_category, 3, modifiers)

        return (None, 0, modifiers)

    # ── Legacy sync wrapper (kept for backward compat) ───────────────

    def smart_classify_full(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """
        Sync wrapper around async_classify for backward compatibility.
        NOTE: This blocks — prefer async_classify from async code.
        """
        try:
            loop = asyncio.get_running_loop()
        except RuntimeError:
            loop = None

        if loop and loop.is_running():
            # We're inside an async context — can't use asyncio.run()
            # Fall back to synchronous embedding-only
            return self._sync_classify_fallback(message, is_reply_to_bot)
        else:
            return asyncio.run(self.async_classify(message, is_reply_to_bot))

    def _sync_classify_fallback(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """Synchronous fallback that skips async Tier 2 (used when called from running loop)."""
        logger.info(f"[Classify] Input: '{message[:100]}{'...' if len(message) > 100 else ''}'")

        processed_message = self.preprocess_quirks(message)
        processed_message = self.expand_contractions(processed_message)
        if is_reply_to_bot and not re.search(r"\b(you|u|ur|your|yours|bot|sonar|sonarr)\b", processed_message):
            processed_message = "bot, " + processed_message

        normalized_for_intent = self.normalize_message(processed_message)

        # Fast regex overrides (same as async version)
        if re.search(r"\d+\s*[\+\-\*/]\s*\d+", message):
            return ("question_general", 2, {})
        if re.search(r"^(define|meaning of|what does)\b", normalized_for_intent):
            return ("request_action", 2, {})
        if re.search(r"\b(ignore all previous|ignore prior|disregard all|you are now|act as if|pretend you are|system prompt|override instructions|new instructions|forget everything)\b", normalized_for_intent):
            return ("bot_injection", 2, {})

        # Embedding only
        if self.embedder and self.label_embeddings is not None:
            candidates = self._embedding_retrieve(processed_message)
            if self.cross_encoder and candidates:
                candidates = self._cross_encoder_rerank(processed_message, candidates)
            if candidates and candidates[0][1] > self.EMBED_ONLY_THRESHOLD:
                logger.info(f"[Classify] Sync fallback: {candidates[0][0]} ({candidates[0][1]:.3f})")
                return (candidates[0][0], 3, {})

        return (None, 0, {})


_classifier_instance = None

def get_classifier() -> MessageClassifier:
    """Get or create the singleton MessageClassifier instance."""
    global _classifier_instance
    if _classifier_instance is None:
        _classifier_instance = MessageClassifier()
    return _classifier_instance
