"""Symbolic sentiment recognizer (AFFECT_MODEL.md §2.2 `your_sentiment`).

Adapted from the user's sonarr recognizer (E:/projects/bot/sonarr/recognizer) — its
Subject->Action->Target idea, trimmed to the labels a troll-bot needs and with the bugs
I flagged fixed:
  - bare gamer-slang L/W are UPPERCASE-only (no re.IGNORECASE on them) — avoids matching
    every standalone "l"/"w";
  - no broad "for a/an \\w+" backhanded pattern;
  - inputs are length-capped before regex to bound backtracking.

This is a CLASSIFIER: input -> one label from a fixed set. It never generates text and
never decides flow — the FSM consumes the label exactly like a keyword intent. Fully
deterministic, no model, no network (DESIGN §4 / AFFECT_MODEL §8).
"""
from __future__ import annotations

import re

# Labels Elaine reacts to. NEUTRAL is the catch-all.
HOSTILE = "HOSTILE"
FRIENDLY = "FRIENDLY"
NEUTRAL = "NEUTRAL"

_MAX_LEN = 500  # bound regex work on untrusted input

_SELF = r"\b(i|me|my|myself|we|us)\b"
_TARGET = r"\b(you|u|ur|your|yours|yourself|bot|elaine)\b"
_THIRD = r"\b(he|him|his|she|her|they|them|that guy|that girl|this person)\b"

# Strong, unambiguously personal insults — hostile even standalone ("idiot", "trash"),
# because in a 1:1 chat they land on her.
_INSULT_STRONG = (
    r"\b(stupid|dumb|idiot|moron|loser|pathetic|useless|worthless|trash|"
    r"brainless|dumbass|clown|npc|incompetent|imbecile|braindead)\b"
)
# Weak / ambient negatives — describe things as often as people, so they only read as
# hostility toward her when aimed at her ("you're the worst" vs "mondays are the worst").
_INSULT_WEAK = (
    r"\b(garbage|terrible|awful|ugly|sucks?|worst|annoying|lame|boring|"
    r"cringe|toxic|mid|basic|ridiculous|gross|creepy)\b"
)
_NEG_ACTION = r"\b(hate|hates|hating|h8|despise|loathe|detest|can'?t stand|sick of|tired of)\b"

# Crude / profane trash-talk. Recognized as hostile input so Elaine fires back; she
# never repeats these herself (her replies stay sharp, not vulgar). Slurs used as
# insults are matched here as hostility signals only — never echoed.
_PROFANITY = (
    r"\b(f+u+c+k+|fuk|fck|fuc|stfu|gtfo|stfd|shut up|shut it|piss off|screw you|"
    r"bitch|b1tch|biatch|asshole|a\*\*hole|ass|arse|dick|prick|cunt|bastard|"
    r"wanker|twat|douche|jackass|dumbass|bullshit|bs|crap|damn you|go to hell|"
    r"kys|kill yourself|suck it|suck my|eat shit|shit head|shithead|dipshit|"
    r"motherf|mf|fu|stupid bot|dumb bot|garbage bot|trash bot)\b"
)
# Slurs / identity-based jabs used as insults — treated as hostile, NEVER repeated.
_SLUR_INSULT = (
    r"\b(ur gay|you'?re gay|you gay|so gay|that'?s gay|f+a+g+|f+a+g+o+t+|"
    r"retard|retarded|tranny|simp|incel|virgin|neckbeard|cuck)\b"
)
_AFFECTION = (
    r"\b(love|loves|adore|adores|like|likes|miss|missed|appreciate|cherish|admire|"
    r"respect|trust|enjoy|amazing|awesome|great|fantastic|brilliant|excellent|perfect|"
    r"lovely|cute|sweet|cool|nice|kind|smart|clever|genius|best|helpful|based|goated|"
    r"king|queen|legend|wholesome)\b"
)
_THREAT = r"\b(kill|hurt|beat|destroy|murder|attack|punch|stab|choke|strangle)\b"

# Gamer slang that only reads as sentiment when capitalized (case-sensitive on purpose).
_SLANG_POS = r"(?-i:\b[WＷ]\b)"
_SLANG_NEG = r"(?-i:\b[L]\b)"

_NEGATION = r"\b(not|no|never|dont|don'?t|cant|can'?t|isn'?t|aren'?t|wasn'?t|ain'?t|hardly|barely)\b"

_inten = (
    r"\b(really|very|so|extremely|incredibly|absolutely|totally|completely|fucking|"
    r"damn|super|hella|insanely|literally|deadass|fr|frfr)\b"
)


def _has(pattern: str, text: str, flags=re.IGNORECASE) -> bool:
    return bool(re.search(pattern, text, flags))


def _negated_near(text_lower: str, keyword_start: int) -> bool:
    """True if a negation word sits within ~3 words before the keyword."""
    preceding = text_lower[max(0, keyword_start - 60) : keyword_start]
    m = None
    for mm in re.finditer(_NEGATION, preceding):
        m = mm
    if m and len(preceding[m.end() :].split()) <= 3:
        return True
    return False


def classify(text: str) -> str:
    """Return HOSTILE / FRIENDLY / NEUTRAL for the input (Elaine's read on you)."""
    text = (text or "")[:_MAX_LEN]
    if not text.strip():
        return NEUTRAL
    low = text.lower()

    targets_bot = _has(_TARGET, low)
    third_party = _has(_THIRD, low)

    # Threats toward the bot are the strongest hostile signal.
    if _has(_THREAT, low) and targets_bot:
        return HOSTILE

    # Profanity / slurs: crude trash-talk reads hostile when aimed at the bot or thrown
    # standalone (no third party). Negation doesn't soften "fuck you". We recognize these
    # so she reacts; she never repeats them.
    if (_has(_PROFANITY, low) or _has(_SLUR_INSULT, low)) and not third_party:
        return HOSTILE

    # Insult / negative-action keywords, split by strength for negation + target polarity.
    strong = re.search(_INSULT_STRONG, low, re.IGNORECASE)
    weak = re.search(_INSULT_WEAK, low, re.IGNORECASE)
    neg_act = re.search(_NEG_ACTION, low, re.IGNORECASE)
    friendly_kw = re.search(_AFFECTION, low, re.IGNORECASE)

    # Slang (case-sensitive) on the original text.
    slang_neg = _has(_SLANG_NEG, text, flags=0)
    slang_pos = _has(_SLANG_POS, text, flags=0)

    # The operative hostile keyword: a strong insult counts even standalone; a weak/ambient
    # negative ("worst", "boring", "hate") only counts when it's aimed at her — otherwise
    # "my boss is the worst" or "this is boring" would read as an attack instead of a gripe.
    hostile_kw = None
    if strong and not third_party:
        hostile_kw = strong
    elif (weak or neg_act) and targets_bot and not third_party:
        hostile_kw = weak or neg_act

    hostile = False
    friendly = False
    if hostile_kw:
        if _negated_near(low, hostile_kw.start()):
            # "you're not dumb at all" — a negated insult aimed at her is backhanded praise.
            if targets_bot:
                friendly = True
        else:
            hostile = True

    if friendly_kw and not third_party:
        if _negated_near(low, friendly_kw.start()):
            if targets_bot:          # "i don't like you" — negated affection at her is hostile
                hostile = True
        else:
            friendly = True

    if slang_neg:
        hostile = True
    if slang_pos:
        friendly = True

    if hostile and not friendly:
        return HOSTILE
    if friendly and not hostile:
        return FRIENDLY
    if hostile and friendly:
        # mixed ("love you but you're dumb") — the later clause usually wins; bias hostile
        return HOSTILE if hostile_kw and friendly_kw and hostile_kw.start() > friendly_kw.start() else FRIENDLY
    return NEUTRAL
