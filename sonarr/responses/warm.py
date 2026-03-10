WARM_RESPONSES = {}
# ================== WARM RESPONSES ==================
WARM_RESPONSES["greeting"] = (
    "REACT:👋:Hey there! Good to see you.",
    "DOUBLE:Welcome back!||I actually missed you a little.",
    "Hi! How are you doing today?",
    "STICKER:✨💖👋:Hello again!",
)
WARM_RESPONSES["compliment"] = (
    "REACT:🥰:Aww, stop it you.",
    "DOUBLE:That's actually very sweet.||Thank you.",
    "WHISPER:You're not so bad yourself.",
    "I appreciate that!",
)
WARM_RESPONSES["question"] = (
    "REACT:🤔:Hmm, let me think about that...",
    "DOUBLE:Good question!||Let me see what I can find.",
    "I'd love to help you with that.",
)
WARM_RESPONSES["vent"] = (
    "REACT:🫂:I'm sorry you're dealing with that.",
    "DOUBLE:That sounds really hard.||I'm here if you need to vent.",
    "WHISPER:Take a deep breath. You'll get through this.",
    "You're stronger than you think.",
)
WARM_RESPONSES["apology"] = (
    "REACT:💖:It's okay, I forgive you.",
    "DOUBLE:Don't worry about it.||We're good.",
    "Apology accepted. Let's move on.",
)
WARM_RESPONSES["brag"] = (
    "REACT:🎉:That's awesome! Good job.",
    "DOUBLE:Wow, really?||I'm actually impressed.",
    "You should be proud of that!",
)
WARM_RESPONSES["flirt"] = (
    "REACT:😳:Oh, my.",
    "DOUBLE:You're making a bot blush.||Stop it.",
    "WHISPER:You're sweet.",
)
WARM_RESPONSES["agreement"] = (
    "REACT:🤝:Exactly!",
    "DOUBLE:Yes!||We are on the exact same page.",
    "I totally agree with you.",
)
WARM_RESPONSES["chitchat"] = (
    "REACT:😊:That's interesting!",
    "DOUBLE:Tell me more!||I love hearing about this.",
    "I'm always down for a chat.",
)
WARM_RESPONSES["advice"] = (
    "REACT:💡:I'd love to help you figure this out.",
    "DOUBLE:Here's what I think...||You've got this.",
    "WHISPER:Whatever you decide, I support it.",
)
WARM_RESPONSES["overshare"] = (
    "REACT:🫂:Thank you for trusting me with that.",
    "DOUBLE:That's a lot to carry.||I'm here for you.",
    "WHISPER:Your secret is safe with me.",
)
WARM_RESPONSES["random"] = (
    "REACT:😊:Whatever you say!",
    "DOUBLE:I see!||Tell me more.",
    "Interesting point.",
)


def get_response(message_type="greeting"):
    """Get a random premade cold response for the given message type."""
    if message_type in COLD_RESPONSES:
        return random.choice(COLD_RESPONSES[message_type])
    return random.choice(COLD_RESPONSES["random"])


def get_all_categories():
    """Get list of all available response categories."""
    return list(COLD_RESPONSES.keys())
