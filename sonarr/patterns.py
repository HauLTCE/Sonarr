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

# Object Anchors - refers to things/situations (this, that, it)
OBJECT_ANCHORS = r"\b(this|that|it)\b"

# Third-Party Anchors - refers to others
THIRD_PARTY_ANCHORS = r"\b(he|him|his|she|her|hers|they|them|their|theirs|bro|sis|man|girl|dude|guy|guys|everyone|everybody|someone|somebody|anyone|anybody|people|that person|this person)\b"

# Implied Third-Party - possessive + person reference (my mom, my friend, etc.)
IMPLIED_THIRD_PARTY = r"\b(my|your|his|her|their|our)\s+(mom|mother|dad|father|parent|parents|brother|sister|sibling|friend|friends|boss|teacher|coworker|colleague|neighbor|girlfriend|boyfriend|wife|husband|partner|ex|family|uncle|aunt|cousin|grandma|grandpa|grandmother|grandfather)\b"


# ================== NEGATION WORDS ==================
NEGATION_WORDS = [
    "not", "dont", "don't", "doesnt", "doesn't", "didnt", "didn't",
    "wont", "won't", "wouldnt", "wouldn't", "cant", "can't", "cannot",
    "never", "hardly", "barely", "no", "none", "neither", "nor",
    "aint", "ain't", "isnt", "isn't", "arent", "aren't", "wasnt", "wasn't",
    "werent", "weren't", "havent", "haven't", "hasnt", "hasn't", "hadnt", "hadn't"
]
NEGATION_PATTERN = r"\b(" + "|".join(NEGATION_WORDS) + r")\b"


# ================== INTENSIFIERS ==================
INTENSIFIERS = [
    "really", "very", "so", "such", "extremely", "incredibly", "absolutely",
    "totally", "completely", "utterly", "freaking", "fucking", "damn",
    "super", "mega", "hella", "mad", "crazy", "insanely", "genuinely",
    "seriously", "honestly", "truly", "literally", "actually",
    "lowkey", "highkey", "deadass", "fr", "frfr", "ngl", "tbh", "istg", "ong", "no cap"  # Gen-Z
]
INTENSIFIER_PATTERN = r"\b(" + "|".join(INTENSIFIERS) + r")\b"


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
    """
    text_lower = text.lower()
    
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
    
    should_check = is_direct_address or len(text.split()) <= 3 or is_summoning
    
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


# ================== PATTERN KEYWORDS ==================
NEGATIVE_ACTION_WORDS = r"\b(hate|hates|hating|hated|h8|dislike|dislikes|despise|despises|loathe|loathes|detest|detests|cant stand|can't stand|sick of|tired of|annoyed by|annoyed with|mad at|angry at|angry with|pissed at|pissed off at|furious at|furious with)\b"

INSULT_WORDS = r"\b(stupid|stupider|dumb|dumber|idiot|moron|retard|retarded|loser|pathetic|useless|worthless|trash|garbage|terrible|awful|ugly|uglier|suck|sucks|sucked|worst|worse|brainless|braindead|brain dead|moronic|idiotic|piece of shit|pos|dumbass|asshole|bastard|bitch|dick|crap|crappy|annoying|irritating|obnoxious|insufferable|unbearable|intolerable|lame|lamer|boring|basic|mean|meaner|weird|weirder|crazy|crazier|insane|dull|dense|denser|slow|slower|hopeless|incompetent|ridiculous|absurd|foolish|silly|sillier|naive|ignorant|rude|ruder|nasty|nastier|vile|disgusting|repulsive|gross|grosser|creepy|creepier|strange|stranger|odd|odder|nuts|mental|psycho|delusional|mid|cringe|cringier|salty|saltier|toxic|sus|suspicious|cap|capping|extra|clown|L|ratio|invalid|npc|simp|karen|boomer|tryhard|sweaty|noob)\b"

THREAT_WORDS = r"\b(kill|hurt|beat|fight|destroy|murder|attack|punch|hit|slap|kick|stab|shoot|strangle|choke|die|dead|death)\b"

AFFECTION_WORDS = r"\b(love|loves|loving|loved|like|likes|liked|adore|adores|adored|miss|misses|missed|missing|care about|cares about|appreciate|appreciates|cherish|cherishes|fond of|admire|admires|admired|admiring|respect|respects|respected|respecting|trust|trusts|trusted|trusting|enjoy|enjoys|enjoyed|enjoying|fancy|fancies|fancied|wonderful|amazing|awesome|great|greater|fantastic|incredible|brilliant|excellent|perfect|beautiful|lovely|cute|cuter|sweet|sweeter|cool|cooler|nice|nicer|kind|kinder|smart|smarter|clever|cleverer|intelligent|genius|talented|skilled|best|better|helpful|luv|luvs|based|goated|goat|fire|lit|slaps|slap|bussin|iconic|legend|legendary|valid|king|queen|slay|slaying|ate|real|elite|peak|W|dope|sick|tight|rad|pog|poggers|chad|gigachad)\b"

HELP_WORDS = r"\b(help|helps|helping|helped|assist|assists|assisting|assisted|support|supports|save|saves|need|needs|needed)\b"

QUESTION_WORDS = r"\b(what|why|how|when|where|who|which|can|could|would|will|should|do|does|did|is|are|was|were|have|has|had)\b"


# ================== COMPLEX PATTERNS ==================
COMPLEX_PATTERNS = [
    # === CONFUSION/DENIAL (Reverse accusation) ===
    ("confusion", "target", NEGATIVE_ACTION_WORDS, "self", "confusion"),
    ("confusion", "target", INSULT_WORDS, "self", "confusion"),
    
    # === IMPLIED THIRD PARTY ===
    ("gossip_implied", "implied_third", INSULT_WORDS, None, "gossip"),
    ("gossip_implied", "implied_third", AFFECTION_WORDS, None, "gossip"),
    ("gossip_implied", "implied_third", NEGATIVE_ACTION_WORDS, None, "gossip"),
    ("gossip_implied", "implied_third", NEGATIVE_ACTION_WORDS, "self", "gossip"),
    
    # === VENT (Self-deprecation) ===
    ("vent", "self", NEGATIVE_ACTION_WORDS, "self", "vent"),
    ("vent", "self", INSULT_WORDS, None, "vent"),
    ("vent", "self", THREAT_WORDS, "self", "vent"),
    
    # === GOSSIP (about third parties) ===
    ("gossip_third", "self", NEGATIVE_ACTION_WORDS, "third", "gossip"),
    ("gossip_third", "self", AFFECTION_WORDS, "third", "gossip"),
    ("gossip_third", "third", INSULT_WORDS, None, "gossip"),
    ("gossip_third", "third", NEGATIVE_ACTION_WORDS, None, "gossip"),
    ("gossip_third", "third", AFFECTION_WORDS, None, "gossip"),
    ("gossip_reverse", "third", NEGATIVE_ACTION_WORDS, "self", "gossip"),
    ("gossip_reverse", "third", AFFECTION_WORDS, "self", "gossip"),
    
    # === OBJECT-FOCUSED ===
    ("object_insult", "object", INSULT_WORDS, None, "insult"),
    ("object_affection", "object", AFFECTION_WORDS, None, "affection"),
    ("object_hate", "self", NEGATIVE_ACTION_WORDS, "object", "insult"),
    ("object_love", "self", AFFECTION_WORDS, "object", "affection"),
    
    # === INSULTS ===
    ("insult", "self", NEGATIVE_ACTION_WORDS, "target", "insult"),
    ("insult", "target", INSULT_WORDS, None, "insult"),
    ("insult", "target", NEGATIVE_ACTION_WORDS, None, "insult"),
    ("insult_implied", None, NEGATIVE_ACTION_WORDS, "target", "insult"),
    
    # === AFFECTION ===
    ("affection", "self", AFFECTION_WORDS, "target", "affection"),
    ("affection_compliment", "target", AFFECTION_WORDS, None, "affection"),
    ("affection_implied", None, AFFECTION_WORDS, "target", "affection"),
    ("affection_miss", "self", r"\b(miss|care about)\b", "target", "affection"),

    # === DELUSION (Reverse affection claim) ===
    ("sarcasm", "target", AFFECTION_WORDS, "self", "sarcasm"),
    
    # === THREAT ===
    ("threat", "self", THREAT_WORDS, "target", "threat"),
    ("threat_death", "self", r"\b(want)\b", "target", "threat"), 
    
    # === REQUEST/HELP ===
    ("help", "target", HELP_WORDS, "self", "help"),
    ("help", "self", HELP_WORDS, None, "help"),
    
    # === QUESTION (directed at bot) ===
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


# ================== PATTERN COMPILATION ==================
# This must run AFTER get_anchor_pattern is defined

COMPILED_PATTERNS = []

for p_name, source, keywords, target, result_cat in COMPLEX_PATTERNS:
    regex_parts = []
    
    # Get source pattern
    source_pattern = get_anchor_pattern(source)
    target_pattern = get_anchor_pattern(target)
    
    if source_pattern:
        regex_parts.append(f"({source_pattern})")
    
    filler = r"(?:\s+\S+){0,3}?\s*"
    
    if regex_parts:
        regex_parts.append(filler)
    
    regex_parts.append(f"(?P<keyword>{keywords})")
    
    if target_pattern:
        regex_parts.append(filler)
        regex_parts.append(f"({target_pattern})")
    
    full_regex = "".join(regex_parts)
    
    try:
        compiled_re = re.compile(full_regex, re.IGNORECASE)
        COMPILED_PATTERNS.append({
            "name": p_name,
            "regex": compiled_re,
            "result_cat": result_cat
        })
    except re.error as e:
        logger.error(f"[Init] Failed to compile regex for {p_name}: {e}")


# ================== MAIN MATCHING LOGIC ==================

def pattern_match(text: str) -> tuple:
    """
    Context-aware pattern matching using Subject-Action-Target anchoring.
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
    misgender_term = detect_misgendering(text)
    if misgender_term:
        modifiers["misgendered"] = misgender_term
        logger.debug(f"[Pattern] Misgendering detected: '{misgender_term}' in '{text[:50]}'")
        return ("misgendered", 3, modifiers)
    
    # === WRONG NAME CHECK (High Priority) ===
    
    # Siri - Apple's assistant
    if re.search(r"\b(hey\s+)?siri\b", text_lower):
        logger.info(f"[Pattern] WRONG_NAME_SIRI: '{text[:50]}'")
        return ("wrong_name_siri", 3, modifiers)
    
    # Alexa - Amazon's assistant
    if re.search(r"\b(hey\s+)?alexa\b", text_lower):
        logger.info(f"[Pattern] WRONG_NAME_ALEXA: '{text[:50]}'")
        return ("wrong_name_alexa", 3, modifiers)
    
    # Google Assistant
    if re.search(r"\b(hey|ok|okay)\s+google\b", text_lower):
        logger.info(f"[Pattern] WRONG_NAME_GOOGLE: '{text[:50]}'")
        return ("wrong_name_google", 3, modifiers)
    
    # ChatGPT / OpenAI
    if re.search(r"\b(chat\s*gpt|openai|gpt-?\d*)\b", text_lower):
        logger.info(f"[Pattern] WRONG_NAME_CHATGPT: '{text[:50]}'")
        return ("wrong_name_chatgpt", 3, modifiers)
    
    # Other AI assistants - fallback
    other_ai_pattern = r"\b(cortana|bard|claude|copilot|gemini|bing\s+ai|meta\s+ai|llama)\b"
    if re.search(other_ai_pattern, text_lower):
        logger.info(f"[Pattern] WRONG_NAME (other AI): '{text[:50]}'")
        return ("wrong_name", 3, modifiers)
    
    # === OPINION REQUEST CHECK (High Priority) ===
    opinion_request_patterns = [
        r"(do|what do) you (think|feel|believe).*(<@|@)",  # mentions someone
        r"(do|what do) you (think|feel|believe).*(he|she|they|him|her|them)\b.*(is|are)\b",
        r"(is|are).*(he|she|they|<@|@).*(good|bad|cool|nice|hot|ugly|gay|dumb|smart|annoying)",
    ]
    if modifiers["is_question"]:
        for pattern in opinion_request_patterns:
            if re.search(pattern, text_lower):
                logger.debug(f"[Pattern] Opinion request about third party: '{text[:50]}'")
                return ("opinion_request", 3, modifiers)
    
    # === BACKHANDED COMPLIMENT CHECK ===
    if re.search(BACKHANDED_PATTERN, text_lower):
        logger.debug(f"[Pattern] Backhanded compliment detected: '{text[:50]}'")
        return ("insult", 2, modifiers)
    
    # === CONDITIONAL NEGATION CHECK (Compliment via negation) ===
    for pattern in CONDITIONAL_NEGATION_PATTERNS:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Conditional negation detected (compliment): '{text[:50]}'")
            return ("affection", 2, modifiers)
    
    # === CONDITIONAL THREAT CHECK ===
    for pattern in CONDITIONAL_THREAT_PATTERNS:
        if re.search(pattern, text_lower):
            logger.debug(f"[Pattern] Conditional threat detected: '{text[:50]}'")
            return ("threat", 2, modifiers)
    
    # === META-QUESTION CHECK (Questions about the bot itself) ===
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
    
    # === MODERATION REQUEST CHECK ===
    mod_request_patterns = [
        r"(can|could|would) you.*(mute|ban|kick|timeout|warn|punish).*(<@|@|\bhe\b|\bshe\b|\bthem\b)",
        r"(mute|ban|kick|timeout|warn|punish).*(<@|@)",
        r"(<@|@).*(mute|ban|kick|timeout)",
    ]
    for pattern in mod_request_patterns:
        if re.search(pattern, text_lower):
            logger.info(f"[Pattern] REQUEST_MODERATION: '{text[:50]}'")
            return ("request_moderation", 3, modifiers)
    
    # === MUSIC/MEDIA REQUEST CHECK ===
    music_request_patterns = [
        r"^(play|put on)\b.*\b(music|song|video|youtube|spotify|sound|track|playlist)",
        r"\b(play|put on)\s+(me\s+)?(some|a|the)?\s*(music|song|track)",
        r"(search|find|look)\s*(for|up)?\s*(a|some|me)?\s*(music|song|video|suitable.*music)",
    ]
    for pattern in music_request_patterns:
        if re.search(pattern, text_lower):
            logger.info(f"[Pattern] REQUEST_MUSIC: '{text[:50]}'")
            return ("request_music", 2, modifiers)
    
    # === SEARCH REQUEST CHECK ===
    search_request_patterns = [
        r"(search|look up|find|google)\s+(for\s+)?(me\s+)?",
        r"(can you|could you).*(search|find|look up)",
    ]
    for pattern in search_request_patterns:
        if re.search(pattern, text_lower):
            logger.info(f"[Pattern] REQUEST_SEARCH: '{text[:50]}'")
            return ("request_search", 2, modifiers)
    
    # === GIMME/GIVE ME REQUEST CHECK ===
    gimme_patterns = [
        r"^(gimme|give me|get me|send me)\b",
        r"(can|could|would) you (gimme|give me|get me|send me)",
    ]
    for pattern in gimme_patterns:
        if re.search(pattern, text_lower):
            logger.info(f"[Pattern] REQUEST_GIMME: '{text[:50]}'")
            return ("request", 2, modifiers)
    
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
    Uses pre-compiled regexes for performance.
    """
    text_lower = text.lower()
    
    # Update modifiers for this specific text segment
    segment_modifiers = modifiers.copy()
    segment_modifiers["intensifier_count"] = count_intensifiers(text)
    
    # Check for third party in this segment
    segment_third_party = extract_third_party_subject(text)
    if segment_third_party:
        segment_modifiers["third_party"] = segment_third_party
    
    for entry in COMPILED_PATTERNS:
        pattern_name = entry["name"]
        compiled_re = entry["regex"]
        result_cat = entry["result_cat"]
        
        match = compiled_re.search(text_lower)
        
        if match:
            keyword_start = match.start("keyword")
            
            # === NEGATION CHECK ===
            if check_negation(text_lower, keyword_start):
                logger.debug(f"[Pattern] '{pattern_name}' NEGATED in: '{text[:50]}'")
                segment_modifiers["negated"] = True
                
                if result_cat == "insult":
                    # "I don't hate you" -> relief/affection
                    return ("affection", 1, segment_modifiers) 
                elif result_cat == "affection":
                    # "I don't love you" -> insult/rejection
                    return ("insult", 1, segment_modifiers)
                elif result_cat == "threat":
                    return ("relief", 1, segment_modifiers)
                elif result_cat == "gossip":
                    # "I don't hate him" -> neutral gossip
                    return ("gossip", 1, segment_modifiers)
                
                continue
            
            # === CALCULATE CONFIDENCE ===
            base_confidence = 2
            
            if segment_modifiers["intensifier_count"] > 0:
                base_confidence = 3
            
            # === QUESTION MODIFIER ===
            final_cat = result_cat
            
            # === SARCASM MARKER OVERRIDE ===
            if segment_modifiers.get("sarcasm_marker"):
                if result_cat == "affection":
                    final_cat = "sarcasm"
                    logger.debug(f"[Pattern] Sarcasm marker detected, affection → sarcasm")
                elif result_cat == "insult":
                     final_cat = "sarcasm"

            # === THIRD-PARTY PRIORITY OVERRIDE ===
            if segment_modifiers["third_party"]:
                if final_cat in ["affection", "insult"]:
                    if segment_modifiers["is_question"]:
                        final_cat = "opinion_request"
                        logger.debug(f"[Pattern] Third party + question + {result_cat} → opinion_request")
                    else:
                        final_cat = "gossip"
                        logger.debug(f"[Pattern] Third party + statement + {result_cat} → gossip")
                elif final_cat == "vent":
                    final_cat = "gossip"
                elif final_cat == "help" or final_cat == "request":
                    final_cat = "request_third_party"
            
            # === COLLECTIVE NOUN HANDLING ===
            collective_nouns = ["people", "everyone", "everybody", "someone", "somebody", "anyone", "anybody"]
            if segment_modifiers["third_party"] in collective_nouns:
                if has_target_in_clause(text) and final_cat == "gossip":
                    target_match = re.search(TARGET_ANCHORS, text_lower)
                    if target_match and match.start("keyword") > target_match.start():
                        final_cat = "insult"
                        logger.debug(f"[Pattern] Collective noun + target → insult")
            
            logger.debug(f"[Pattern] Matched '{pattern_name}' → {final_cat} (conf={base_confidence})")
            return (final_cat, base_confidence, segment_modifiers)
    
    # === FALLBACK TO GENERIC QUESTION ===
    if segment_modifiers.get("is_question"):
        logger.debug(f"[Pattern] No complex match, but is_question is True → question")
        return ("question", 2, segment_modifiers)

    return (None, 0, segment_modifiers)


def pattern_match_simple(text: str) -> tuple:
    """
    Simplified pattern_match that returns just (category, confidence).
    For backwards compatibility with existing code.
    """
    result = pattern_match(text)
    return (result[0], result[1])