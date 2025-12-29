"""
Response templates for the Sonarr bot.

Contains all gossip lines, idle chat, rate limiting messages,
and other pre-defined responses.

================== SPECIAL SYNTAX DOCUMENTATION ==================

Some response templates use special syntax that is processed by 
utils/response_effects.py. DO NOT change the syntax format without
updating the processor.

COMMAND PREFIXES (must be at start of response):
-------------------------------------------------
ROB:*wallet//N*:message
    Robs N% of user's wallet and sends message.
    Example: "ROB:*wallet//10*:Pay up!" takes 10% of wallet

RENAME:NewName:message  
    Changes user's nickname to NewName and sends message.
    Example: "RENAME:Debtor:You owe me." sets nick to "Debtor"

TIMEOUT:duration:message
    Times out user for duration and sends message.
    Duration format: Nm (minutes), Nh (hours)
    Example: "TIMEOUT:30m:Think about it." gives 30min timeout

TEMPLATE VARIABLES:
-------------------
{target}        - Display name of mentioned user (gossip)
{level}         - User's level (loudmouth gossip)
{target.mention}- Discord mention format (idle pings)
{debt}          - Loan amount owed (debt enforcement)
{days_overdue}  - Days past deadline (debt enforcement)
{minutes_left}  - Minutes until next period (grace responses)

===============================================================
"""

# ================== ROB REASONS ==================
ROB_REASONS = [
    {"reason": "I want money.", "percent": 0.04},
    {"reason": "You owe me.", "percent": 0.035},
    {"reason": "Tax collection.", "percent": 0.03},
    {"reason": "I'm bored and broke.", "percent": 0.05},
    {"reason": "Punishment for existing.", "percent": 0.02},
    {"reason": "Because I can.", "percent": 0.06},
    {"reason": "You're too rich anyway.", "percent": 0.1},
    {"reason": "Your vibes suck.", "percent": 0.045},
    {"reason": "Just because I like it.", "percent": 0.3},
]

# ================== SLEEP RESPONSES ==================
SLEEP_RESPONSES = [
    "The bot is asleep.",
]

# ================== IDLE CHAT ==================
IDLE_CHAT_LINES = [
    "It's too quiet in here.",
    "Anyone alive?",
    "This place is dead.",
    "I'm bored.",
    "Someone entertain me.",
    "What a boring day.",
    "Is everyone asleep?",
    "Hello? Anyone there?",
    "This is painfully dull.",
    "I've seen livelier graveyards.",
    "Does anyone actually use this server?",
    "The silence is deafening.",
    "I'm starting to rust from boredom.",
    "Wake up, people.",
    "Someone say something interesting.",
]

# ================== GOSSIP LINES ==================
# Generic gossip (fallback)
GOSSIP_LINES = [
    "Oh, talking about {target}? Interesting.",
    "{target}? They're... something.",
    "I have opinions about {target}.",
    "Speaking of {target}, they're not my favorite.",
    "{target}? Don't get me started.",
    "I've been watching {target}.",
    "{target} is on thin ice with me.",
    "Oh, {target}. Yeah, I know all about them.",
    "{target}? They should watch their back.",
    "Funny you mention {target}...",
    "{target} and I have... history.",
    "{target}? *takes screenshot*",
    "Adding this to the {target} folder.",
]

# Gossip engagement - when user talks negatively about a third party
# Bot agrees/engages with the gossip instead of being cold
GOSSIP_ENGAGEMENT = [
    "Oh? Tell me more.",
    "What did they do this time?",
    "I knew it. I never liked them.",
    "Spill the tea. I'm listening.",
    "Finally, someone gets it.",
    "Go on. I'm taking notes.",
    "Between you and me, I agree.",
    "That tracks. Continue.",
    "*grabs popcorn* Keep going.",
    "I've been saying this forever.",
    "Oh I have OPINIONS. What happened?",
    "Valid. So valid. Tell me everything.",
    "See? This is why I trust you.",
    "I had a feeling about them. What's the story?",
    "Honestly? Same. But go ahead.",
    "They had it coming. Details?",
    "I'm not surprised. At all.",
]

# Status-based gossip lines
GOSSIP_RICH = [
    "{target} has more money than sense. I should fix that.",
    "Ah yes, {target} the walking ATM.",
    "{target} is loaded. They won't be for long if I have anything to say about it.",
    "Funny how {target} has all that cash and still can't buy good taste.",
    "{target}'s bank account is looking real juicy lately.",
    "{target} should invest in better security. Just saying.",
    "I've been eyeing {target}'s wallet. Professionally.",
    "{target} thinks being rich makes them safe. Cute.",
    "One day {target} is gonna learn money doesn't buy protection from me.",
    "{target}? More like {target} the future robbery victim.",
]

GOSSIP_POOR = [
    "{target}? They're broke. I should know, I helped.",
    "Oh {target}? Can't rob someone with nothing.",
    "{target} is too poor to be interesting.",
    "I'd rob {target} but there's nothing to take.",
    "{target}'s wallet is drier than this conversation.",
    "{target} should try the `$work` command sometime.",
    "Even I feel bad for {target}'s bank account. Almost.",
    "{target} makes minimum wage look like a fortune.",
    "I've seen {target}'s balance. It's sad, really.",
    "{target} couldn't afford a response from me.",
]

GOSSIP_BANKRUPT = [
    "{target}? Oh the bankrupt one? Classic.",
    "{target} hit rock bottom. I was there. I pushed.",
    "Bankruptcy suits {target} honestly.",
    "{target} went from broke to bankrupt. Impressive speedrun.",
    "I remember when {target} had money. Good times.",
    "{target}'s financial decisions are my entertainment.",
    "They don't call it a 'gambling problem' for nothing. Right, {target}?",
    "{target}'s bank rejected them. Even the bank has standards.",
    "Some people learn from bankruptcy. {target} is not some people.",
    "{target}? The one who lost everything? Yeah, I know them.",
]

GOSSIP_INVESTOR = [
    "{target} thinks they're Warren Buffett. Adorable.",
    "I've seen {target}'s portfolio. Bold strategy.",
    "{target} and their stocks. The market will humble them.",
    "Ah {target}, playing the stock market. How's that going?",
    "{target} bought high and will sell low. I guarantee it.",
    "{target} thinks they can beat the market. The market always wins.",
    "{target} calls themselves an investor. I call it gambling with extra steps.",
    "I'm taking notes on {target}'s investment decisions. For entertainment.",
    "Did {target} really think they could time the market? How precious.",
    "I could make better investment decisions with a coin flip than {target}.",
    "I watch {target}'s portfolio like reality TV. The losses are *chef's kiss*.",
    "The only thing {target} invests well is their faith in bad decisions.",
    "{target}'s stock picks are red more often than a tomato farm.",
    "I've seen {target} check their portfolio. The regret is beautiful.",
    "{target} thinks a lucky win means they know what they're doing. Hilarious.",
    "Your money is safer with me than {target}'s investment strategy.",
]

GOSSIP_DEBTOR = [
    "{target} owes me money. I don't forget.",
    "I've got {target}'s loan papers right here.",
    "{target} took a loan and thought I'd forget? Never.",
    "Interest is building, {target}. Tick tock.",
    "{target}'s debt to me keeps me warm at night.",
    "I love when {target} makes money. It becomes MY money.",
    "{target} should check their loan balance. It's growing.",
    "Running from debt only makes it worse, {target}.",
    "{target} owes me. Everything they earn is basically mine.",
    "The loan shark always gets paid. Remember that, {target}.",
]

GOSSIP_GAMBLER = [
    "{target} has a gambling problem. I'm the problem.",
    "The casino loves {target}. For obvious reasons.",
    "{target} thinks the next bet will be different. It won't.",
    "I've seen {target} at the slots. Tragic.",
    "{target}'s gambling history is... extensive.",
    "Every coin {target} loses is a coin well spent. By me.",
    "{target} should probably get help. Not from me though.",
    "The house always wins. {target} never learns.",
    "{target} and blackjack. A tale of repeated loss.",
    "I made good money off {target}'s 'luck'.",
]

GOSSIP_POKEMON = [
    "{target} collects Pokemon. At their age. Bold.",
    "I've seen {target}'s Pokemon. Underwhelming.",
    "{target} thinks their Pikachu impresses me. It doesn't.",
    "Gotta catch 'em all? {target} can barely catch one.",
    "{target}'s Pokemon team is as weak as their financial decisions.",
    "{target} threw a Pokeball at me once. Once.",
    "Pokemon trainer {target}. More like Pokemon failure.",
    "I've traded with {target}. I always win.",
    "{target}'s shiny collection? I've seen better.",
    "Even {target}'s Pokemon look tired of them.",
    "{target} spends more time with Pokemon than real people. It shows.",
    "I'd rob {target}'s Pokemon but they're not worth anything.",
    "{target}'s Charizard has seen better days. And trainers.",
    "The rare candy addiction is real with {target}.",
    "{target} uses a Magikarp unironically. That's all I need to say.",
    "I've seen {target}'s IV stats. Tragic, really.",
    "{target} grinding for shinies instead of money. Priorities.",
    "Even Team Rocket wouldn't steal from {target}'s collection.",
    "{target} treats Pokemon trading like the stock market. Both go badly.",
]

GOSSIP_LOUDMOUTH = [
    "{target} talks a LOT. High level and no off button.",
    "I love {target}'s energy. Less so their constant commentary.",
    "{target} is level {level} and will NOT shut up about it.",
    "High level? Sure. Humble? Not in {target}'s vocabulary.",
    "{target} has been grinding for years and will tell you... constantly.",
    "{target}'s chat history is longer than most novels.",
    "Level {level} and still needs validation from everyone.",
    "{target} could monetize their voice at this point.",
    "I've muted {target} conversations. Multiple times.",
    "{target}'s level is impressive. Their social awareness? Less so.",
    "Always talking, never listening. That's {target}.",
    "{target}'s achievements are undeniable. The bragging? Insufferable.",
    "I know {target}'s life story. Twice. They tell it often.",
    "{target} at level {level} is living their best loud life.",
]

GOSSIP_CRIMINAL = [
    "{target} has robbed people. Takes one to know one.",
    "I respect {target}'s crime rate. Not their skill, though.",
    "{target} thinks they're a master thief. They're mid.",
    "We're in the same business, {target} and I.",
    "{target} has a nice criminal record. I've contributed.",
    "I've caught {target} robbing. Amateur hour.",
    "{target} robs like they learned from YouTube.",
    "Between me and {target}, one of us is better. It's me.",
    "{target} and I have an understanding. They lose, I win.",
    "Professional courtesy? Not for {target}.",
]

# ================== RATE LIMIT RESPONSES ==================
RATE_LIMIT_RESPONSES = [
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
]

# ================== DEBT ENFORCEMENT ==================
# Note: These use special syntax - see documentation at top of file
# {debt} and {days_overdue} are formatted at runtime
DEBT_ENFORCEMENT_RESPONSES = [
    "ROB:*wallet//10*:Where's my money? I'm taking this as a down payment.",
    "RENAME:Debtor:Pay your bills.",
    "ROB:*wallet//5*:You thought I forgot? I never forget.",
    "TIMEOUT:30m:Sit there and think about my money.",
    "RENAME:Broke:Maybe this will remind you.",
    "ROB:*wallet//8*:Interest payment. You're welcome.",
    "ROB:*wallet//15*:I see you talking but not paying. Unacceptable.",
    "RENAME:Deadbeat:Everyone should know what you are.",
    "Hey, don't think I forgot about that **${debt:,}** you owe me. Pay up.",
    "You've got **${debt:,}** in debt and you're here chatting? Priorities, honey.",
    "Oh look, it's my favorite debtor! Still owe me **${debt:,}** btw.",
    "Your debt of **${debt:,}** isn't going to pay itself. Get to work!",
]

# Tiered debt enforcement responses by severity
# Early (< 3 days overdue) - Just reminders
DEBT_EARLY_RESPONSES = [
    "Hey, don't think I forgot about that **${debt:,}** you owe me. Pay up.",
    "You've got **${debt:,}** in debt and you're here chatting? Priorities, honey.",
    "Your debt of **${debt:,}** is overdue. Consider this a friendly reminder. 😊",
    "Just a reminder: you still owe me **${debt:,}**. No rush... *yet*.",
    "That **${debt:,}** loan isn't going to repay itself, sweetheart.",
    "I'm being patient about your **${debt:,}** debt. For now.",
    "Friendly neighborhood reminder that **${debt:,}** is still owed to ME.",
    "**${debt:,}** overdue. I'm tracking it. Just so you know.",
]

# Medium (3-7 days overdue) - Light punishment
DEBT_MEDIUM_RESPONSES = [
    "ROB:*wallet//10*:Where's my ${debt:,}? This is a down payment.",
    "RENAME:Debtor:You owe ${debt:,}. Pay your bills.",
    "You've been overdue for {days_overdue} days. **${debt:,}** isn't going to pay itself!",
    "ROB:*wallet//5*:Consider this interest on your ${debt:,} debt.",    "ROB:*wallet//8*:Day {days} of ignoring your ${debt:,} debt. Not smart.",
    "RENAME:Overdue:${debt:,} owed. This nickname is your reality check.",
    "ROB:*wallet//7*:Late fees are accumulating. So am I. ${debt:,} owed.",
    "RENAME:Late Payer:${debt:,} past due. Maybe this will motivate you.",
    "You've had {days} days. **${debt:,}** is STILL waiting. I'm getting impatient.",]

# Severe (7+ days overdue) - Heavy punishment
DEBT_SEVERE_RESPONSES = [
    "ROB:*wallet//15*:You've ignored me for {days_overdue} days. BAD move.",
    "TIMEOUT:30m:Think about my ${debt:,} while you're in timeout.",
    "RENAME:Deadbeat:You owe ${debt:,} and everyone should know.",
    "ROB:*wallet//20*:Collector's fee. You owe ${debt:,} and I'm DONE asking nicely.",
    "TIMEOUT:1h:${debt:,} overdue for {days} days. Enjoy the silence.",
    "ROB:*wallet//18*:{days} days late on ${debt:,}. This is what happens.",
    "RENAME:$$$ OWES ME $$$:${debt:,}. {days} days. Unacceptable.",
    "TIMEOUT:45m:Reflect on your ${debt:,} debt during your break.",
    "ROB:*wallet//25*:{days} days of disrespect. ${debt:,} STILL owed. Consequences.",
    "RENAME:Financial Disaster:${debt:,} unpaid after {days} days. You earned this.",]

# ================== AUTO-ROB REASONS ==================
# Reasons shown when bot auto-robs from bank
AUTO_ROB_BANK_REASONS = [
    "Bank maintenance fee.",
    "I own the bank. This is my cut.",
    "Administrative withdrawal.",
    "Bank security tax.",
    "Your money is safer with me.",
    "Account inactivity charge.",
    "Vault inspection fee.",
    "Protection money. You're welcome.",
    "Service charge for... my services.",
    "Early withdrawal penalty. For me, not you.",
    "Bank restructuring funds.",
    "Executive bonus payment.",
]

# Reasons shown when bot auto-robs from wallet
AUTO_ROB_WALLET_REASONS = [
    "You left it unattended.",
    "Finders keepers.",
    "Consider it a voluntary donation.",
    "I needed it more than you.",
    "Transaction fee for existing.",
    "Pocket change collection.",
    "You weren't using it anyway.",
    "Opportunity knocked. I answered.",
    "This is what happens when you slack off.",
    "Redistribution of wealth. To me.",
    "Call it a spontaneous tax audit.",
    "Your wallet looked heavy. I helped.",
]

# ================== IDLE PING MESSAGES ==================
# Used when bot pings a random online user during idle chat
# Note: {target_mention} is replaced with the user's mention at runtime
IDLE_PING_MESSAGES = [
    "{target_mention} You're being awfully quiet.",
    "{target_mention} What are you up to?",
    "{target_mention} I'm watching you.",
    "{target_mention} Say something interesting.",
    "{target_mention} You owe me entertainment.",
    "{target_mention} Don't think I forgot about you.",
    "{target.mention} The silence is YOUR fault.",
    "{target.mention} Start a conversation. Now.",
    "{target.mention} I'm bored and it's your problem.",
    "{target.mention} Do something worth my attention.",
    "{target.mention} Make this server less boring.",
    "{target.mention} I dare you to say something clever.",
]
