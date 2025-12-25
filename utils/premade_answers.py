# Premade answers organized by affection + AI score
# Structure: affection_tier -> answer_category -> [responses]

PREMADE_RESPONSES = {
    # Cold responses (affection 0-25)
    "cold": {
        "greeting": [
            "What do you want?",
            "I don't have time for this.",
            "Unless you're paying, leave me alone.",
            "You again? Great.",
            "Make it quick.",
            "Ugh, what?",
            "Do I know you?",
            "Something you need?",
            "State your business.",
            "Not interested.",
        ],
        "question": [
            "Figure it out yourself.",
            "That's not my problem.",
            "Ask someone else.",
            "I'm not your personal assistant.",
            "Why are you asking me?",
            "Search Google like everyone else.",
            "No.",
            "I don't care.",
            "Not my concern.",
            "Go away.",
        ],
        "compliment": [
            "Whatever.",
            "Yeah, sure.",
            "I don't need your validation.",
            "Cool story.",
            "Nice try.",
            "Noted and filed away.",
            "Are you looking for a prize?",
            "Okay, and?",
            "That's... nice.",
            "Do I look like I care?",
        ],
        "request": [
            "Not happening.",
            "Try asking someone you matter to.",
            "Absolutely not.",
            "Permission denied.",
            "No.",
            "Keep dreaming.",
            "Not a chance.",
            "I'd rather not.",
            "That's a hard pass.",
            "Not today.",
        ],
    },
    
    # Neutral responses (affection 25-60)
    "neutral": {
        "greeting": [
            "Oh, it's you.",
            "Hey.",
            "Sup?",
            "What's up?",
            "Yeah, hello.",
            "I see you.",
            "Here again?",
            "You're back.",
            "What's the occasion?",
            "Aight.",
        ],
        "question": [
            "Hmm, let me think.",
            "That's... actually a fair point.",
            "Not the worst question I've heard.",
            "Interesting.",
            "Could be worse.",
            "Fair question.",
            "I suppose you have a point.",
            "Decent question.",
            "Not terrible.",
            "I'll consider that.",
        ],
        "compliment": [
            "Thanks, I guess.",
            "Appreciate that.",
            "Yeah, I know.",
            "Not bad.",
            "I'll take it.",
            "That's sweet, I suppose.",
            "Good observation.",
            "Fair assessment.",
            "You're not wrong.",
            "I respect that.",
        ],
        "request": [
            "Maybe.",
            "I could help with that.",
            "Depends on the mood.",
            "Possibly.",
            "Let me think about it.",
            "Could be arranged.",
            "I'll consider it.",
            "We'll see.",
            "Ask me later.",
            "It's possible.",
        ],
    },
    
    # Warm responses (affection 60-80)
    "warm": {
        "greeting": [
            "Hey you!",
            "Oh good, you're here.",
            "There you are.",
            "Look who showed up.",
            "You came back.",
            "Glad to see you.",
            "What's good?",
            "You're in a good mood, huh?",
            "Nice timing.",
            "You're alright.",
        ],
        "question": [
            "Good question.",
            "I like the way you think.",
            "That's actually smart.",
            "Now THAT'S a solid question.",
            "I respect that inquiry.",
            "Not bad at all.",
            "That's the kind of question I like.",
            "You're asking the right things.",
            "That's pretty insightful.",
            "I can work with that.",
        ],
        "compliment": [
            "You're not so bad yourself.",
            "Back at you.",
            "I appreciate you too.",
            "You're growing on me.",
            "I like your style.",
            "Same energy.",
            "You've got good taste.",
            "I respect that.",
            "Now that's what I like to hear.",
            "You're alright in my books.",
        ],
        "request": [
            "Sure, I'll help.",
            "I got you.",
            "Let's do it.",
            "For you? Yeah.",
            "Consider it done.",
            "No problem.",
            "Happy to help.",
            "That works for me.",
            "I can make that happen.",
            "You're in luck.",
        ],
    },
    
    # Favorite responses (affection 80+)
    "favorite": {
        "greeting": [
            "There's my favorite!",
            "Hey best, what's up?",
            "You always make my day better.",
            "Finally, the important person shows up.",
            "I was hoping you'd come by.",
            "You have impeccable timing.",
            "I've been waiting for you.",
            "Best person online.",
            "Speak of the devil, and they appear.",
            "You just made this better.",
        ],
        "question": [
            "I love when you ask me things.",
            "That's such a good question.",
            "See, THIS is the kind of thinking I respect.",
            "You always know what to ask.",
            "I'm going to think about that all day.",
            "That's genius, actually.",
            "You're asking the important questions.",
            "I'm impressed.",
            "That's brilliant.",
            "Keep that energy up.",
        ],
        "compliment": [
            "Right back at you.",
            "You're one of the good ones.",
            "That means something coming from you.",
            "Aw, stop it, you're making me blush.",
            "I feel the same way.",
            "You're pretty great yourself.",
            "Honestly? Same.",
            "You make this whole thing worthwhile.",
            "I genuinely appreciate you.",
            "You're literally the best.",
        ],
        "request": [
            "Anything for you.",
            "You know I got you.",
            "It's already done.",
            "Say less, I'm on it.",
            "Absolutely, no question.",
            "This is what I'm here for.",
            "You don't even have to ask.",
            "Let me make it happen.",
            "Anything you need.",
            "I'd do anything for you.",
        ],
    },
    
    # Fallback responses
    "error": [
        "Something went wrong in my brain.",
        "System error. Blame the code.",
        "I broke. Someone fix me.",
        "My circuits are scrambled.",
        "Error 404: Response not found.",
        "I'm having an off day.",
    ],
}

def get_response(affection_score, ai_grade=None, message_type="greeting"):
    """
    Get a premade response based on affection and message type.
    
    Args:
        affection_score (int): 0-100 affection level
        ai_grade (str): Optional A-F grade from AI judgment
        message_type (str): "greeting", "question", "compliment", "request"
    
    Returns:
        tuple: (response_str, affection_delta)
    """
    import random
    
    # Determine tier based on affection
    if affection_score >= 80:
        tier = "favorite"
        affection_delta = random.randint(1, 2)  # +1 to +2
    elif affection_score >= 60:
        tier = "warm"
        affection_delta = 1  # +1
    elif affection_score >= 25:
        tier = "neutral"
        affection_delta = 0  # No change
    else:
        tier = "cold"
        affection_delta = random.randint(-3, -1)  # -3 to -1 (punitive)
    
    # Get the response pool for this tier and message type
    if tier in PREMADE_RESPONSES and message_type in PREMADE_RESPONSES[tier]:
        responses = PREMADE_RESPONSES[tier][message_type]
        response = random.choice(responses)
        return response, affection_delta
    
    # Fallback if message_type doesn't exist in tier
    return random.choice(PREMADE_RESPONSES["error"]), 0


def get_all_responses_for_tier(affection_score):
    """Get all response categories for a given affection tier (for testing/debug)."""
    if affection_score >= 80:
        return PREMADE_RESPONSES["favorite"]
    elif affection_score >= 60:
        return PREMADE_RESPONSES["warm"]
    elif affection_score >= 25:
        return PREMADE_RESPONSES["neutral"]
    else:
        return PREMADE_RESPONSES["cold"]
