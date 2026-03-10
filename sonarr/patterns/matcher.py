import re
import logging

from .anchors import *
from .modifiers import *
from .complex import COMPLEX_PATTERNS

logger = logging.getLogger("bot")


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



# ================== PATTERN COMPILATION ==================
# This must run AFTER get_anchor_pattern is defined

COMPILED_PATTERNS = []

for p_name, source, keywords, target, result_cat in COMPLEX_PATTERNS:
    regex_parts = []
    
    # Get source pattern
    source_pattern = get_anchor_pattern(source)
    target_pattern = get_anchor_pattern(target)
    
    filler = r"(?:\s+\S+){0,4}?\s*"
    
    if source_pattern:
        regex_parts.append(f"({source_pattern})")
        regex_parts.append(filler)
    else:
        # Allow up to 5 arbitrary words at the start if no source anchor
        regex_parts.append(r"^(?:\S+\s+){0,5}?")
    
    regex_parts.append(f"(?P<keyword>(?:{keywords}))")
    
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
        "overly_modifier_count": count_overly_modifiers(text),
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
    # Split on "but/however/yet" and evaluate both clauses
    before_clause, after_clause, conjunction = split_on_conjunction(text)
    
    if after_clause:
        modifiers["has_conjunction"] = True
        logger.debug(f"[Pattern] Conjunction '{conjunction}' found, evaluating both clauses")
        
        has_global_target = has_target_in_clause(text)
        
        before_result = _pattern_match_single(before_clause, modifiers.copy())
        after_result = _pattern_match_single(after_clause, modifiers.copy())
        
        b_cat, b_conf = before_result[0], before_result[1]
        a_cat, a_conf = after_result[0], after_result[1]
        
        if b_cat and a_cat and b_cat != a_cat:
            combined_cat = f"{b_cat}_{a_cat}"
            logger.debug(f"[Pattern] Conjunction split produced mixed state: {combined_cat}")
            return (combined_cat, max(b_conf, a_conf), after_result[2])
            
        elif a_cat:
            if has_global_target and not has_target_in_clause(after_clause):
                logger.debug(f"[Pattern] Conjunction split prioritized after-clause (inheriting global target): {a_cat}")
            else:
                logger.debug(f"[Pattern] Conjunction split prioritized after-clause: {a_cat}")
            return after_result
            
        elif b_cat:
            logger.debug(f"[Pattern] Conjunction split fell back to before-clause: {b_cat}")
            return before_result
    
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
            if segment_modifiers["is_question"]:
                if final_cat in ["insult", "affection", "threat"]:
                    final_cat = f"question_{final_cat}"
                    logger.debug(f"[Pattern] Question + Action → {final_cat}")
            
            # === SARCASM MARKER OVERRIDE ===
            if segment_modifiers.get("sarcasm_marker"):
                if final_cat in ["affection", "question_affection"]:
                    final_cat = "sarcasm"
                    logger.debug(f"[Pattern] Sarcasm marker detected, affection → sarcasm")
                elif final_cat in ["insult", "question_insult"]:
                     final_cat = "sarcasm"
                     
            # === OVERLY MODIFIER OVERRIDE ===
            if segment_modifiers.get("overly_modifier_count", 0) > 0:
                if final_cat in ["affection", "question_affection"]:
                    final_cat = "sarcasm"
                    logger.debug(f"[Pattern] Overly modifier detected, affection → sarcasm")

            # === THIRD-PARTY PRIORITY OVERRIDE ===
            if segment_modifiers["third_party"]:
                if result_cat in ["affection", "insult"]:
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
