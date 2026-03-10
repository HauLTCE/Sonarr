import re

from .anchors import TARGET_ANCHORS, THIRD_PARTY_ANCHORS, IMPLIED_THIRD_PARTY

# ================== NEGATION WORDS ==================
NEGATION_WORDS = [
    "not", "dont", "don't", "doesnt", "doesn't", "didnt", "didn't",
    "wont", "won't", "wouldnt", "wouldn't", "cant", "can't", "cannot",
    "never", "hardly", "barely", "no", "none", "neither", "nor",
    "aint", "ain't", "isnt", "isn't", "arent", "aren't", "wasnt", "wasn't",
    "werent", "weren't", "havent", "haven't", "hasnt", "hasn't", "hadnt", "hadn't"
]
NEGATION_PATTERN = r"\b(" + "|".join(NEGATION_WORDS) + r")\b"


# ================== INTENSIFIERS & MODIFIERS ==================
INTENSIFIERS = [
    "really", "very", "so", "such", "extremely", "incredibly", "absolutely",
    "totally", "completely", "utterly", "freaking", "fucking", "damn",
    "super", "mega", "hella", "mad", "crazy", "insanely", "genuinely",
    "seriously", "honestly", "truly", "literally", "actually",
    "lowkey", "highkey", "deadass", "fr", "frfr", "ngl", "tbh", "istg", "ong", "no cap"  # Gen-Z
]
INTENSIFIER_PATTERN = r"\b(" + "|".join(INTENSIFIERS) + r")\b"

OVERLY_MODIFIERS = [
    "too", "way too", "excessively", "overly", "too damn", "wayy too"
]
OVERLY_MODIFIER_PATTERN = r"\b(" + "|".join(OVERLY_MODIFIERS) + r")\b"


# ================== QUESTION MARKERS ==================
QUESTION_STARTERS = [
    "what", "why", "how", "when", "where", "who", "which", "whose",
    "can", "could", "would", "will", "should", "do", "does", "did",
    "is", "are", "was", "were", "have", "has", "had", "am",
    "y", "wut", "wat", "wht", "hw", "whr"  # Gen-Z abbreviations
]
QUESTION_STARTER_PATTERN = r"^(" + "|".join(QUESTION_STARTERS) + r")\b"


# ================== SARCASM MARKERS ==================
SARCASM_MARKERS = [
    "oh wow", "oh great", "oh sure", "oh yeah", "oh really",
    "yeah right", "sure thing", "suuure", "suuuure",
    "wow", "gee", "gosh", "golly",
    "thanks for nothing", "how wonderful", "how nice", "how lovely",
    "what a surprise", "big surprise", "shocking", "shocker",
    "as if", "like that's", "real nice", "real smart", "real helpful"
]
SARCASM_MARKER_PATTERN = r"^(" + "|".join([re.escape(m) for m in SARCASM_MARKERS]) + r")\b"

SARCASM_NEGATION_MARKERS = ["sure", "right", "of course", "obviously", "clearly", "definitely", "totally"]
SARCASM_NEGATION_PATTERN = r"^(" + "|".join(SARCASM_NEGATION_MARKERS) + r")\b"


# ================== CLAUSE CONJUNCTIONS ==================
CLAUSE_CONJUNCTIONS = [
    "but", "however", "yet", "although", "though", "still", "except",
    "nevertheless", "nonetheless", "on the other hand", "that said"
]
CLAUSE_SPLIT_PATTERN = r"\b(" + "|".join(CLAUSE_CONJUNCTIONS) + r")\b"


# ================== GENDER MISGENDERING DETECTION ==================
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
    Should ignore if the user is actually talking ABOUT a third party.
    """
    text_lower = text.lower()
    
    # If there's an explicit third party mentioned in the same sentence, assume they are talking about them
    if extract_third_party_subject(text_lower):
        return None
        
    # Check if message is directed at bot
    has_target = bool(re.search(TARGET_ANCHORS, text_lower))
    
    # Check for summoning/calling patterns
    summoning_patterns = [
        r"\b(call|summon|invoke|bring forth|heed|answer)\b",
        r"\b(come|appear|show yourself)\b",
    ]
    is_summoning = any(re.search(p, text_lower) for p in summoning_patterns)
    
    # Check for direct address patterns
    direct_address_patterns = [
        r"^(hey|hi|hello|yo|sup|thanks|thank you|thx|ty|ok|okay|alright|aight|sure|yeah|yea|yes|no|nah|nope|wow|oh|lol|lmao|haha)\s*,?\s*",
        r"(thanks|thank you|thx|ty|ok|okay|alright|aight|sure|yeah|yea|yes|no|nah|nope|wow|oh|lol|lmao|haha)\s*,?\s*$",
    ]
    
    is_direct_address = any(re.search(p, text_lower) for p in direct_address_patterns)
    
    should_check = is_direct_address or len(text.split()) <= 3 or is_summoning or has_target
    
    if should_check:
        for term_category, pattern in MASCULINE_TERMS.items():
            if re.search(pattern, text_lower):
                third_party_pattern = r"(my|your|his|her|their|the|a|that|this)\s+" + pattern.replace(r"\b", "")
                third_party_check = re.search(third_party_pattern, text_lower)
                if not third_party_check:
                    return term_category
    return None


def detect_correct_pronouns(text: str) -> bool:
    """
    Check if the user is using correct feminine pronouns to refer to Sonarr.
    """
    text_lower = text.lower()
    
    # Feminine terms that refer to the bot
    FEMININE_PATTERNS = [
        r"\b(she|her|hers|herself)\b",  # Pronouns
        r"\b(queen|girl|woman|lady|miss|ma'am|maam|ms)\b",  # Titles
        r"\b(sis|sister|girly)\b",  # Casual
    ]
    
    # Check if message is directed at bot
    has_target = bool(re.search(TARGET_ANCHORS, text_lower))
    
    # Direct address patterns
    direct_address_patterns = [
        r"^(hey|hi|hello|yo|sup|thanks|thank you|thx|ty)\s*,?\s*",
        r"(thanks|thank you|thx|ty)\s*,?\s*$",
    ]
    
    is_direct_address = any(re.search(p, text_lower) for p in direct_address_patterns)
    
    # Look for feminine pronouns in bot-directed context
    if has_target or is_direct_address or len(text.split()) <= 5:
        for pattern in FEMININE_PATTERNS:
            if re.search(pattern, text_lower):
                third_party_pattern = r"(my|your|his|her|their|the|a|that|this)\s+" + pattern.replace(r"\b", "")
                third_party_check = re.search(third_party_pattern, text_lower)
                if not third_party_check:
                    return True
    
    return False


# ================== BACKHANDED COMPLIMENT PATTERNS ==================
BACKHANDED_PATTERNS = [
    r"smarter than (?:you|u) look",
    r"better than (?:i |I )?(?:expected|thought)",
    r"not as (?:stupid|dumb|bad|ugly) as",
    r"for (?:a|an) \w+",  # "smart for a..."
    r"(?:almost|kinda|sorta|kind of|sort of) (?:smart|nice|good|cool)",
    r"if (?:you|u) (?:were|was) nicer", 
    r"might (?:actually )?like (?:you|u) if", 
]
BACKHANDED_PATTERN = r"(" + "|".join(BACKHANDED_PATTERNS) + r")"


# ================== CONDITIONAL MARKERS ==================
CONDITIONAL_INSULT_PATTERNS = [
    r"if (?:you|u) (?:weren't|werent|were not|wasn't|wasnt|was not) (?:so |such a?)?", 
    r"would .+ if (?:you|u)", 
    r"if (?:you|u) (?:keep|kept|continue)", 
]

CONDITIONAL_THREAT_PATTERNS = [
    r"(?:gonna|going to|will|i'll|im gonna|i'm gonna) (?:hate|hurt|kill|beat|destroy)",
    r"if (?:you|u) (?:keep|kept|continue|don't stop)",
]

HEDGED_INSULT_PATTERNS = [
    r"(?:i think|i feel like|i believe|maybe|perhaps|probably|kinda|kind of|sorta|sort of) (?:you|u) (?:might |may |could )?(?:be )?",
]

CONDITIONAL_NEGATION_PATTERNS = [
    r"if (?:i|I) (?:said|called|thought) (?:you|u) (?:were|was|are) \w+.{0,20}(?:would be lying|wouldn't be true|would be wrong|be lying)",
    r"(?:would be lying|wouldn't be true|would be wrong) if (?:i|I) (?:said|called|thought) (?:you|u)",
    r"if (?:i|I) (?:said|called|thought) .{0,30}(?:would be lying|wouldn't be true|be lying)",
]



def check_negation(text: str, keyword_match_start: int) -> bool:
    """Check if there's a negation word within 3 words before the keyword."""
    preceding_text = text[max(0, keyword_match_start - 75):keyword_match_start].lower()
    
    negation_match = re.search(NEGATION_PATTERN, preceding_text)
    if negation_match:
        between_text = preceding_text[negation_match.end():]
        word_count = len(between_text.split())
        if word_count <= 3:
            return True
    
    return False


def count_intensifiers(text: str) -> int:
    """Count intensifier words in the text."""
    matches = re.findall(INTENSIFIER_PATTERN, text.lower())
    return len(matches)


def count_overly_modifiers(text: str) -> int:
    """Count overly modifiers in the text (like 'too', 'way too')."""
    matches = re.findall(OVERLY_MODIFIER_PATTERN, text.lower())
    return len(matches)


def is_question(text: str) -> bool:
    """Check if the text is a question."""
    text_stripped = text.strip()
    
    # Check for question mark
    if text_stripped.endswith("?"):
        return True
    
    # Check for question starter
    if re.match(QUESTION_STARTER_PATTERN, text_stripped.lower()):
        return True
    
    return False


def detect_sarcasm_marker(text: str) -> bool:
    """Check if the text starts with a sarcasm marker."""
    text_lower = text.lower().strip()
    
    # Check for sarcasm marker at start
    if re.match(SARCASM_MARKER_PATTERN, text_lower, re.IGNORECASE):
        return True
    
    # Check for "yeah right" anywhere
    if "yeah right" in text_lower or "as if" in text_lower:
        return True
    
    return False


def split_on_conjunction(text: str) -> tuple:
    """Split text on clause conjunctions (but, however, yet, etc.)"""
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
    """Check if a clause contains a target anchor (you, u, ur, etc.)"""
    if clause is None:
        return False
    return bool(re.search(TARGET_ANCHORS, clause.lower()))


def extract_third_party_subject(text: str) -> str | None:
    """Extract the third-party subject from text for gossip tracking."""
    # Check for direct third-party pronouns first
    match = re.search(THIRD_PARTY_ANCHORS, text.lower())
    if match:
        return match.group(0)
    
    # Check for implied third-party (my mom, my friend, etc.)
    implied_match = re.search(IMPLIED_THIRD_PARTY, text.lower())
    if implied_match:
        return implied_match.group(0)
    
    return None



