"""
Pattern matching module for context-aware text classification.

This module implements Subject-Action-Target anchoring for understanding
the relationship between pronouns and keywords in messages.

Features:
- Pronoun anchor detection (self, target, third-party)
- Negation handling ("I don't hate you" vs "I hate you")
- Intensifier scoring for severity detection
- Question detection as a modifier
- Third-party gossip engagement
"""

import re
import logging

logger = logging.getLogger("bot")

# ================== PRONOUN ANCHORS ==================
# Self Anchors - refers to the speaker
SELF_ANCHORS = r"\b(i|me|my|mine|myself|we|us|our|ours|im|i'm|ive|i've|id|i'd|ill|i'll)\b"

# Target Anchors - refers to the bot/listener  
TARGET_ANCHORS = r"\b(you|u|ur|your|yours|yourself|yall|y'all|bot|sonar|sonarr)\b"

# Third-Party Anchors - refers to others
THIRD_PARTY_ANCHORS = r"\b(he|him|his|she|her|hers|they|them|their|theirs|it|its|bro|sis|man|girl|dude|guy|guys|everyone|everybody|someone|somebody|anyone|anybody|people|that person|this person)\b"


# ================== NEGATION WORDS ==================
# Words that flip the meaning of a statement
NEGATION_WORDS = [
    "not", "dont", "don't", "doesnt", "doesn't", "didnt", "didn't",
    "wont", "won't", "wouldnt", "wouldn't", "cant", "can't", "cannot",
    "never", "hardly", "barely", "no", "none", "neither", "nor",
    "aint", "ain't", "isnt", "isn't", "arent", "aren't", "wasnt", "wasn't",
    "werent", "weren't", "havent", "haven't", "hasnt", "hasn't", "hadnt", "hadn't"
]
NEGATION_PATTERN = r"\b(" + "|".join(NEGATION_WORDS) + r")\b"


# ================== INTENSIFIERS ==================
# Words that increase the severity/confidence of a statement
INTENSIFIERS = [
    "really", "very", "so", "such", "extremely", "incredibly", "absolutely",
    "totally", "completely", "utterly", "freaking", "fucking", "damn",
    "super", "mega", "hella", "mad", "crazy", "insanely", "genuinely",
    "seriously", "honestly", "truly", "literally", "actually"
]
INTENSIFIER_PATTERN = r"\b(" + "|".join(INTENSIFIERS) + r")\b"


# ================== QUESTION MARKERS ==================
# Interrogative words and patterns
QUESTION_STARTERS = [
    "what", "why", "how", "when", "where", "who", "which", "whose",
    "can", "could", "would", "will", "should", "do", "does", "did",
    "is", "are", "was", "were", "have", "has", "had", "am"
]
QUESTION_STARTER_PATTERN = r"^(" + "|".join(QUESTION_STARTERS) + r")\b"


# ================== PATTERN KEYWORDS ==================
# Keywords grouped by sentiment/action type for pattern matching

NEGATIVE_ACTION_WORDS = r"\b(hate|hates|hating|hated|dislike|dislikes|despise|despises|loathe|loathes|detest|detests|cant stand|can't stand|sick of|tired of|annoyed by|annoyed with|mad at|angry at|angry with|pissed at|pissed off at|furious at|furious with)\b"

INSULT_WORDS = r"\b(stupid|dumb|idiot|moron|retard|retarded|loser|pathetic|useless|worthless|trash|garbage|terrible|awful|ugly|suck|sucks|sucked|worst|brainless|braindead|brain dead|moronic|idiotic|piece of shit|pos|dumbass|asshole|bastard|bitch|dick|crap|crappy|annoying|irritating|obnoxious|insufferable|unbearable|intolerable|lame|boring|basic)\b"

THREAT_WORDS = r"\b(kill|hurt|beat|fight|destroy|murder|attack|punch|hit|slap|kick|stab|shoot|strangle|choke|die|dead|death)\b"

AFFECTION_WORDS = r"\b(love|loves|loving|loved|like|likes|liked|adore|adores|adored|miss|misses|missed|missing|care about|cares about|appreciate|appreciates|cherish|cherishes|fond of)\b"

HELP_WORDS = r"\b(help|helps|helping|helped|assist|assists|assisting|assisted|support|supports|save|saves|need|needs|needed)\b"

QUESTION_WORDS = r"\b(what|why|how|when|where|who|which|can|could|would|will|should|do|does|did|is|are|was|were|have|has|had)\b"


# ================== COMPLEX PATTERNS ==================
# Subject-Action-Target structure for context-aware classification
# Format: (pattern_name, source_anchor, keywords, target_anchor, result_category)
# source/target can be: "self", "target", "third", "any", or None

COMPLEX_PATTERNS = [
    # === INSULTS ===
    # "I hate you" / "We dislike you" -> insult
    ("insult", "self", NEGATIVE_ACTION_WORDS, "target", "insult"),
    # "You are stupid" / "You're an idiot" -> insult
    ("insult", "target", INSULT_WORDS, None, "insult"),
    # "You suck" / "You're trash" -> insult
    ("insult", "target", NEGATIVE_ACTION_WORDS, None, "insult"),
    
    # === VENT (Self-deprecation) ===
    # "I hate myself" / "I am stupid" -> vent
    ("vent", "self", NEGATIVE_ACTION_WORDS, "self", "vent"),
    ("vent", "self", INSULT_WORDS, None, "vent"),  # "I'm stupid"
    # "I want to die" / "I hate my life" -> vent
    ("vent", "self", THREAT_WORDS, "self", "vent"),
    
    # === CONFUSION/DENIAL (Reverse accusation) ===
    # "You hate me" / "The bot hates me" -> confusion (not an insult TO the user)
    ("confusion", "target", NEGATIVE_ACTION_WORDS, "self", "confusion"),
    # "You think I'm stupid" -> confusion
    ("confusion", "target", INSULT_WORDS, "self", "confusion"),
    
    # === AFFECTION ===
    # "I love you" / "I like you" -> affection
    ("affection", "self", AFFECTION_WORDS, "target", "affection"),
    # "I miss you" / "I care about you" -> affection
    
    # === DELUSION (Reverse affection claim) ===
    # "You love me" / "You like me" -> sarcasm (bot doesn't love them)
    ("sarcasm", "target", AFFECTION_WORDS, "self", "sarcasm"),
    
    # === THREAT ===
    # "I will kill you" / "I'm gonna hurt you" -> threat
    ("threat", "self", THREAT_WORDS, "target", "threat"),
    # "I want you dead" -> threat
    
    # === REQUEST/HELP ===
    # "Can you help me" / "Will you assist me" -> help
    ("help", "target", HELP_WORDS, "self", "help"),
    # "Help me" / "I need help" -> help
    ("help", "self", HELP_WORDS, None, "help"),
    
    # === QUESTION (directed at bot) ===
    # "What do you think" / "How do you feel" -> question
    ("question", "target", QUESTION_WORDS, None, "question"),
    
    # === GOSSIP (about third parties) ===
    # "I hate him" / "She's so stupid" -> gossip engagement
    ("gossip_third", "self", NEGATIVE_ACTION_WORDS, "third", "gossip"),
    ("gossip_third", "third", INSULT_WORDS, None, "gossip"),
    ("gossip_third", "third", NEGATIVE_ACTION_WORDS, None, "gossip"),
]


# ================== HELPER FUNCTIONS ==================

def get_anchor_pattern(anchor_type: str) -> str | None:
    """Get the regex pattern for an anchor type."""
    if anchor_type == "self":
        return SELF_ANCHORS
    elif anchor_type == "target":
        return TARGET_ANCHORS
    elif anchor_type == "third":
        return THIRD_PARTY_ANCHORS
    elif anchor_type == "any":
        return f"({SELF_ANCHORS}|{TARGET_ANCHORS}|{THIRD_PARTY_ANCHORS})"
    return None


def check_negation(text: str, keyword_match_start: int) -> bool:
    """
    Check if there's a negation word within 3 words before the keyword.
    
    Args:
        text: The full text being analyzed
        keyword_match_start: The character position where the keyword starts
        
    Returns:
        True if negation is detected, False otherwise
    """
    # Get the text before the keyword (up to 30 chars should cover 3 words)
    preceding_text = text[max(0, keyword_match_start - 30):keyword_match_start].lower()
    
    # Check for negation words in the preceding text
    negation_match = re.search(NEGATION_PATTERN, preceding_text)
    if negation_match:
        # Count words between negation and keyword
        between_text = preceding_text[negation_match.end():]
        word_count = len(between_text.split())
        if word_count <= 2:  # Negation within 2 words of keyword
            return True
    
    return False


def count_intensifiers(text: str) -> int:
    """
    Count intensifier words in the text.
    
    Returns the number of intensifiers found.
    """
    matches = re.findall(INTENSIFIER_PATTERN, text.lower())
    return len(matches)


def is_question(text: str) -> bool:
    """
    Check if the text is a question.
    
    Looks for:
    - Ends with question mark
    - Starts with interrogative word
    """
    text_stripped = text.strip()
    
    # Check for question mark
    if text_stripped.endswith("?"):
        return True
    
    # Check for question starter
    if re.match(QUESTION_STARTER_PATTERN, text_stripped.lower()):
        return True
    
    return False


def extract_third_party_subject(text: str) -> str | None:
    """
    Extract the third-party subject from text for gossip tracking.
    
    Returns the matched pronoun/reference or None.
    """
    match = re.search(THIRD_PARTY_ANCHORS, text.lower())
    if match:
        return match.group(0)
    return None


def pattern_match(text: str) -> tuple:
    """
    Context-aware pattern matching using Subject-Action-Target anchoring.
    
    Features:
    - Negation detection ("I don't hate you" → not an insult)
    - Intensifier scoring (confidence boost)
    - Question modifier detection
    
    Returns (matched_category, confidence, modifiers) or (None, 0, {}) if no match.
    
    Args:
        text: The message text to analyze
        
    Returns:
        Tuple of (category, confidence, modifiers_dict)
        - confidence: 0 = no match, 2 = pattern match, 3 = pattern + intensifiers
        - modifiers: {"negated": bool, "is_question": bool, "third_party": str|None}
    """
    text_lower = text.lower()
    modifiers = {
        "negated": False,
        "is_question": is_question(text),
        "third_party": extract_third_party_subject(text),
        "intensifier_count": count_intensifiers(text)
    }
    
    for pattern_name, source, keywords, target, result_cat in COMPLEX_PATTERNS:
        regex_parts = []
        
        # Get source pattern
        source_pattern = get_anchor_pattern(source)
        target_pattern = get_anchor_pattern(target)
        
        # Build regex: [Source] ... [Keyword] ... [Target]
        if source_pattern:
            regex_parts.append(f"({source_pattern})")
        
        # Filler: allow up to 10 words between parts (non-greedy)
        filler = r"(?:\s+\S+){0,10}?\s+"
        
        if regex_parts:
            regex_parts.append(filler)
        
        # Capture keyword with named group
        regex_parts.append(f"(?P<keyword>{keywords})")
        
        if target_pattern:
            regex_parts.append(filler)
            regex_parts.append(f"({target_pattern})")
        
        full_regex = "".join(regex_parts)
        
        try:
            match = re.search(full_regex, text_lower, re.IGNORECASE)
            if match:
                keyword_start = match.start("keyword")
                
                # === NEGATION CHECK ===
                if check_negation(text_lower, keyword_start):
                    logger.debug(f"[Pattern] '{pattern_name}' NEGATED in: '{text[:50]}'")
                    modifiers["negated"] = True
                    
                    # Flip the category for negated statements
                    if result_cat == "insult":
                        # "I don't hate you" -> could be neutral or even affection
                        return ("random", 1, modifiers)
                    elif result_cat == "affection":
                        # "I don't love you" -> cold/rejection
                        return ("insult", 1, modifiers)
                    elif result_cat == "threat":
                        # "I won't hurt you" -> reassurance
                        return ("random", 1, modifiers)
                    # Other categories: just return with lower confidence
                    continue
                
                # === CALCULATE CONFIDENCE ===
                base_confidence = 2
                
                # Boost confidence for intensifiers
                if modifiers["intensifier_count"] > 0:
                    base_confidence = 3
                    logger.debug(f"[Pattern] Intensifiers detected ({modifiers['intensifier_count']}), boosted confidence")
                
                # === QUESTION MODIFIER ===
                # "Why are you so stupid?" is both insult AND question
                final_cat = result_cat
                if modifiers["is_question"] and result_cat in ["insult", "threat"]:
                    # Sarcastic/rhetorical insult question
                    final_cat = "sarcasm"
                    logger.debug(f"[Pattern] Question + {result_cat} → sarcasm")
                
                logger.debug(f"[Pattern] Matched '{pattern_name}' → {final_cat} (conf={base_confidence}): '{text[:50]}'")
                return (final_cat, base_confidence, modifiers)
                
        except re.error as e:
            logger.error(f"[Pattern] Regex error for {pattern_name}: {e}")
            continue
    
    return (None, 0, modifiers)


def pattern_match_simple(text: str) -> tuple:
    """
    Simplified pattern_match that returns just (category, confidence).
    For backwards compatibility with existing code.
    """
    result = pattern_match(text)
    return (result[0], result[1])
