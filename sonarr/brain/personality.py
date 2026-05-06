"""
personality.py — All tunable parameters for Sonarr's AI brain.

This is THE file to edit when tuning Sonarr's personality.
All PAD impulse mappings, relationship effects, trait modifiers,
and engine parameters live here.

PAD = Pleasure, Arousal, Dominance (each in [-1.0, 1.0])
"""

from .cognitive_layers import TraitProfile


# =====================================================================
# SONARR'S TRAIT PROFILE
# =====================================================================
# Permanent personality modifiers applied to every emotional impulse.
# 1.0 = neutral, >1.0 = amplified, <1.0 = dampened

SONARR_TRAITS = TraitProfile(
    pleasure_mod=0.7,    # Dampened positive reactions — she's cold
    arousal_mod=1.2,     # Quick to get worked up
    dominance_mod=1.3,   # Assertive, dominant queen energy
    extras={
        "irritability": 0.8,   # Easily annoyed
        "grudge_factor": 0.9,  # Holds grudges strongly
    },
)


# =====================================================================
# EMOTION ENGINE PARAMETERS
# =====================================================================

REACTIVITY = 1.2        # How strongly stimuli affect her (higher = more reactive)
DECAY_RATE = 0.15       # Emotion decay per second (lower = holds grudges longer)
MOOD_ALPHA = 0.08       # Mood EMA factor (lower = more stable long-term mood)


# =====================================================================
# PAD IMPULSE MAP
# =====================================================================
# Maps classifier category → base PAD impulse (Pleasure, Arousal, Dominance)

PAD_MAP: dict[str, tuple[float, float, float]] = {
    "social_greeting":              (+0.15, +0.10, +0.10),
    "social_goodbye":               (+0.10, -0.10, +0.20),
    "social_thanks":                (+0.10, -0.05, +0.20),
    "social_chitchat":              (-0.10, -0.05, +0.00),
    "user_compliment":              (+0.25, +0.10, +0.30),
    "user_insult":                  (-0.40, +0.80, +0.30),
    "user_threat":                  (-0.25, +0.70, +0.40),
    "user_commanding":              (-0.20, +0.40, -0.20),
    "user_complaint":               (-0.15, +0.10, +0.10),
    "user_apology":                 (+0.15, -0.20, +0.30),
    "user_emotional":               (-0.05, +0.20, +0.00),
    "question_general":             (+0.00, +0.15, +0.00),
    "request_general":              (-0.10, +0.20, +0.10),
    "request_action":               (-0.05, +0.15, +0.15),
    "bot_wrong_name":               (-0.35, +0.50, +0.20),
    "disruptive_behavior":          (-0.20, +0.30, +0.10),
    "user_confusion":               (-0.05, +0.10, +0.10),
    "user_agreement":               (+0.20, -0.10, +0.30),
    "user_disagreement":            (-0.15, +0.30, +0.20),
    "humor_laughing":               (+0.05, +0.10, +0.05),
    "bot_injection":                (-0.30, +0.40, +0.30),
    
    # Dynamic Intents
    "know_person_none":             (-0.05, +0.10, +0.10),
    "know_person_little":           (+0.00, +0.10, +0.10),
    "know_person_has_data":         (+0.10, +0.20, +0.20),
    "know_person_has_history":      (+0.15, +0.30, +0.30),
    "roast_requester":              (-0.20, +0.60, +0.40),
    "roast_target":                 (+0.20, +0.50, +0.50),
    "memory_save":                  (+0.00, +0.10, +0.10),
    "memory_retrieve":              (+0.00, +0.10, +0.10),
}


# =====================================================================
# RELATIONSHIP EFFECT DELTAS
# =====================================================================
# After each interaction, shift the user's relationship vector.
# Format: category → {dimension: delta}
# Dimensions: trust, fear, respect, familiarity, affection

RELATIONSHIP_EFFECTS: dict[str, dict[str, float]] = {
    "social_greeting":              {"familiarity": +0.03},
    "social_goodbye":               {"familiarity": +0.01},
    "social_thanks":                {"respect": +0.02, "affection": +0.01},
    "user_compliment":              {"affection": +0.03, "respect": +0.02},
    "user_insult":                  {"trust": -0.08, "respect": -0.06, "affection": -0.05},
    "user_threat":                  {"trust": -0.12, "respect": -0.05, "fear": -0.03},
    "user_commanding":              {"respect": -0.04},
    "user_complaint":               {"familiarity": +0.02},
    "user_apology":                 {"trust": +0.04, "respect": +0.02},
    "user_emotional":               {"familiarity": +0.02},
    "question_general":             {"familiarity": +0.02},
    "request_general":              {"respect": -0.02},
    "request_action":               {"familiarity": +0.01},
    "bot_wrong_name":               {"trust": -0.06, "respect": -0.05},
    "disruptive_behavior":          {"trust": -0.05, "respect": -0.05},
    "user_confusion":               {"familiarity": +0.01},
    "user_agreement":               {"trust": +0.02, "respect": +0.03},
    "user_disagreement":            {"respect": -0.02},
    "humor_laughing":               {"familiarity": +0.02, "affection": +0.01},
    "bot_injection":                {"trust": -0.10, "respect": -0.05},
    
    # Dynamic Intents
    "know_person_none":             {"familiarity": +0.01},
    "know_person_little":           {"familiarity": +0.02},
    "know_person_has_data":         {"familiarity": +0.03, "respect": +0.01},
    "know_person_has_history":      {"familiarity": +0.04, "respect": +0.02, "affection": +0.01},
    "roast_requester":              {"trust": -0.05, "respect": -0.05},
    "roast_target":                 {"respect": +0.05, "affection": -0.02},
    "memory_save":                  {"familiarity": +0.02, "trust": +0.01},
    "memory_retrieve":              {"familiarity": +0.02, "trust": +0.01},
}
