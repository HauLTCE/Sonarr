"""
Message classifier for the Sonarr bot.

Combines pattern matching, keyword classification, and text processing
for intelligent message categorization.
"""

import os
import json
import re
import hashlib
import logging
import pickle
from rapidfuzz import process, fuzz

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.svm import LinearSVC
from sklearn.pipeline import Pipeline
from sklearn.exceptions import NotFittedError

import nltk
from nltk.sentiment.vader import SentimentIntensityAnalyzer
import spacy
from sentence_transformers import SentenceTransformer, util
import torch

from .keywords import KEYWORD_MAP, STOPWORDS

logger = logging.getLogger("bot")


class ScikitLearnClassifier:
    """A robust TF-IDF + LinearSVC pipeline classifier to replace the custom Naive Bayes implementation."""
    
    def __init__(self, model_path="data/sklearn_model.pkl"):
        self.model_path = model_path
        self.pipeline = None
        self.total_docs = 0
        self.stopwords = STOPWORDS
        self.is_fitted = False
        
        # Keep track of raw training data for incremental updates
        self.training_texts = []
        self.training_labels = []

    def load_model(self) -> bool:
        """Load the pre-trained pipeline model from disk."""
        if not os.path.exists(self.model_path):
            return False
            
        try:
            with open(self.model_path, 'rb') as f:
                saved_data = pickle.load(f)
                self.pipeline = saved_data.get('pipeline')
                self.training_texts = saved_data.get('texts', [])
                self.training_labels = saved_data.get('labels', [])
                self.total_docs = len(self.training_texts)
                self.is_fitted = saved_data.get('is_fitted', False)
                
            logger.info(f"Loaded persistent sklearn ML model with {self.total_docs} docs.")
            return True
        except Exception as e:
            logger.error(f"Failed to load sklearn ML model: {e}")
            return False
            
    def save_model(self):
        """Save the current pipeline model state to disk."""
        try:
            os.makedirs(os.path.dirname(self.model_path), exist_ok=True)
            saved_data = {
                'pipeline': self.pipeline,
                'texts': self.training_texts,
                'labels': self.training_labels,
                'is_fitted': self.is_fitted
            }
            with open(self.model_path, 'wb') as f:
                pickle.dump(saved_data, f)
            logger.debug(f"Saved persistent sklearn ML model to {self.model_path}")
        except Exception as e:
            logger.error(f"Failed to save sklearn ML model: {e}")

    def add_example(self, text: str, category: str, auto_save: bool = True):
        """Add a single example."""
        self.training_texts.append(text)
        self.training_labels.append(category)
        self.total_docs += 1
        
        unique_classes = set(self.training_labels)
        if len(unique_classes) > 1:
            self._fit_pipeline()
            
        if auto_save:
            self.save_model()

    def _fit_pipeline(self):
        self.pipeline = Pipeline([
            ('tfidf', TfidfVectorizer(stop_words=list(self.stopwords))),
            ('clf', LinearSVC(class_weight='balanced', random_state=42))
        ])
        self.pipeline.fit(self.training_texts, self.training_labels)
        self.is_fitted = True

    def train(self, data: dict):
        """Train on a dictionary of {category: [list of text examples]} from scratch."""
        self.training_texts = []
        self.training_labels = []
        self.total_docs = 0
        
        for category, examples in data.items():
            for text in examples:
                self.training_texts.append(text)
                self.training_labels.append(category)
                self.total_docs += 1
                
        unique_classes = set(self.training_labels)
        if len(unique_classes) > 1:
            self._fit_pipeline()
                
        self.save_model()
        logger.info(f"Trained & saved sklearn model with {self.total_docs} examples across {len(unique_classes)} classes.")

    def predict(self, text: str) -> tuple:
        """Return (best_category, score/confidence)."""
        if not self.is_fitted or self.total_docs == 0 or not self.pipeline:
            return None, 0
            
        try:
            distances = self.pipeline.decision_function([text])[0]
            if hasattr(distances, '__iter__'):
                max_dist = max(distances)
                best_idx = distances.argmax()
                prediction = self.pipeline.classes_[best_idx]
            else:
                max_dist = abs(distances)
                prediction = self.pipeline.predict([text])[0]

            prediction_str = str(prediction)
            
            # LinearSVC decision function returns distance to the hyperplane.
            # Usually > 0.5 is very confident. < 0.2 is essentially a guess.
            if max_dist < 0.3:
                return None, 0
                
            confidence = 2 if max_dist >= 0.6 else 1
            
            return prediction_str, confidence
        except Exception as e:
            logger.error(f"Error predicting with sklearn model: {e}")
            return None, 0


class MessageClassifier:
    """
    Handles message classification using multiple strategies:
    1. Local ML Model (TF-IDF + SVM)
    2. Semantic Zero-Shot (SentenceTransformers)
    3. NLP Augmentation (spaCy + VADER)
    4. Fuzzy Keyword matching
    """
    
    def __init__(self):
        self.keyword_map = KEYWORD_MAP
        self.stopwords = STOPWORDS
        self.ml_classifier = ScikitLearnClassifier()
        self.sentiment_analyzer = SentimentIntensityAnalyzer()
        self._last_modifiers = {}
        
        try:
            self.nlp = spacy.load('en_core_web_sm')
            logger.info("Loaded spaCy NLP model successfully.")
        except Exception as e:
            logger.error(f"Failed to load spaCy model: {e}")
            self.nlp = None
            
        try:
            self.embedder = SentenceTransformer('all-MiniLM-L6-v2')
            logger.info("Loaded Semantic Embedder successfully.")
        except Exception as e:
            logger.error(f"Failed to load SentenceTransformer: {e}")
            self.embedder = None
            
        self._init_ml_model()
        
    def _init_ml_model(self):
        """Load persistent model if exists, otherwise train from scratch if json data exists."""
        if not self.ml_classifier.load_model():
            logger.info("No persistent ML model found. Checking for initial training data...")
            data_path = "data/training_data.json"
            if os.path.exists(data_path):
                try:
                    with open(data_path, 'r', encoding='utf-8') as f:
                        training_data = json.load(f)
                    self.ml_classifier.train(training_data)
                except Exception as e:
                    logger.error(f"Failed to load initial training data: {e}")
            else:
                logger.warning(f"No initial training data found at {data_path}. ML classifier will be empty.")
    
    def normalize_message(self, text: str) -> str:
        """Normalize message for consistent hashing."""
        text = text.lower().strip()
        text = re.sub(r'[^\w\s]', '', text)
        text = re.sub(r'\s+', ' ', text)
        return text
    
    def extract_content_words(self, text: str) -> list:
        """Extract meaningful content words (remove stopwords) for smart caching."""
        normalized = self.normalize_message(text)
        words = normalized.split()
        content_words = [w for w in words if w not in self.stopwords and len(w) > 1]
        return sorted(content_words)
    
    def hash_message(self, text: str) -> str:
        """Create a hash of content words for smart cache matching."""
        content_words = self.extract_content_words(text)
        if not content_words:
            normalized = self.normalize_message(text)
            return hashlib.md5(normalized.encode('utf-8')).hexdigest()[:16]
        content_str = ' '.join(content_words)
        return hashlib.md5(content_str.encode('utf-8')).hexdigest()[:16]
    
    def count_words(self, text: str) -> int:
        """Count words in message."""
        normalized = self.normalize_message(text)
        return len(normalized.split())
    
    def preprocess_quirks(self, text: str) -> str:
        """
        Handle typing quirks to prevent simple filter bypasses.
        """
        text = text.lower()
        text = re.sub(r'(?<=[a-z])\.(?=[a-z])', '', text)
        text = re.sub(r'(.)\1{2,}', r'\1\1', text)
        return text

    def expand_contractions(self, text: str) -> str:
        """Expand a small set of common contractions relevant for intent detection."""
        # Keep this intentionally small + deterministic.
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

    def keyword_classify(self, message: str, return_score: bool = False):
        """
        Try to classify message using keywords with RapidFuzz for fuzzy matching.
        """
        # Normalize for more consistent keyword matching (punctuation/contractions)
        text = self.expand_contractions(message.lower())
        text = re.sub(r"[^\w\s]", " ", text)
        text = re.sub(r"\s+", " ", text).strip()
        words = text.split()
        scores = {}
        
        for category, keywords in self.keyword_map.items():
            score = 0
            for kw in keywords:
                if ' ' in kw:
                    if fuzz.partial_ratio(kw, text) > 85:
                        score += 1
                elif not kw.isalnum():
                    if kw in text:
                        score += 1
                else:
                    best_match = process.extractOne(kw, words, scorer=fuzz.ratio)
                    if best_match and best_match[1] > 85:
                        score += 1
                        
            if score > 0:
                scores[category] = score
        
        if scores:
            best = max(scores, key=scores.get)
            if return_score:
                return best, scores[best]
            if scores[best] >= 1:
                return best
        
        return (None, 0) if return_score else None
    
    def smart_classify(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """
        Backwards-compatible method wrapper.
        """
        cat, conf, mods = self.smart_classify_full(message, is_reply_to_bot)
        return cat, conf
    
    def smart_classify_full(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """
        Full classification with NLP, ML, Semantic, and Fuzzy Logic.
        Replaces all legacy regex pattern matching.
        """
        logger.info(f"[Classify] Input: '{message[:100]}{'...' if len(message) > 100 else ''}'")
        
        processed_message = self.preprocess_quirks(message)
        processed_message = self.expand_contractions(processed_message)
        if is_reply_to_bot and not re.search(r"\b(you|u|ur|your|yours|bot|sonar|sonarr)\b", processed_message):
            processed_message = "bot, " + processed_message

        # 0. Fast intent overrides (cheap + fixes common misses)
        normalized_for_intent = self.normalize_message(processed_message)

        # Math-ish questions like: "what's 1 + 1" or "2*8"
        if re.search(r"\d+\s*[\+\-\*/]\s*\d+", message):
            logger.info("[Classify] Intent override: question (math)")
            return ("question_general", 2, {})

        # Definition / lookup
        if re.search(r"\b(define|meaning of|what is|who is|what does)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: request_search")
            return ("request_search", 2, {})

        # Music request (action-oriented)
        if (
            re.search(r"\b(play|queue|put on|listen to|fetch|get)\b", normalized_for_intent)
            and re.search(r"\b(song|track|music|playlist|spotify|soundcloud|youtube)\b", normalized_for_intent)
        ):
            logger.info("[Classify] Intent override: request_music")
            return ("request_music", 2, {})

        # Knowledge about a person (usually asked with mentions/replies)
        if re.search(r"\b(do you know|you know|what do you know about|tell me about|who is this person|who is this)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: know_person")
            return ("know_person", 2, {})

        # Math / science / art / music discussion
        if re.search(r"[√∑π∞≠≤≥∫]", message) or re.search(r"\b(integral|derivative|matrix|calculus|algebra|geometry|trigonometry)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: math")
            return ("topic_math", 2, {})

        if re.search(r"\b(physics|chemistry|biology|astronomy|quantum|atom|molecule|dna|neuron|gravity|relativity)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: science")
            return ("topic_science", 2, {})

        if re.search(r"\b(drawing|draw|sketch|painting|paint|illustration|design|photography|sculpture|museum|aesthetic)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: art")
            return ("topic_art", 2, {})

        if re.search(r"\b(genre|album|artist|band|lyrics|melody|beat)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: music_talk")
            return ("topic_music", 2, {})

        if re.search(r"\b(blame|flame|roast|drag|call out|expose|cook)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: roast_someone")
            return ("roast_someone", 2, {})

        # 1. NLP Sentiment & Extraction
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
        
        self._last_modifiers = modifiers

        # 2. Local ML Model (TF-IDF + SVM)
        ml_cat, ml_conf = self.ml_classifier.predict(processed_message)
        if ml_cat:
            logger.info(f"[Classify] ML match: {ml_cat} (conf={ml_conf})")
            return (ml_cat, ml_conf, modifiers)
            
        # 3. Zero-Shot Semantic Similarity
        if self.embedder and self.keyword_map:
            categories = list(self.keyword_map.keys())
            if categories:
                msg_emb = self.embedder.encode(processed_message, convert_to_tensor=True)
                cat_embs = self.embedder.encode(categories, convert_to_tensor=True)
                cosine_scores = util.cos_sim(msg_emb, cat_embs)[0]
                best_score_idx = torch.argmax(cosine_scores).item()
                best_score = cosine_scores[best_score_idx].item()
                
                if best_score > 0.4:
                    semantic_cat = categories[best_score_idx]
                    logger.info(f"[Classify] Semantic match: {semantic_cat} (score={best_score:.2f})")
                    return (semantic_cat, 2, modifiers)
        
        # 4. Fuzzy Keyword Match Fallback
        keyword_cat, keyword_score = self.keyword_classify(message, return_score=True)
        if keyword_cat:
            confidence = 2 if keyword_score >= 2 else 1
            logger.info(f"[Classify] Fuzzy Keyword match: {keyword_cat} (conf={confidence})")
            return (keyword_cat, confidence, modifiers)
        
        logger.info(f"[Classify] No match for: '{message[:50]}'")
        return (None, 0, modifiers)
    
    def classify(self, message: str, min_confidence: int = 1) -> str | None:
        category, confidence, mods = self.smart_classify_full(message)
        if confidence >= min_confidence:
            return category
        return None


# ================== PRONOUN & MISGENDERING UTILS ==================

def detect_misgendering(text: str) -> str | None:
    """Check if the user is using masculine terms to refer to Sonarr."""
    text_lower = text.lower()
    
    masculine_terms = {
        "bro": r"\b(bro|broski|brotha|brother)\b",
        "dude": r"\b(dude|duude|duuude)\b",
        "man": r"\b(man|maan|maaan)\b",
        "guy": r"\b(guy|guuy)\b",
        "sir": r"\b(sir|sire)\b",
        "him": r"\b(him)\b",
        "he": r"\b(he|hes|he's)\b",
        "his": r"\b(his)\b",
        "boy": r"\b(boy|boi|boii|boiii)\b",
        "king": r"\b(king)\b",
        "mister": r"\b(mister|mr)\b",
    }
    
    for term_category, pattern in masculine_terms.items():
        if re.search(pattern, text_lower):
            # Exclude third-party context (e.g., "my brother")
            third_party_pattern = r"(my|your|his|her|their|the|a|that|this)\s+" + pattern.replace(r"\b", "")
            if not re.search(third_party_pattern, text_lower):
                return term_category
    return None

def detect_correct_pronouns(text: str) -> bool:
    """Check if the user is using correct feminine pronouns to refer to Sonarr."""
    text_lower = text.lower()
    feminine_patterns = [
        r"\b(she|her|hers|herself)\b",
        r"\b(queen|girl|woman|lady|miss|ma'am|maam|ms)\b",
        r"\b(sis|sister|girly)\b",
    ]
    for pattern in feminine_patterns:
        if re.search(pattern, text_lower):
            third_party_pattern = r"(my|your|his|her|their|the|a|that|this)\s+" + pattern.replace(r"\b", "")
            if not re.search(third_party_pattern, text_lower):
                return True
    return False

def extract_third_party_subject(text: str) -> str | None:
    """Extract a third-party subject using regex (fallback)."""
    match = re.search(r"\b(he|she|they|him|her|them|someone|somebody|everyone|everybody|people|person|anyone|anybody)\b", text.lower())
    if match:
        return match.group(0)
    match = re.search(r"\b(my|your|his|her|our|their|the|a|an|that|this)\s+([a-zA-Z]+)\b", text.lower())
    if match:
        return match.group(0)
    return None


_classifier_instance = None

def get_classifier() -> MessageClassifier:
    """Get or create the singleton MessageClassifier instance."""
    global _classifier_instance
    if _classifier_instance is None:
        _classifier_instance = MessageClassifier()
    return _classifier_instance
