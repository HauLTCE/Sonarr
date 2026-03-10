from .anchors import *
import re

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



