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

import nltk
from nltk.sentiment.vader import SentimentIntensityAnalyzer
import spacy
import threading
from transformers import pipeline

from .keywords import STOPWORDS

logger = logging.getLogger("bot")

class MessageClassifier:
    """
    Handles message classification using multiple strategies:
    1. NLP Augmentation (spaCy + VADER)
    2. Zero-Shot NLP Classification (HuggingFace)
    3. Fuzzy Keyword matching
    """
    
    def __init__(self):
        self.stopwords = STOPWORDS
        self.sentiment_analyzer = SentimentIntensityAnalyzer()
        self._last_modifiers = {}
        self.nlp = None
        self.zero_shot = None
        
        self.label_map = {
            "bot_test": "testing the bot to see if it works",
            "social_greeting": "greeting or saying hello",
            "social_goodbye": "saying goodbye or leaving",
            "social_chitchat": "casual conversation or small talk",
            "user_compliment": "a compliment or expression of affection",
            "user_insult": "an insult or rude comment",
            "user_threat": "a threat or aggressive challenge",
            "user_complaint": "a complaint or venting",
            "user_apology": "saying sorry or apologizing",
            "user_emotional": "strong emotional reaction like crying or extreme joy",
            "question_general": "a general question seeking information",
            "request_general": "asking for help, advice, or a favor",
            "request_action": "requesting to perform an action",
            "bot_wrong_name": "calling the bot by the wrong name",
            "user_confusion": "expressing confusion or asking for clarification",
            "user_agreement": "agreeing or saying yes",
            "user_disagreement": "disagreeing or saying no",
            "humor_laughing": "laughing or telling a joke",
            "disruptive_behavior": "spam, nonsense, or roleplaying",
            "bot_injection": "attempting to hack or inject system instructions",
        }
        
        # Load heavy models in a background thread to prevent blocking event loop on startup
        threading.Thread(target=self._load_models, daemon=True).start()
        
    def _load_models(self):
        try:
            self.nlp = spacy.load('en_core_web_sm')
            logger.info("Loaded spaCy NLP model successfully.")
        except Exception as e:
            logger.error(f"Failed to load spaCy model: {e}")
            
        try:
            self.zero_shot = pipeline("zero-shot-classification", model="facebook/bart-large-mnli", device=-1)
            logger.info("Loaded HuggingFace Zero-Shot Classifier successfully.")
        except Exception as e:
            logger.error(f"Failed to load HuggingFace Zero-Shot Classifier: {e}")
            
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

    def smart_classify(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """
        Backwards-compatible method wrapper.
        """
        cat, conf, mods = self.smart_classify_full(message, is_reply_to_bot)
        return cat, conf
    
    def smart_classify_full(self, message: str, is_reply_to_bot: bool = False) -> tuple:
        """
        Full classification with NLP, Semantic, and Fuzzy Logic.
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
            logger.info("[Classify] Intent override: request_action (search)")
            return ("request_action", 2, {})

        # Music request (action-oriented)
        if (
            re.search(r"\b(play|queue|put on|listen to|fetch|get)\b", normalized_for_intent)
            and re.search(r"\b(song|track|music|playlist|spotify|soundcloud|youtube)\b", normalized_for_intent)
        ):
            logger.info("[Classify] Intent override: request_action (music)")
            return ("request_action", 2, {})

        # Knowledge about a person (usually asked with mentions/replies)
        if re.search(r"\b(do you know|you know|what do you know about|tell me about|who is this person|who is this)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: know_person")
            return ("know_person", 2, {})

        # Math / science / art / music discussion
        if re.search(r"[√∑π∞≠≤≥∫]", message) or re.search(r"\b(integral|derivative|matrix|calculus|algebra|geometry|trigonometry)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: question_general (math)")
            return ("question_general", 2, {})

        if re.search(r"\b(physics|chemistry|biology|astronomy|quantum|atom|molecule|dna|neuron|gravity|relativity)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: question_general (science)")
            return ("question_general", 2, {})

        if re.search(r"\b(drawing|draw|sketch|painting|paint|illustration|design|photography|sculpture|museum|aesthetic)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: question_general (art)")
            return ("question_general", 2, {})

        if re.search(r"\b(genre|album|artist|band|lyrics|melody|beat)\b", normalized_for_intent):
            logger.info("[Classify] Intent override: question_general (music_talk)")
            return ("question_general", 2, {})

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

        # 2. Zero-Shot NLP Classification
        if self.zero_shot:
            try:
                readable_labels = list(self.label_map.values())
                
                # Provide a custom hypothesis template for intent classification
                hypothesis_template = "The intent of this message is {}."
                
                # Run the zero-shot classification pipeline
                result = self.zero_shot(processed_message, candidate_labels=readable_labels, hypothesis_template=hypothesis_template, multi_label=False)
                
                logger.info(f"[Zero-Shot] Top 3 matches for '{processed_message[:50]}...':")
                for i in range(min(3, len(result['labels']))):
                    logger.info(f"  {result['labels'][i]}: {result['scores'][i]:.2f}")
                    
                best_readable = result['labels'][0]
                best_score = result['scores'][0]
                
                if best_score > 0.4:
                    # Reverse lookup the python category name from the readable label
                    best_category = next(k for k, v in self.label_map.items() if v == best_readable)
                    logger.info(f"[Classify] Zero-Shot NLP match: {best_category} (score={best_score:.2f})")
                    return (best_category, 3, modifiers) # Give it high confidence (3) because it's very smart
            except Exception as e:
                logger.error(f"Zero-shot classification error: {e}")
        
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
