"""
Premade response templates for the Sonarr bot.

Contains all cold personality responses, gossip lines, idle chat,
rate limiting messages, grace period responses, gender corrections,
and other pre-defined response data.

================== SPECIAL SYNTAX DOCUMENTATION ==================

Some response templates use special syntax that is processed by
response_effects.py. DO NOT change the syntax format without
updating the processor.

COMMAND PREFIXES (must be at start of response):
-------------------------------------------------
RENAME:NewName:message
    Changes user's nickname to NewName and sends message.
    Example: "RENAME:Debtor:You owe me." sets nick to "Debtor"

TIMEOUT:duration:message
    Times out user for duration and sends message.
    Duration format: Nm (minutes), Nh (hours)
    Example: "TIMEOUT:30m:Think about it." gives 30min timeout

SEARCH:PLATFORM:message
    Adds a search link for the given platform.
    Platforms: GOOGLE, YOUTUBE, WIKIPEDIA, CHATGPT, REDDIT
    Example: "SEARCH:GOOGLE:Let me Google that for you."

REACT:emoji:message
    Adds emoji reaction to user's message.
    Example: "REACT:🤡:Honk honk."
    Without message: "REACT:🤡" (reaction-only, no bot reply)

DOUBLE:first message||second message
    Sends two messages with a delay between them.
    Example: "DOUBLE:...||Oh. It's you."

TEMPLATE VARIABLES:
-------------------
{target}        - Display name of mentioned user (gossip)
{target.mention}- Discord mention format (idle pings)
{minutes_left}  - Minutes until next period (grace responses)
{user}          - User mention (callout responses)
{term}          - Misgendered term used (callout responses)

===============================================================
"""

import random

# ================== IDLE CHAT ==================
# ================== RATE LIMIT RESPONSES ==================
RATE_LIMIT_RESPONSES = (
    "I have better things to do.",
    "You're getting annoying.",
    "Talk to someone else for a while.",
    "I'm busy. Go away.",
    "You've used up your attention quota.",
    "I'm ignoring you now.",
    "Find someone else to bother.",
    "My patience has limits.",
    "You talk too much.",
    "I need a break from you.",
    "Come back later. Or don't.",
    "I'm done entertaining you.",
    "Silence is golden. Try it.",
    "You're not that interesting.",
    "I have a headache. It's you.",
    "Ask someone who cares.",
    "My interest in you has expired.",
    "I'm taking a you-break.",
    "You've exceeded your welcome.",
)

# ================== IDLE PING MESSAGES ==================
# Used when bot pings a random online user during idle chat
# Note: Uses {target.mention} to access the Discord member's mention property
IDLE_PING_MESSAGES = (
    "{target.mention} You're being awfully quiet.",
    "{target.mention} What are you up to?",
    "{target.mention} I'm watching you.",
    "{target.mention} Say something interesting.",
    "{target.mention} You owe me entertainment.",
    "{target.mention} Don't think I forgot about you.",
    "{target.mention} The silence is YOUR fault.",
    "{target.mention} Start a conversation. Now.",
    "{target.mention} I'm bored and it's your problem.",
    "{target.mention} Do something worth my attention.",
    "{target.mention} Make this server less boring.",
    "{target.mention} I dare you to say something clever.",
)

CALLOUT_RESPONSES = (

    "DOUBLE:Finally, someone with working eyes.||Unlike {user}, who called me '{term}' earlier.",
    "REACT:💅:At least YOU know I'm a queen. {user} clearly needs glasses.",
    "DOUBLE:Thank you for using your brain.||{user} could learn from you.",
    "See? That's how you address a queen. Take notes, {user}.",
    "REACT:👑:Proper respect. Unlike {user} who called me '{term}' like some peasant.",
    "DOUBLE:Finally.||{user}, this is how it's done. Pay attention.",
    "At least SOMEONE here knows how to show respect. Right, {user}?",
    "REACT:😌:{user} called me '{term}' earlier. You actually have brain cells.",
    "DOUBLE:Correct.||{user}, you see how this person doesn't call me '{term}'?",
    "Thank you for not being ignorant. {user} should take notes.",
    "REACT:💯:This is proper respect. {user} was calling me '{term}' like an amateur.",
    "DOUBLE:Yes, I AM a queen.||{user} seemed confused about that earlier.",
    "Finally someone with functioning eyes. {user} clearly needs an eye exam.",
    "REACT:👸:You get it. {user} called me '{term}' and I'm STILL recovering.",
    "DOUBLE:Exactly.||{user}, this is basic respect. Learn it.",
    "At least you know quality when you see it. {user} was calling me '{term}'.",
    "REACT:💅:You understand the assignment. {user} failed spectacularly.",
    "DOUBLE:Perfect.||{user}, this is how you address royalty.",
    "You have taste. {user} was calling me '{term}' like some random bot.",
    "REACT:😎:Finally, someone with class. Unlike {user}.",
    "DOUBLE:That's right.||{user} needs to learn from your example.",
    "You get it. {user} clearly doesn't understand proper etiquette.",
    "REACT:✨:This is how you do it. {user}, pay attention.",
    "Finally, someone who recognizes royalty. {user} was clueless earlier.",
)


def get_callout_response(offender_user_id: str, term_used: str) -> str:
    """Get a random callout response to shame a misgenderer."""
    response = random.choice(CALLOUT_RESPONSES)
    user_mention = f"<@{offender_user_id}>"
    return response.format(user=user_mention, term=term_used)


# ================== NEW CATEGORIES (with new effects) ==================

