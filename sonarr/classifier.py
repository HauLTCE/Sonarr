"""
Message classifier for the Sonarr bot.

Combines pattern matching, keyword classification, and text processing
for intelligent message categorization.
"""

import re
import hashlib
import logging

from .patterns import pattern_match, pattern_match_simple
from .keywords import KEYWORD_MAP, STOPWORDS

logger = logging.getLogger("bot")


class MessageClassifier:
    """
    Handles message classification using multiple strategies:
    1. Context-aware pattern matching (Subject-Action-Target)
    2. Keyword-based classification
    3. Text normalization and hashing for caching
    """
    
    def __init__(self):
        self.keyword_map = KEYWORD_MAP
        self.stopwords = STOPWORDS
    
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
        1. Reduces repeated letters to a max of 2.
        2. Removes periods often used to bypass filters (e.g. s.t.u.p.i.d)
        """
        text = text.lower()
        # Remove arbitrary periods used as bypasses
        text = re.sub(r'(?<=[a-z])\.(?=[a-z])', '', text)
        # Reduce repeated characters (3 or more) down to 2
        text = re.sub(r'(.)\1{2,}', r'\1\1', text)
        return text

    def keyword_classify(self, message: str, return_score: bool = False):
        """
        Try to classify message using keywords.
        
        Args:
            message: The message text to classify
            return_score: If True, returns (category, score) tuple
            
        Returns:
            category name, or (category, score) if return_score=True,
            or None/tuple(None, 0) if no match
        """
        text = message.lower()
        scores = {}
        
        for category, keywords in self.keyword_map.items():
            score = 0
            for kw in keywords:
                # Use word boundary matching to avoid "yo" matching "you"
                if ' ' in kw:
                    # Multi-word phrases: simple substring is fine
                    if kw in text:
                        score += 1
                elif not kw.isalnum():
                    # Emojis or punctuation-heavy slang: no word boundaries
                    if kw in text:
                        score += 1
                else:
                    # Single alphanumeric words: need safer boundary check than \b
                    pattern = r'(?:^|\W)' + re.escape(kw) + r'(?:$|\W)'
                    if re.search(pattern, text):
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
        Smart classification combining pattern matching and keyword counting.
        
        Priority:
        1. Complex patterns (highest priority) - confidence 3+
        2. Dominant keywords (2+ matches) - confidence 2
        3. Single keyword match - confidence 1
        
        Args:
            message: The message text to classify
            is_reply_to_bot: Whether the message is a direct reply to the bot
            
        Returns:
            Tuple of (category, confidence) where confidence is 0-4
        """
        logger.info(f"[Classify] Input: '{message[:100]}{'...' if len(message) > 100 else ''}'")
        
        # Pre-process message to neutralize typing quirks
        processed_message = self.preprocess_quirks(message)
        
        # If the user is replying directly to the bot, implicitly add a target anchor
        if is_reply_to_bot and not re.search(r"\b(you|u|ur|your|yours|bot|sonar|sonarr)\b", processed_message):
            processed_message = "bot, " + processed_message

        # 1. Try complex pattern matching first (highest priority)
        # pattern_match now returns (cat, conf, modifiers)
        logger.info(f"[Classify] Calling pattern_match on processed message...")
        pattern_result = pattern_match(processed_message)
        logger.info(f"[Classify] pattern_match returned: {pattern_result}")
        pattern_cat, pattern_conf = pattern_result[0], pattern_result[1]
        
        if pattern_cat:
            # Store modifiers for potential use
            self._last_modifiers = pattern_result[2] if len(pattern_result) > 2 else {}
            logger.info(f"[Classify] Pattern match: {pattern_cat} (conf={pattern_conf + 1}, mods={self._last_modifiers})")
            return (pattern_cat, pattern_conf + 1)  # Confidence 3-4
        
        # 2. Fall back to keyword classification
        keyword_cat, keyword_score = self.keyword_classify(message, return_score=True)
        if keyword_cat:
            # Dominant keyword (2+ matches) = confidence 2
            # Single keyword = confidence 1
            confidence = 2 if keyword_score >= 2 else 1
            logger.info(f"[Classify] Keyword match: {keyword_cat} (conf={confidence}, score={keyword_score})")
            return (keyword_cat, confidence)
        
        logger.info(f"[Classify] No match for: '{message[:50]}'")
        return (None, 0)
    
    def smart_classify_full(self, message: str) -> tuple:
        """
        Full classification with modifier details.
        
        Returns:
            Tuple of (category, confidence, modifiers_dict)
        """
        # Try complex pattern matching first
        pattern_result = pattern_match(message)
        pattern_cat, pattern_conf = pattern_result[0], pattern_result[1]
        modifiers = pattern_result[2] if len(pattern_result) > 2 else {}
        
        if pattern_cat:
            return (pattern_cat, pattern_conf + 1, modifiers)
        
        # Fall back to keyword classification
        keyword_cat, keyword_score = self.keyword_classify(message, return_score=True)
        if keyword_cat:
            confidence = 2 if keyword_score >= 2 else 1
            return (keyword_cat, confidence, modifiers)
        
        return (None, 0, modifiers)
    
    def classify(self, message: str, min_confidence: int = 1) -> str | None:
        """
        Classify a message and return the category if confidence meets threshold.
        
        Args:
            message: The message text to classify
            min_confidence: Minimum confidence level required (1-3)
            
        Returns:
            Category name or None if confidence is too low
        """
        category, confidence = self.smart_classify(message)
        if confidence >= min_confidence:
            return category
        return None


# Singleton instance for convenience
_classifier_instance = None

def get_classifier() -> MessageClassifier:
    """Get or create the singleton MessageClassifier instance."""
    global _classifier_instance
    if _classifier_instance is None:
        _classifier_instance = MessageClassifier()
    return _classifier_instance
