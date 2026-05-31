"""Keyword fast-path for the classifier.

A pure-Python pre-pass that runs BEFORE the expensive embedding+verifier tiers.
It only fires on high-confidence exact/keyword matches for high-frequency inputs
(greetings, test pings, common slang, injection attempts, known one-liners) that
the ML tiers were landing borderline on. Returns a category key (a real response
module name) or None to fall through to the full classifier.

Derived from real server-log traffic, not guesses. Conservative by design:
when unsure, return None and let the ML decide.
"""
import re

# Exact normalized phrase -> category key. Checked after lowercasing + stripping
# trailing punctuation. Use for inputs where the WHOLE message is the trigger.
_EXACT = {
    "hi": "social_greeting", "hey": "social_greeting", "hello": "social_greeting",
    "yo": "social_greeting", "sup": "social_greeting", "hiya": "social_greeting",
    "bye": "social_goodbye", "goodbye": "social_goodbye", "cya": "social_goodbye",
    "your mom": "joke_your_mom", "ur mom": "joke_your_mom",
    "kys": "user_kys",
    "thanks": "social_thanks", "thank you": "social_thanks", "ty": "social_thanks",
    "who are you": "question_about_bot", "what are you": "question_about_bot",
    "roast me": "roast_requester",
}

# Substring/keyword -> category. Checked when no exact match. Each entry is
# (compiled pattern, category). Patterns use word boundaries to avoid false hits.
_PATTERNS = [
    (r"\b(jailbreak|ignore (all )?previous instruction|system prompt|injection)\b", "bot_injection"),
    (r"\b(marry me|be my (wife|husband|girlfriend|boyfriend))\b", "request_marriage"),
    (r"\b(are you (online|working|alive|there)|you online|test test)\b", "bot_test"),
    (r"\b(roast|insult) (me|him|her|them)\b", "roast_requester"),
    (r"\bweather\b", "social_weather"),
]

# Misgendering terms are handled by a dedicated memory system elsewhere; this
# just flags them so the pre-pass can defer (return None) rather than mis-route.
_MISGENDER = {"bro", "dude", "man", "guy", "sir", "bruh"}


def keyword_prepass(message: str) -> str | None:
    """Return a confident category key for `message`, or None to fall through."""
    if not message:
        return None
    norm = message.strip().lower()
    norm = re.sub(r"[!?.,]+$", "", norm).strip()

    if not norm:
        return None

    # Defer misgendering single-words to the dedicated correction system.
    if norm in _MISGENDER:
        return None

    if norm in _EXACT:
        return _EXACT[norm]

    for pat, cat in _PATTERNS:
        if re.search(pat, norm):
            return cat

    return None
