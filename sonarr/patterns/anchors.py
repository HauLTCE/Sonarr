import re

# ================== PRONOUN ANCHORS ==================
# Self Anchors - refers to the speaker
SELF_ANCHORS = r"\b(i|me|my|mine|myself|we|us|our|ours|im|i'm|ive|i've|id|i'd|ill|i'll)\b"

# Target Anchors - refers to the bot/listener  
TARGET_ANCHORS = r"\b(you|u|ur|your|yours|yourself|yall|y'all|bot|sonar|sonarr)\b"

# Object Anchors - refers to things/situations (this, that, it)
OBJECT_ANCHORS = r"\b(this|that|it)\b"

# Third-Party Anchors - refers to others
THIRD_PARTY_ANCHORS = r"\b(he|him|his|she|her|hers|they|them|their|theirs|bro|sis|man|girl|dude|guy|guys|everyone|everybody|someone|somebody|anyone|anybody|people|that person|this person)\b"

# Implied Third-Party - possessive + person reference (my mom, my friend, etc.)
IMPLIED_THIRD_PARTY = r"\b(my|your|his|her|their|our)\s+(mom|mother|dad|father|parent|parents|brother|sister|sibling|friend|friends|boss|teacher|coworker|colleague|neighbor|girlfriend|boyfriend|wife|husband|partner|ex|family|uncle|aunt|cousin|grandma|grandpa|grandmother|grandfather)\b"



# ================== PATTERN KEYWORDS ==================
NEGATIVE_ACTION_WORDS = r"\b(hate|hates|hating|hated|h8|dislike|dislikes|despise|despises|loathe|loathes|detest|detests|cant stand|can't stand|sick of|tired of|annoyed by|annoyed with|mad at|angry at|angry with|pissed at|pissed off at|furious at|furious with)\b"

INSULT_WORDS = r"\b(stupid|stupider|dumb|dumber|idiot|moron|retard|retarded|loser|pathetic|useless|worthless|trash|garbage|terrible|awful|ugly|uglier|suck|sucks|sucked|worst|worse|brainless|braindead|brain dead|moronic|idiotic|piece of shit|pos|dumbass|asshole|bastard|bitch|dick|crap|crappy|annoying|irritating|obnoxious|insufferable|unbearable|intolerable|lame|lamer|boring|basic|mean|meaner|weird|weirder|crazy|crazier|insane|dull|dense|denser|slow|slower|hopeless|incompetent|ridiculous|absurd|foolish|silly|sillier|naive|ignorant|rude|ruder|nasty|nastier|vile|disgusting|repulsive|gross|grosser|creepy|creepier|strange|stranger|odd|odder|nuts|mental|psycho|delusional|mid|cringe|cringier|salty|saltier|toxic|sus|suspicious|cap|capping|extra|clown|L|ratio|invalid|npc|simp|karen|boomer|tryhard|sweaty|noob)\b"

THREAT_WORDS = r"\b(kill|hurt|beat|fight|destroy|murder|attack|punch|hit|slap|kick|stab|shoot|strangle|choke|die|dead|death)\b"

AFFECTION_WORDS = r"\b(love|loves|loving|loved|like|likes|liked|adore|adores|adored|miss|misses|missed|missing|care about|cares about|appreciate|appreciates|cherish|cherishes|fond of|admire|admires|admired|admiring|respect|respects|respected|respecting|trust|trusts|trusted|trusting|enjoy|enjoys|enjoyed|enjoying|fancy|fancies|fancied|wonderful|amazing|awesome|great|greater|fantastic|incredible|brilliant|excellent|perfect|beautiful|lovely|cute|cuter|sweet|sweeter|cool|cooler|nice|nicer|kind|kinder|smart|smarter|clever|cleverer|intelligent|genius|talented|skilled|best|better|helpful|luv|luvs|based|goated|goat|fire|lit|slaps|slap|bussin|iconic|legend|legendary|valid|king|queen|slay|slaying|ate|real|elite|peak|W|dope|sick|tight|rad|pog|poggers|chad|gigachad)\b"

HELP_WORDS = r"\b(help|helps|helping|helped|assist|assists|assisting|assisted|support|supports|save|saves|need|needs|needed)\b"

QUESTION_WORDS = r"\b(what|why|how|when|where|who|which|can|could|would|will|should|do|does|did|is|are|was|were|have|has|had)\b"



