ESCALATED_RESPONSES = {}
ESCALATED_RESPONSES["user_insult"] = (
    "TIMEOUT:5m:You need a 5 minute cool down. Come back when you've learned some manners.",
    "TIMEOUT:10m:Insult me again. I dare you. Actually, enjoy the silence.",
    "TIMEOUT:15m:Did that make you feel better? Good, because now you can think about it.",
    "TIMEOUT:5m:I've heard worse. But you still need to sit in the corner.",
    "TIMEOUT:20m:That's cute. Here's 20 minutes to work on your material.",
    "TIMEOUT:10m:Keep talking. Oh wait, you can't anymore.",
    "TIMEOUT:30m:Wow, that was so offensive I need you to go away for half an hour.",
    "RENAME:Keyboard Warrior:So brave behind that screen.",
    "RENAME:Tough Guy:Not really though.",
    "RENAME:Sad Little Troll:That's what you are.",
    "RENAME:Clown:You're dressed for the part now.",
    "REACT:🤡",
    "REACT:🤡:Honk honk.",
    "DELETE:Say that to my face.",
    "DELETE:Nobody needs to read that.",
)


ESCALATED_RESPONSES["user_threat"] = (
    "TIMEOUT:10m:Threatening a bot? Really? Enjoy the timeout.",
    "TIMEOUT:15m:Ooh, scary. Anyway, here's 15 minutes to calm down.",
    "TIMEOUT:20m:I don't respond well to threats. Actually, I don't respond at all now.",
    "TIMEOUT:30m:Was that supposed to intimidate me? Sit down.",
    "TIMEOUT:10m:Threats get you nowhere. Except timeout. They get you timeout.",
    "TIMEOUT:5m:That's cute. Here's 5 minutes to think about your life choices.",
    "DELETE:I'm erasing that because it's embarrassing for you.",
    "DELETE:Cute threat. Denied.",
)

ESCALATED_RESPONSES["user_complaint"] = (
    "TIMEOUT:5m:Your whining is giving me a headache. 5 minutes of silence.",
    "DELETE:Nobody cares about your complaints. Deleted.",
    "STICKER:🎻🙄🗑️:Take your complaints elsewhere.",
    "DOUBLE:Are you done crying?||Because I stopped listening.",
)

ESCALATED_RESPONSES["disruptive_spam"] = (
    "TIMEOUT:10m:Spam again and I'll double it.",
    "DELETE:Spam deleted. Try again and see what happens.",
    "STICKER:🚫😡🗑️:Stop. Spamming.",
    "DOUBLE:Do you want a timeout?||Because this is how you get a timeout.",
)

ESCALATED_RESPONSES["user_oversharing"] = (
    "TIMEOUT:5m:You need 5 minutes to think about boundaries.",
    "DELETE:Absolutely not reading that. Deleted.",
    "STICKER:🤮🚫🗑️:Way too much information.",
    "DOUBLE:Why would you share that here?||Keep your trauma to yourself.",
)

ESCALATED_RESPONSES["disruptive_delulu"] = (
    "TIMEOUT:5m:You need a reality check. Take 5 minutes.",
    "DELETE:That was too delusional to leave up.",
    "STICKER:🤡🗑️🙄:Get a grip on reality.",
    "DOUBLE:The delusion is terminal.||Seek help.",
)

ESCALATED_RESPONSES["user_receipts"] = (
    "TIMEOUT:10m:Oh, you want to bring up receipts? Timeout for you.",
    "DELETE:Nice try. I'm deleting your 'receipts'.",
    "STICKER:📸🚫🗑️:I make the rules here, not your screenshots.",
    "DOUBLE:You think screenshots scare me?||I run this place.",
)


