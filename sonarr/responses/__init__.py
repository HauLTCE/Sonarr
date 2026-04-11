import os
import random
import importlib

from .escalated import ESCALATED_RESPONSES
from .sassy import SASSY_RESPONSES
from .system import RATE_LIMIT_RESPONSES, IDLE_PING_MESSAGES, get_callout_response
from .gossip import GOSSIP_LINES, IDLE_CHAT_LINES
from .effects import apply_effects

COLD_RESPONSES = {}
EMPTY_MESSAGE_RESPONSES = ()
EVENING_GRACE_RESPONSES = ()
MORNING_GRACE_RESPONSES = ()
LUNCH_BREAK_RESPONSES = ()
ERROR_RESPONSES = ()

GENDER_CORRECTION = {}

# Dynamically load categories from main/
main_dir = os.path.join(os.path.dirname(__file__), "main")
if os.path.exists(main_dir):
    for filename in os.listdir(main_dir):
        if filename.endswith(".py") and filename != "__init__.py":
            mod_name = filename[:-3]
            mod = importlib.import_module(f".main.{mod_name}", package=__name__)
            
            if mod_name == "error":
                ERROR_RESPONSES = getattr(mod, "RESPONSES", ())
            elif mod_name == "empty_message":
                EMPTY_MESSAGE_RESPONSES = getattr(mod, "RESPONSES", ())
            elif mod_name == "evening_grace":
                EVENING_GRACE_RESPONSES = getattr(mod, "RESPONSES", ())
            elif mod_name == "morning_grace":
                MORNING_GRACE_RESPONSES = getattr(mod, "RESPONSES", ())
            elif mod_name == "lunch_break":
                LUNCH_BREAK_RESPONSES = getattr(mod, "RESPONSES", ())
            elif mod_name == "gender_correction":
                GENDER_CORRECTION = getattr(mod, "GENDER_CORRECTION", {})
            else:
                if hasattr(mod, "RESPONSES"):
                    COLD_RESPONSES[mod_name] = mod.RESPONSES

def get_gender_correction(term: str) -> str | None:
    """Get a gender correction response for a specific masculine term."""
    term_lower = term.lower()
    if term_lower in GENDER_CORRECTION:
        return random.choice(GENDER_CORRECTION[term_lower])
    return None

def get_response(message_type="greeting"):
    """Get a random premade cold response for the given message type."""
    if message_type in COLD_RESPONSES:
        return random.choice(COLD_RESPONSES[message_type])
    return random.choice(COLD_RESPONSES.get("random", ["What?"]))

def get_all_categories():
    """Get list of all available response categories."""
    return list(COLD_RESPONSES.keys())

__all__ = [
    "COLD_RESPONSES",
    "ESCALATED_RESPONSES",
    "SASSY_RESPONSES",
    "RATE_LIMIT_RESPONSES",
    "IDLE_PING_MESSAGES",
    "GOSSIP_LINES",
    "IDLE_CHAT_LINES",
    "apply_effects",
    "EMPTY_MESSAGE_RESPONSES",
    "EVENING_GRACE_RESPONSES",
    "MORNING_GRACE_RESPONSES",
    "LUNCH_BREAK_RESPONSES",
    "get_gender_correction",
    "get_callout_response",
]
