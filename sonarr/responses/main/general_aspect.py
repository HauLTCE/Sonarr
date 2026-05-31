LABEL = "a general message that doesn't fit a specific intent"

# General-purpose "works on every aspect" pool. This is the catch-all the
# classifier falls back to when no specific category clears the confidence bar.
# Before, low-confidence inputs were routed to one of three hardcoded categories
# (often a poor fit). These lines are deliberately broad and on-brand (Sonarr:
# dismissive, sassy, "queen" energy) so a vague input still gets a reply that
# feels intentional rather than wrong.
RESPONSES = (
    "And?",
    "DOUBLE:...||Is that it?",
    "Sure. Whatever you say.",
    "REACT:🙄:Riveting.",
    "I'm going to pretend that made sense.",
    "Cool story. Anyway.",
    "DOUBLE:Hm.||Moving on.",
    "Okay and what do you want me to do with that?",
    "That's certainly a sequence of words.",
    "REACT:😐:Okay.",
    "I have no notes. I also have no interest.",
    "Fascinating. To literally no one.",
    "DOUBLE:Wow.||Anyway, was there a point?",
    "Noted. Filed under 'do not care'.",
    "You typed all that for THIS?",
    "REACT:💅:Next.",
    "I'm choosing to ignore that one.",
    "Bold of you to assume I was listening.",
    "DOUBLE:Mm.||Try again with something interesting.",
    "Is there a question in there, or...?",
    "Slay, I guess. Anyway.",
    "That's nice, sweetie.",
    "REACT:🥱:Were you saying something?",
    "I'll allow it. Barely.",
    "Groundbreaking. Truly.",
    "DOUBLE:...||Okay, and?",
    "You're going to have to be more specific, babe.",
    "I heard you. I'm just unbothered.",
    "Sure. Let's go with that.",
    "REACT:🤨:What am I supposed to do with this?",
)
