"""
Pattern matching module for context-aware text classification.

This module implements Subject-Action-Target anchoring for understanding
the relationship between pronouns and keywords in messages.
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


# ================== PATTERN KEYWORDS ==================
# Keywords grouped by sentiment/action type for pattern matching

NEGATIVE_ACTION_WORDS = r"\b(hate|hates|hating|hated|dislike|dislikes|despise|despises|loathe|loathes|detest|detests|cant stand|can't stand|sick of|tired of|annoyed by|annoyed with|mad at|angry at|angry with|pissed at|pissed off at|furious at|furious with)\b"

INSULT_WORDS = r"\b(stupid|dumb|idiot|moron|retard|retarded|loser|pathetic|useless|worthless|trash|garbage|terrible|awful|ugly|suck|sucks|sucked|worst|brainless|braindead|brain dead|moronic|idiotic|piece of shit|pos|dumbass|asshole|bastard|bitch|dick|crap|crappy)\b"

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
    # "I hate him" / "She's so stupid" -> not directed at bot
    ("gossip_third", "self", NEGATIVE_ACTION_WORDS, "third", "random"),
    ("gossip_third", "third", INSULT_WORDS, None, "random"),
]


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


def pattern_match(text: str) -> tuple:
    """
    Context-aware pattern matching using Subject-Action-Target anchoring.
    
    Returns (matched_category, confidence) or (None, 0) if no match.
    
    Checks for the structural relationship between pronouns and keywords,
    not just keyword presence.
    
    Args:
        text: The message text to analyze
        
    Returns:
        Tuple of (category, confidence) where confidence is 0-2
    """
    text_lower = text.lower()
    
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
        
        regex_parts.append(f"({keywords})")
        
        if target_pattern:
            regex_parts.append(filler)
            regex_parts.append(f"({target_pattern})")
        
        full_regex = "".join(regex_parts)
        
        try:
            if re.search(full_regex, text_lower, re.IGNORECASE):
                logger.debug(f"[Pattern] Matched '{pattern_name}' → {result_cat}: '{text[:50]}'")
                return (result_cat, 2)  # Confidence 2 for pattern match
        except re.error as e:
            logger.error(f"[Pattern] Regex error for {pattern_name}: {e}")
            continue
    
    return (None, 0)
