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

# Object Anchors - refers to things/situations (this, that, it)
OBJECT_ANCHORS = r"\b(this|that|it)\b"

# Third-Party Anchors - refers to others
THIRD_PARTY_ANCHORS = r"\b(he|him|his|she|her|hers|they|them|their|theirs|bro|sis|man|girl|dude|guy|guys|everyone|everybody|someone|somebody|anyone|anybody|people|that person|this person)\b"

# Implied Third-Party - possessive + person reference (my mom, my friend, etc.)
IMPLIED_THIRD_PARTY = r"\b(my|your|his|her|their|our)\s+(mom|mother|dad|father|parent|parents|brother|sister|sibling|friend|friends|boss|teacher|coworker|colleague|neighbor|girlfriend|boyfriend|wife|husband|partner|ex|family|uncle|aunt|cousin|grandma|grandpa|grandmother|grandfather)\b"


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
    "seriously", "honestly", "truly", "literally", "actually",
    "lowkey", "highkey", "deadass", "fr", "frfr", "ngl", "tbh", "istg", "ong", "no cap"  # Gen-Z
]
INTENSIFIER_PATTERN = r"\b(" + "|".join(INTENSIFIERS) + r")\b"


# ================== QUESTION MARKERS ==================
# Interrogative words and patterns
QUESTION_STARTERS = [
    "what", "why", "how", "when", "where", "who", "which", "whose",
    "can", "could", "would", "will", "should", "do", "does", "did",
    "is", "are", "was", "were", "have", "has", "had", "am",
    "y", "wut", "wat", "wht", "hw", "whr"  # Gen-Z abbreviations
]
QUESTION_STARTER_PATTERN = r"^(" + "|".join(QUESTION_STARTERS) + r")\b"


# ================== SARCASM MARKERS ==================
# Phrases that indicate sarcasm/irony when followed by positive statements
SARCASM_MARKERS = [
    "oh wow", "oh great", "oh sure", "oh yeah", "oh really",
    "yeah right", "sure thing", "suuure", "suuuure",
    "wow", "gee", "gosh", "golly",
    "thanks for nothing", "how wonderful", "how nice", "how lovely",
    "what a surprise", "big surprise", "shocking", "shocker",
    "as if", "like that's", "real nice", "real smart", "real helpful"
]
SARCASM_MARKER_PATTERN = r"^(" + "|".join([re.escape(m) for m in SARCASM_MARKERS]) + r")\b"

# Sarcasm through negation - "you're not annoying at all" with "sure" or similar
SARCASM_NEGATION_MARKERS = ["sure", "right", "of course", "obviously", "clearly", "definitely", "totally"]
SARCASM_NEGATION_PATTERN = r"^(" + "|".join(SARCASM_NEGATION_MARKERS) + r")\b"


# ================== CLAUSE CONJUNCTIONS ==================
# Words that split sentences into clauses - the part AFTER these usually carries the true sentiment
CLAUSE_CONJUNCTIONS = [
    "but", "however", "yet", "although", "though", "still", "except",
    "nevertheless", "nonetheless", "on the other hand", "that said"
]
CLAUSE_SPLIT_PATTERN = r"\b(" + "|".join(CLAUSE_CONJUNCTIONS) + r")\b"


# ================== GENDER MISGENDERING DETECTION ==================
# Terms that incorrectly refer to Sonarr as male - she's female!
MASCULINE_TERMS = {
    "bro": r"\b(bro|broski|brotha|brother)\b",
    "dude": r"\b(dude|duude|duuude)\b",
    "man": r"\b(man|maan|maaan)\b",
    "guy": r"\b(guy|guuy)\b",
    "sir": r"\b(sir|sire)\b",
    "him": r"\b(him)\b",
    "he": r"\b(he|hes|he's)\b",
    "his": r"\b(his)\b",
    "boy": r"\b(boy|boi|boii|boiii)\b",
    "homie": r"\b(homie|homies|homes)\b",
    "king": r"\b(king)\b",
    "bruh": r"\b(bruh|bruuh|bruuuh)\b",
    "mate": r"\b(mate)\b",
    "fella": r"\b(fella|fellas|fellow)\b",
    "lad": r"\b(lad|lads|laddie)\b",
    "gentleman": r"\b(gentleman|gentlemen)\b",
    "mister": r"\b(mister|mr)\b",
}


def detect_misgendering(text: str) -> str | None:
    """
    Check if the user is using masculine terms to refer to Sonarr.
    
    Returns the category of masculine term used, or None if no misgendering detected.
    Only triggers if the term is directed AT the bot (not about third parties).
    """
    text_lower = text.lower()
    
    # Check if message is directed at bot (contains target anchor or addressing bot)
    has_target = bool(re.search(TARGET_ANCHORS, text_lower))
    
    # Also check for direct address patterns like "thanks bro" or "hey dude"
    direct_address_patterns = [
        r"^(hey|hi|hello|yo|sup|thanks|thank you|thx|ty|ok|okay|alright|aight|sure|yeah|yea|yes|no|nah|nope|wow|oh|lol|lmao|haha)\s*,?\s*",
        r"(thanks|thank you|thx|ty|ok|okay|alright|aight|sure|yeah|yea|yes|no|nah|nope|wow|oh|lol|lmao|haha)\s*,?\s*$",
    ]
    
    is_direct_address = any(re.search(p, text_lower) for p in direct_address_patterns)
    
    logger.info(f"[Misgender] Check: '{text_lower}' | has_target={has_target}, direct={is_direct_address}, words={len(text.split())}")
    
    # Only check for misgendering if it's DIRECTLY addressing the bot
    # Don't trigger just because "you" appears - that could be asking about someone else
    if is_direct_address or len(text.split()) <= 3:
        for term_category, pattern in MASCULINE_TERMS.items():
            if re.search(pattern, text_lower):
                # Make sure it's not referring to a third party
                # e.g., "my bro is cool" should NOT trigger
                third_party_pattern = r"(my|your|his|her|their|the|a|that|this)\s+" + pattern.replace(r"\b", "")
                third_party_check = re.search(third_party_pattern, text_lower)
                logger.info(f"[Misgender] Found '{term_category}', third_party_check={bool(third_party_check)}")
                if not third_party_check:
                    logger.info(f"[Misgender] DETECTED: '{term_category}' in '{text[:50]}'")
                    return term_category
    else:
        logger.info(f"[Misgender] Skipped: no target/direct/short msg")
    
    return None


def detect_correct_gender_address(text: str) -> bool:
    """
    Detect if the user is correctly addressing Sonarr as female.
    Heuristics:
    - Must be a direct address to the bot (greeting/thanks patterns or target anchors)
    - Contains feminine honorifics/terms for Sonarr (queen, ma'am, lady, miss)
    We avoid generic 'she/her' to reduce third-party false positives.
    """
    text_lower = text.lower()

    # Direct address (reuse heuristic similar to misgendering)
    direct_address_patterns = [
        r"^(hey|hi|hello|yo|sup|thanks|thank you|thx|ty|ok|okay|alright|aight|sure|yeah|yea|yes|no|nah|nope|wow|oh|lol|lmao|haha)\s*,?\s*",
        r"(thanks|thank you|thx|ty|ok|okay|alright|aight|sure|yeah|yea|yes|no|nah|nope|wow|oh|lol|lmao|haha)\s*,?\s*$",
    ]
    has_target = bool(re.search(TARGET_ANCHORS, text_lower))
    is_direct_address = has_target or any(re.search(p, text_lower) for p in direct_address_patterns)

    if not is_direct_address:
        logger.info(f"[GenderCorrect] Skipped (not direct): '{text_lower[:60]}'")
        return False

    # Feminine terms that imply correct gender for Sonarr
    feminine_terms = r"\b(queen|queenie|ma'am|maam|lady|miss|ms|madam|madame)\b"
    if re.search(feminine_terms, text_lower):
        # Exclude obvious third-party references like "my queen" about someone else
        third_party_exclude = r"(my|your|his|her|their|the|a|that|this)\s+" + feminine_terms.replace(r"\\b", "")
        if not re.search(third_party_exclude, text_lower):
            logger.info(f"[GenderCorrect] DETECTED in '{text_lower[:60]}'")
            return True
    return False


# ================== BACKHANDED COMPLIMENT PATTERNS ==================
# Patterns that look like compliments but are actually insults
BACKHANDED_PATTERNS = [
    r"smarter than (?:you|u) look",
    r"better than (?:i |I )?(?:expected|thought)",
    r"not as (?:stupid|dumb|bad|ugly) as",
    r"for (?:a|an) \w+",  # "smart for a..."
    r"(?:almost|kinda|sorta|kind of|sort of) (?:smart|nice|good|cool)",
    r"if (?:you|u) (?:were|was) nicer",  # "if you were nicer" = you're not nice
    r"might (?:actually )?like (?:you|u) if",  # "might like you if" = don't like you now
]
BACKHANDED_PATTERN = r"(" + "|".join(BACKHANDED_PATTERNS) + r")"


# ================== CONDITIONAL MARKERS ==================
# "If" clauses that often contain hidden insults or threats
CONDITIONAL_INSULT_PATTERNS = [
    r"if (?:you|u) (?:weren't|werent|were not|wasn't|wasnt|was not) (?:so |such a?)?",  # "if you weren't so dumb"
    r"would .+ if (?:you|u)",  # "would like you if you..."
    r"if (?:you|u) (?:keep|kept|continue)",  # "if you keep this up"
]

# Conditional threats - future harm warnings
CONDITIONAL_THREAT_PATTERNS = [
    r"(?:gonna|going to|will|i'll|im gonna|i'm gonna) (?:hate|hurt|kill|beat|destroy)",
    r"if (?:you|u) (?:keep|kept|continue|don't stop)",
]

# Hedged insult patterns - soft language that still insults
HEDGED_INSULT_PATTERNS = [
    r"(?:i think|i feel like|i believe|maybe|perhaps|probably|kinda|kind of|sorta|sort of) (?:you|u) (?:might |may |could )?(?:be )?",
]

# Complex conditional compliments - "If I said you were ugly, I would be lying"
# The insult is negated by the conditional structure
CONDITIONAL_NEGATION_PATTERNS = [
    r"if (?:i|I) (?:said|called|thought) (?:you|u) (?:were|was|are) \w+.{0,20}(?:would be lying|wouldn't be true|would be wrong|be lying)",
    r"(?:would be lying|wouldn't be true|would be wrong) if (?:i|I) (?:said|called|thought) (?:you|u)",
    r"if (?:i|I) (?:said|called|thought) .{0,30}(?:would be lying|wouldn't be true|be lying)",
]


# ================== PATTERN KEYWORDS ==================
# Keywords grouped by sentiment/action type for pattern matching

NEGATIVE_ACTION_WORDS = r"\b(hate|hates|hating|hated|h8|dislike|dislikes|despise|despises|loathe|loathes|detest|detests|cant stand|can't stand|sick of|tired of|annoyed by|annoyed with|mad at|angry at|angry with|pissed at|pissed off at|furious at|furious with)\b"

INSULT_WORDS = r"\b(stupid|stupider|dumb|dumber|idiot|moron|retard|retarded|loser|pathetic|useless|worthless|trash|garbage|terrible|awful|ugly|uglier|suck|sucks|sucked|worst|worse|brainless|braindead|brain dead|moronic|idiotic|piece of shit|pos|dumbass|asshole|bastard|bitch|dick|crap|crappy|annoying|irritating|obnoxious|insufferable|unbearable|intolerable|lame|lamer|boring|basic|mean|meaner|weird|weirder|crazy|crazier|insane|dull|dense|denser|slow|slower|hopeless|incompetent|ridiculous|absurd|foolish|silly|sillier|naive|ignorant|rude|ruder|nasty|nastier|vile|disgusting|repulsive|gross|grosser|creepy|creepier|strange|stranger|odd|odder|nuts|mental|psycho|delusional|mid|cringe|cringier|salty|saltier|toxic|sus|suspicious|cap|capping|extra|clown|L|ratio|invalid|npc|simp|karen|boomer|tryhard|sweaty|noob|bot)\b"

THREAT_WORDS = r"\b(kill|hurt|beat|fight|destroy|murder|attack|punch|hit|slap|kick|stab|shoot|strangle|choke|die|dead|death)\b"

AFFECTION_WORDS = r"\b(love|loves|loving|loved|like|likes|liked|adore|adores|adored|miss|misses|missed|missing|care about|cares about|appreciate|appreciates|cherish|cherishes|fond of|admire|admires|admired|admiring|respect|respects|respected|respecting|trust|trusts|trusted|trusting|enjoy|enjoys|enjoyed|enjoying|fancy|fancies|fancied|wonderful|amazing|awesome|great|greater|fantastic|incredible|brilliant|excellent|perfect|beautiful|lovely|cute|cuter|sweet|sweeter|cool|cooler|nice|nicer|kind|kinder|smart|smarter|clever|cleverer|intelligent|genius|talented|skilled|best|better|helpful|luv|luvs|based|goated|goat|fire|lit|slaps|slap|bussin|iconic|legend|legendary|valid|king|queen|slay|slaying|ate|real|elite|peak|W|dope|sick|tight|rad|pog|poggers|chad|gigachad)\b"

HELP_WORDS = r"\b(help|helps|helping|helped|assist|assists|assisting|assisted|support|supports|save|saves|need|needs|needed)\b"

QUESTION_WORDS = r"\b(what|why|how|when|where|who|which|can|could|would|will|should|do|does|did|is|are|was|were|have|has|had)\b"


# ================== COMPLEX PATTERNS ==================
# Subject-Action-Target structure for context-aware classification
# Format: (pattern_name, source_anchor, keywords, target_anchor, result_category)
# source/target can be: "self", "target", "third", "any", or None

COMPLEX_PATTERNS = [
    # === CONFUSION/DENIAL (Reverse accusation) - CHECK FIRST ===
    # These are more specific (have both source AND target) so check before simpler patterns
    # "You hate me" / "The bot hates me" -> confusion (not an insult TO the user)
    ("confusion", "target", NEGATIVE_ACTION_WORDS, "self", "confusion"),
    # "You think I'm stupid" -> confusion
    ("confusion", "target", INSULT_WORDS, "self", "confusion"),
    
    # === IMPLIED THIRD PARTY - CHECK BEFORE VENT ===
    # "My mom is nice" / "My friend is stupid" - must check before "I am stupid" vent
    ("gossip_implied", "implied_third", INSULT_WORDS, None, "gossip"),
    ("gossip_implied", "implied_third", AFFECTION_WORDS, None, "gossip"),
    ("gossip_implied", "implied_third", NEGATIVE_ACTION_WORDS, None, "gossip"),
    ("gossip_implied", "implied_third", NEGATIVE_ACTION_WORDS, "self", "gossip"),  # "My teacher hates me"
    
    # === VENT (Self-deprecation) ===
    # "I hate myself" / "I am stupid" -> vent
    ("vent", "self", NEGATIVE_ACTION_WORDS, "self", "vent"),
    ("vent", "self", INSULT_WORDS, None, "vent"),  # "I'm stupid"
    # "I want to die" / "I hate my life" -> vent
    ("vent", "self", THREAT_WORDS, "self", "vent"),
    
    # === GOSSIP (about third parties) - CHECK BEFORE GENERIC INSULTS ===
    # "I hate him" / "She's so stupid" -> gossip engagement
    ("gossip_third", "self", NEGATIVE_ACTION_WORDS, "third", "gossip"),
    ("gossip_third", "self", AFFECTION_WORDS, "third", "gossip"),  # "I love her"
    ("gossip_third", "third", INSULT_WORDS, None, "gossip"),
    ("gossip_third", "third", NEGATIVE_ACTION_WORDS, None, "gossip"),
    ("gossip_third", "third", AFFECTION_WORDS, None, "gossip"),  # "She is wonderful"
    # "He doesn't like me" -> gossip (third party + action + self)
    ("gossip_reverse", "third", NEGATIVE_ACTION_WORDS, "self", "gossip"),
    ("gossip_reverse", "third", AFFECTION_WORDS, "self", "gossip"),
    
    # === OBJECT-FOCUSED (this/that/it) ===
    # "This is stupid" / "That is amazing" -> directed at situation/object
    ("object_insult", "object", INSULT_WORDS, None, "insult"),
    ("object_affection", "object", AFFECTION_WORDS, None, "affection"),
    # "I hate this" / "I love that" -> self + action + object
    ("object_hate", "self", NEGATIVE_ACTION_WORDS, "object", "insult"),
    ("object_love", "self", AFFECTION_WORDS, "object", "affection"),
    
    # === INSULTS (less specific - no target requirement for some) ===
    # "I hate you" / "We dislike you" -> insult
    ("insult", "self", NEGATIVE_ACTION_WORDS, "target", "insult"),
    # "You are stupid" / "You're an idiot" -> insult
    ("insult", "target", INSULT_WORDS, None, "insult"),
    # "You suck" / "You're trash" -> insult
    ("insult", "target", NEGATIVE_ACTION_WORDS, None, "insult"),
    # "lowkey hate u" / "hate u" -> implied self + hate + target
    ("insult_implied", None, NEGATIVE_ACTION_WORDS, "target", "insult"),
    
    # === AFFECTION ===
    # "I love you" / "I like you" -> affection
    ("affection", "self", AFFECTION_WORDS, "target", "affection"),
    # "You are smart" / "You're amazing" -> affection (compliment)
    ("affection_compliment", "target", AFFECTION_WORDS, None, "affection"),
    # "luv u" / "love ya" -> implied self + affection + target (no explicit subject)
    ("affection_implied", None, AFFECTION_WORDS, "target", "affection"),
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
    
    # === QUESTION (directed at bot) - LOWEST PRIORITY ===
    # "What do you think" / "How do you feel" -> question
    ("question", "target", QUESTION_WORDS, None, "question"),
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
    elif anchor_type == "object":
        return OBJECT_ANCHORS
    elif anchor_type == "implied_third":
        return IMPLIED_THIRD_PARTY
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


def detect_sarcasm_marker(text: str) -> bool:
    """
    Check if the text starts with a sarcasm marker.
    
    Examples: "Oh wow, you're SO smart" → sarcasm detected
    """
    text_lower = text.lower().strip()
    
    # Check for sarcasm marker at start
    if re.match(SARCASM_MARKER_PATTERN, text_lower, re.IGNORECASE):
        return True
    
    # Check for "yeah right" anywhere
    if "yeah right" in text_lower or "as if" in text_lower:
        return True
    
    return False


def split_on_conjunction(text: str) -> tuple:
    """
    Split text on clause conjunctions (but, however, yet, etc.)
    
    Returns (before_clause, after_clause, conjunction_found)
    If no conjunction found, returns (text, None, False)
    
    The clause AFTER the conjunction usually carries the true sentiment.
    """
    text_lower = text.lower()
    
    # Find the conjunction
    match = re.search(CLAUSE_SPLIT_PATTERN, text_lower)
    if match:
        before = text[:match.start()].strip()
        after = text[match.end():].strip()
        conjunction = match.group(0)
        return (before, after, conjunction)
    
    return (text, None, None)


def has_target_in_clause(clause: str) -> bool:
    """
    Check if a clause contains a target anchor (you, u, ur, etc.)
    """
    if clause is None:
        return False
    return bool(re.search(TARGET_ANCHORS, clause.lower()))


def extract_third_party_subject(text: str) -> str | None:
    """
    Extract the third-party subject from text for gossip tracking.
    
    Returns the matched pronoun/reference or None.
    """
    # Check for direct third-party pronouns first
    match = re.search(THIRD_PARTY_ANCHORS, text.lower())
    if match:
        return match.group(0)
    
    # Check for implied third-party (my mom, my friend, etc.)
    implied_match = re.search(IMPLIED_THIRD_PARTY, text.lower())
    if implied_match:
        return implied_match.group(0)
    
    return None


def pattern_match(text: str) -> tuple:
    """
    Context-aware pattern matching using Subject-Action-Target anchoring.
    
    Features:
    - Clause splitting on "but/however" - prioritizes clause after conjunction
    - Sarcasm marker detection ("Oh wow", "Yeah right", etc.)
    - Negation detection ("I don't hate you" → not an insult)
    - Intensifier scoring (confidence boost)
    - Question modifier detection
    - Third-party priority override
    
    Returns (matched_category, confidence, modifiers) or (None, 0, {}) if no match.
    
    Args:
        text: The message text to analyze
        
    Returns:
        Tuple of (category, confidence, modifiers_dict)
        - confidence: 0 = no match, 2 = pattern match, 3 = pattern + intensifiers
        - modifiers: {"negated": bool, "is_question": bool, "third_party": str|None, "sarcasm_marker": bool, "has_conjunction": bool}
    """
    logger.info(f"[Pattern] pattern_match called with: '{text[:50]}'")
    text_lower = text.lower()
    
    # Initialize modifiers
    modifiers = {
        "negated": False,
        "is_question": is_question(text),
        "third_party": extract_third_party_subject(text),
        "intensifier_count": count_intensifiers(text),
        "sarcasm_marker": detect_sarcasm_marker(text),
        "has_conjunction": False,
        "misgendered": None
    }
    
    # === MISGENDERING CHECK (Highest Priority) ===
    # Sonarr is female - detect masculine terms directed at her
    misgender_term = detect_misgendering(text)
    if misgender_term:
        modifiers["misgendered"] = misgender_term
        logger.debug(f"[Pattern] Misgendering detected: '{misgender_term}' in '{text[:50]}'")
        return ("misgendered", 3, modifiers)
    
    # === BACKHANDED COMPLIMENT CHECK ===
    # "You're smarter than you look" is an insult disguised as a compliment
    if re.search(BACKHANDED_PATTERN, text_lower):
        logger.debug(f"[Pattern] Backhanded compliment detected: '{text[:50]}'")
        return ("insult", 2, modifiers)
    
    # === CONDITIONAL NEGATION CHECK (Compliment via negation) ===
    # "If I said you were ugly, I would be lying" = you're NOT ugly = affection
    for pattern in CONDITIONAL_NEGATION_PATTERNS:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Conditional negation detected (compliment): '{text[:50]}'")
            return ("affection", 2, modifiers)
    
    # === CONDITIONAL THREAT CHECK ===
    # "If you keep this up, I'm gonna hate you" = threat of future action
    for pattern in CONDITIONAL_THREAT_PATTERNS:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Conditional threat detected: '{text[:50]}'")
            return ("threat", 2, modifiers)
    
    # === META-QUESTION CHECK (Questions about the bot itself) ===
    # "what are you?" / "do you work here?" / "are you a bot?"
    meta_patterns = [
        r"^(what|who)\s+(are\s+)?you",
        r"are you a?\s*(bot|ai|robot|real)",
        r"do you work",
        r"what can you do",
        r"what do you do",
    ]
    if modifiers["is_question"]:
        for pattern in meta_patterns:
            if re.search(pattern, text_lower):
                logger.debug(f"[Pattern] Meta-question detected (about bot): '{text[:50]}'")
                return ("meta_question", 2, modifiers)
    
    # === SELF-INQUIRY CHECK ===
    # "am I cool?" / "do I look good?" - asking bot to judge the user
    self_inquiry_patterns = [
        r"^(am i|do i|can i).*\b(cool|good|smart|nice|awesome|bad|ugly|stupid|annoying|funny|pretty|hot|cute)\b",
        r"how do i look",
        r"do i.*good",
        r"am i.*enough",
        r"what do you think (of|about) me",
        r"how am i",
    ]
    if modifiers["is_question"]:
        for pattern in self_inquiry_patterns:
            if re.search(pattern, text_lower):
                logger.debug(f"[Pattern] Self-inquiry detected (asking bot to judge user): '{text[:50]}'")
                return ("self_inquiry", 2, modifiers)
    
    # === RELATIONSHIP STATUS CHECK ===
    relationship_patterns = [
        r"are you (single|taken|dating|married)",
        r"do you have a (boyfriend|girlfriend|partner|wife|husband)",
        r"are you in a relationship",
        r"what('s| is) your relationship status",
    ]
    if modifiers["is_question"]:
        for pattern in relationship_patterns:
            if re.search(pattern, text_lower):
                logger.debug(f"[Pattern] Relationship question detected: '{text[:50]}'")
                return ("relationship", 2, modifiers)
    
    # === AGE CHECK ===
    age_patterns = [
        r"how old are you",
        r"what('s| is) your age",
        r"when were you (born|made|created)",
        r"what year were you",
    ]
    if modifiers["is_question"]:
        for pattern in age_patterns:
            if re.search(pattern, text_lower):
                logger.debug(f"[Pattern] Age question detected: '{text[:50]}'")
                return ("age", 2, modifiers)
    
    # === CAPABILITIES CHECK ===
    capability_patterns = [
        r"what can you do",
        r"can you (do|make|create|help|show)",
        r"are you able to",
        r"what are your (abilities|capabilities|powers)",
    ]
    if modifiers["is_question"]:
        for pattern in capability_patterns:
            if re.search(pattern, text_lower):
                # Make sure no third party
                if not modifiers.get("third_party"):
                    logger.debug(f"[Pattern] Capabilities question detected: '{text[:50]}'")
                    return ("capabilities", 2, modifiers)
    
    # === MEMORY CHECK ===
    memory_patterns = [
        r"do you remember",
        r"did you forget",
        r"you (remember|forgot)",
    ]
    if re.search(r"|".join(memory_patterns), text_lower):
        logger.debug(f"[Pattern] Memory question detected: '{text[:50]}'")
        return ("memory", 2, modifiers)
    
    # === HYPOTHETICAL CHECK ===
    hypothetical_patterns = [
        r"^what if\b",
        r"^if you (could|were|had)",
        r"^hypothetically",
        r"^imagine if",
        r"^would you ever",
    ]
    for pattern in hypothetical_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Hypothetical detected: '{text[:50]}'")
            return ("hypothetical", 2, modifiers)
    
    # === PREFERENCE CHECK ===
    preference_patterns = [
        r"what('s| is) your favo(u)?rite",
        r"do you (like|prefer|enjoy)",
        r"what do you (like|prefer|enjoy)",
        r"which do you (like|prefer)",
    ]
    if modifiers["is_question"]:
        for pattern in preference_patterns:
            if re.search(pattern, text_lower):
                # Only if no third party (avoid "do you like him")
                if not modifiers.get("third_party"):
                    logger.debug(f"[Pattern] Preference question detected: '{text[:50]}'")
                    return ("preference", 2, modifiers)
    
    # === ROLEPLAY CHECK ===
    roleplay_patterns = [
        r"\*[^*]+\*",  # *action text*
        r"^(pretend|imagine|act like|roleplay|rp)",
        r"you are now",
        r"from now on you",
    ]
    for pattern in roleplay_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Roleplay attempt detected: '{text[:50]}'")
            return ("roleplay", 2, modifiers)
    
    # === PHILOSOPHY/EXISTENTIAL CHECK ===
    philosophy_patterns = [
        r"meaning of life",
        r"why (are|do) we exist",
        r"what is (the point|consciousness|reality)",
        r"do you (have a soul|feel|think|exist)",
        r"are you (sentient|conscious|alive|real)",
    ]
    for pattern in philosophy_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Philosophy/existential detected: '{text[:50]}'")
            return ("philosophy", 2, modifiers)
    
    # === TEST/PING CHECK ===
    test_patterns = [
        r"^(test|testing|ping|hello\?|anyone there|you there|u there)$",
        r"^(are you (there|alive|awake|online|working))$",
    ]
    for pattern in test_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Test/ping detected: '{text[:50]}'")
            return ("test", 2, modifiers)
    
    # === WEATHER CHECK (boring small talk) ===
    weather_patterns = [
        r"\b(weather|rain|snow|sunny|cloudy|hot|cold)\b.*(today|outside|there)",
        r"how('s| is) the weather",
        r"is it (raining|snowing|sunny|hot|cold)",
    ]
    for pattern in weather_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Weather small talk detected: '{text[:50]}'")
            return ("weather", 2, modifiers)
    
    # === COMPARISON CHECK ===
    comparison_patterns = [
        r"(better|worse|smarter|dumber) than",
        r"compared to",
        r"(like|similar to|same as) (siri|alexa|chatgpt|gpt|bard|claude)",
        r"you('re| are) (just like|no different)",
    ]
    for pattern in comparison_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Comparison detected: '{text[:50]}'")
            return ("comparison", 2, modifiers)
    
    # === SIMP DETECTION ===
    simp_patterns = [
        r"i('d| would) do anything for you",
        r"you('re| are) (perfect|everything|my queen|my goddess)",
        r"i worship you",
        r"please (notice|love|marry) me",
        r"i('m| am) your (biggest fan|simp|servant)",
    ]
    for pattern in simp_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Simp behavior detected: '{text[:50]}'")
            return ("simp", 2, modifiers)
    
    # === STALKER/CREEPY CHECK ===
    stalker_patterns = [
        r"where do you live",
        r"what('s| is) your (address|location|ip)",
        r"i('ve| have) been (watching|following|stalking)",
        r"i know where you",
        r"i('ll| will) find you",
    ]
    for pattern in stalker_patterns:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Stalker/creepy detected: '{text[:50]}'")
            return ("stalker", 3, modifiers)
    
    # === GENERAL INQUIRY CHECK ===
    # "do you know where X is?" / "can you tell me about Y?"
    # NOTE: This runs AFTER complex patterns so affection/insult get caught first
    if modifiers["is_question"]:
        inquiry_markers = [
            r"^do you know\b",
            r"\bhave you (seen|heard)\b",
            r"\bwhere (is|are|did)\b",
            r"^who (is|are)\b",
            r"^when\b",
        ]
        for pattern in inquiry_markers:
            if re.search(pattern, text_lower):
                logger.debug(f"[Pattern] General inquiry detected: '{text[:50]}'")
                return ("inquiry", 1, modifiers)
    
    # === HEDGED INSULT CHECK ===
    # "I think you might be annoying" = still an insult, just softened
    for pattern in HEDGED_INSULT_PATTERNS:
        if re.search(pattern, text_lower):
            insult_match = re.search(INSULT_WORDS, text_lower)
            if insult_match:
                logger.debug(f"[Pattern] Hedged insult detected: '{text[:50]}'")
                return ("insult", 2, modifiers)
    
    # === SARCASM VIA NEGATION CHECK ===
    # "Sure, you're not annoying at all" - sarcasm marker + negation = sarcasm
    if re.match(SARCASM_NEGATION_PATTERN, text_lower):
        if re.search(NEGATION_PATTERN, text_lower):
            logger.debug(f"[Pattern] Sarcasm negation marker detected: '{text[:50]}'")
            return ("sarcasm", 3, modifiers)
    
    # === SARCASM MARKER CHECK ===
    # If text starts with sarcasm marker, it's likely sarcasm regardless of content
    if modifiers["sarcasm_marker"]:
        # Check if there's a positive word after the marker - that confirms sarcasm
        affection_match = re.search(AFFECTION_WORDS, text_lower)
        if affection_match:
            logger.debug(f"[Pattern] Sarcasm marker + affection word → sarcasm: '{text[:50]}'")
            return ("sarcasm", 3, modifiers)
        # Also check if there's an insult - sarcasm marker + insult = sarcasm
        insult_match = re.search(INSULT_WORDS, text_lower)
        if insult_match:
            logger.debug(f"[Pattern] Sarcasm marker + insult → sarcasm: '{text[:50]}'")
            return ("sarcasm", 3, modifiers)
    
    # === CONDITIONAL INSULT CHECK ===
    # "If you weren't so dumb" / "I would like you if you weren't..."
    for pattern in CONDITIONAL_INSULT_PATTERNS:
        if re.search(pattern, text_lower):
            # Check if there's an insult word in the text
            insult_match = re.search(INSULT_WORDS, text_lower)
            if insult_match:
                logger.debug(f"[Pattern] Conditional insult detected: '{text[:50]}'")
                return ("insult", 2, modifiers)
    
    # === CLAUSE SPLITTING ===
    # Split on "but/however/yet" and prioritize the second clause
    before_clause, after_clause, conjunction = split_on_conjunction(text)
    
    if after_clause:
        modifiers["has_conjunction"] = True
        logger.debug(f"[Pattern] Conjunction '{conjunction}' found, prioritizing after-clause")
        
        # Check if target (you/u) appears in the after-clause
        # If so, the after-clause sentiment toward "you" is the true sentiment
        if has_target_in_clause(after_clause):
            # Process the after-clause first
            after_result = _pattern_match_single(after_clause, modifiers)
            if after_result[0] is not None:
                logger.debug(f"[Pattern] After-clause matched: {after_result[0]}")
                return after_result
        
        # If after-clause didn't match with target, still try it
        after_result = _pattern_match_single(after_clause, modifiers)
        if after_result[0] is not None:
            return after_result
    
    # === MULTI-TARGET WITH COMMA SEPARATION ===
    # "He sucks, you suck, everyone sucks" - multiple subjects with insults
    # If "you" appears with an insult in any comma-separated clause, it's an insult to the target
    if "," in text and has_target_in_clause(text):
        # Split by comma and check each clause
        comma_clauses = text.split(",")
        for clause in comma_clauses:
            clause = clause.strip()
            if has_target_in_clause(clause):
                # This clause has "you" in it - check for insult
                insult_match = re.search(INSULT_WORDS, clause.lower())
                negative_match = re.search(NEGATIVE_ACTION_WORDS, clause.lower())
                if insult_match or negative_match:
                    logger.debug(f"[Pattern] Multi-target: found you-insult in clause: '{clause}'")
                    return ("insult", 2, modifiers)
    
    # === STANDARD PATTERN MATCHING ===
    return _pattern_match_single(text, modifiers)


def _pattern_match_single(text: str, modifiers: dict) -> tuple:
    """
    Internal function to match patterns on a single clause/text.
    Used by pattern_match for clause-split processing.
    """
    text_lower = text.lower()
    
    # Update modifiers for this specific text segment
    segment_modifiers = modifiers.copy()
    segment_modifiers["intensifier_count"] = count_intensifiers(text)
    
    # Check for third party in this segment
    segment_third_party = extract_third_party_subject(text)
    if segment_third_party:
        segment_modifiers["third_party"] = segment_third_party
    
    for pattern_name, source, keywords, target, result_cat in COMPLEX_PATTERNS:
        regex_parts = []
        
        # Get source pattern
        source_pattern = get_anchor_pattern(source)
        target_pattern = get_anchor_pattern(target)
        
        # Build regex: [Source] ... [Keyword] ... [Target]
        if source_pattern:
            regex_parts.append(f"({source_pattern})")
        
        # Filler: allow contractions (you're, I'm) and up to 10 words between parts
        filler = r"(?:['`]?\w*\s+\S*){0,10}?\s*"
        
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
                    segment_modifiers["negated"] = True
                    
                    # Flip the category for negated statements
                    if result_cat == "insult":
                        return ("random", 1, segment_modifiers)
                    elif result_cat == "affection":
                        return ("random", 1, segment_modifiers)
                    elif result_cat == "threat":
                        return ("random", 1, segment_modifiers)
                    elif result_cat == "gossip":
                        return ("gossip", 1, segment_modifiers)
                    continue
                
                # === CALCULATE CONFIDENCE ===
                base_confidence = 2
                
                if segment_modifiers["intensifier_count"] > 0:
                    base_confidence = 3
                
                # === QUESTION MODIFIER ===
                final_cat = result_cat
                if segment_modifiers["is_question"] and result_cat in ["insult", "threat"]:
                    final_cat = "sarcasm"
                
                # === SARCASM MARKER OVERRIDE ===
                # If sarcasm marker detected and we got affection, flip to sarcasm
                if segment_modifiers.get("sarcasm_marker") and result_cat == "affection":
                    final_cat = "sarcasm"
                    logger.debug(f"[Pattern] Sarcasm marker detected, affection → sarcasm")
                
                # === THIRD-PARTY PRIORITY OVERRIDE ===
                # If there's a third party and we matched affection/complaint, it's about someone else, not the bot
                if segment_modifiers["third_party"]:
                    if final_cat in ["affection", "complaint"]:
                        # Question about third party opinion: "do you like him?" -> opinion_request
                        if segment_modifiers["is_question"]:
                            final_cat = "opinion_request"
                            logger.debug(f"[Pattern] Third party + question + {result_cat} → opinion_request")
                        else:
                            # Statement about third party: "I love him" -> gossip
                            final_cat = "gossip"
                            logger.debug(f"[Pattern] Third party + statement + {result_cat} → gossip")
                    elif final_cat == "vent":
                        final_cat = "gossip"
                    elif final_cat == "help" or final_cat == "request":
                        # "can you help him?" -> request_third_party
                        final_cat = "request_third_party"
                        logger.debug(f"[Pattern] Third party + request → request_third_party")
                
                # === COLLECTIVE NOUN HANDLING ===
                # "People say you're trash" / "Everyone knows you're dumb"
                # If third party is collective (people, everyone) AND target is present, it's an insult not gossip
                collective_nouns = ["people", "everyone", "everybody", "someone", "somebody", "anyone", "anybody"]
                if segment_modifiers["third_party"] in collective_nouns:
                    if has_target_in_clause(text) and final_cat == "gossip":
                        # Check if the keyword (insult) is closer to target than to third party
                        target_match = re.search(TARGET_ANCHORS, text_lower)
                        if target_match and match.start("keyword") > target_match.start():
                            # Keyword comes after target - it's about the target
                            final_cat = "insult"
                            logger.debug(f"[Pattern] Collective noun + target → insult")
                
                logger.debug(f"[Pattern] Matched '{pattern_name}' → {final_cat} (conf={base_confidence})")
                return (final_cat, base_confidence, segment_modifiers)
                
        except re.error as e:
            logger.error(f"[Pattern] Regex error for {pattern_name}: {e}")
            continue
    
    return (None, 0, segment_modifiers)


def pattern_match_simple(text: str) -> tuple:
    """
    Simplified pattern_match that returns just (category, confidence).
    For backwards compatibility with existing code.
    """
    result = pattern_match(text)
    return (result[0], result[1])
