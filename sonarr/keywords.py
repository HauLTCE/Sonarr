"""
Keyword mapping for message classification.

Contains the keyword_map dictionary and stopwords for text processing.
"""

# Stopwords for filtering out common words during text processing
STOPWORDS = {
    "i", "me", "my", "myself", "we", "our", "you", "your", "he", "she", "it", "they",
    "the", "a", "an", "and", "but", "or", "for", "nor", "on", "at", "to", "from",
    "is", "am", "are", "was", "were", "be", "been", "being", "have", "has", "had",
    "do", "does", "did", "will", "would", "could", "should", "may", "might", "must",
    "of", "in", "with", "as", "by", "about", "into", "through", "during", "before",
    "after", "above", "below", "between", "under", "again", "further", "then", "once",
    "here", "there", "all", "each", "few", "more", "most", "other", "some", "such",
    "only", "own", "same", "so", "than", "too", "very", "just", "also", "now",
    "im", "ive", "id", "ill", "youre", "youve", "youll", "hes", "shes", "its",
    "were", "theyve", "theyll", "wont", "dont", "doesnt", "didnt", "cant", "couldnt",
    "really", "actually", "basically", "literally", "probably", "maybe", "kinda", "sorta",
    "like", "think", "know", "feel", "want", "need", "got", "get", "going", "gonna",
    "today", "right", "rn", "rly", "tho", "though", "even", "still", "already",
}

# Keywords mapped to categories for simple classification
KEYWORD_MAP = {
    "greeting": [
        "hello", "hi", "hey", "yo", "morning", "afternoon", "evening", "sup", "howdy", "greetings",
        "oi", "oii", "oiii", "ahoy", "ahoyyy",
        "hiya", "heya", "ello", "helo", "henlo", "hewwo", "hii", "hiii", "hiiii", "heyyy",
        "wassup", "whats up", "what's up", "wazzup", "waddup", "good morning", "good afternoon",
        "good evening", "gm", "good day", "salutations", "ayo", "ayoo", "yoo", "yooo",
    ],
    "thanks": [
        "thank", "thx", "ty", "thanks", "appreciate", "grateful", "thankyou", "thank you",
        "tysm", "tyvm", "thnx", "thnks", "cheers", "ta", "much appreciated",
        "thanks a lot", "thanks so much", "many thanks", "big thanks",
    ],
    "goodbye": [
        "bye", "goodbye", "later", "cya", "see you", "good night", "gn", "farewell", "peace out",
        "byebye", "bye bye", "bai", "buh bye", "ttyl", "gtg", "gotta go", "leaving", "im out",
        "i'm out", "peace", "take care", "see ya", "seeya", "laterz", "laters", "night",
        "nighty night", "goodnight", "gnite", "adios", "ciao", "sayonara",
    ],
    "question": [
        "what", "why", "how", "when", "where", "who", "which", "does", "can you", "is there",
        "do you", "are you", "will you", "would you", "could you", "should", "is it", "was it",
        "tell me", "explain", "whats", "what's", "hows", "how's", "whys", "whos", "who's",
        "anyone know", "does anyone", "wondering", "curious",
    ],
    "confusion": [
        "confused", "huh", "wat", "wut", "????", "?????", "idk", "don't understand", "makes no sense",
        "what do you mean", "wdym", "i dont get it", "i don't get it", "dont get it",
        "lost", "im lost", "i'm lost", "unclear", "no idea", "clueless", "bewildered",
        "puzzled", "perplexed", "baffled", "wtf", "huhh", "huhhh", "ehh", "uhh", "umm",
    ],
    "insult": [
        "stupid", "idiot", "dumb", "trash", "fuck", "shit", "ass", "bitch", "suck", "worst", "hate",
        "moron", "retard", "loser", "pathetic", "useless", "garbage", "worthless", "terrible",
        "awful", "disgusting", "ugly", "dumbass", "asshole", "bastard", "dick", "piss off",
        "screw you", "go away", "shut up", "stfu", "kys", "die", "kill yourself", "brain dead",
        "braindead", "moronic", "idiotic", "brainless", "crap", "crappy", "sucks", "sucked",
    ],
    "affection": [
        "love", "like you", "miss", "adore", "crush", "heart", "fond",
        "care about", "love you", "luv", "ily", "ilysm", "i love", "loving", "beloved",
        "darling", "sweetheart", "dear", "precious", "cherish", "devotion", "soulmate",
    ],
    "vent": [
        "sad", "depressed", "cry", "upset", "unhappy", "miserable", "heartbroken",
        "crying", "tears", "sob", "sobbing", "weep", "weeping", "devastated", "broken",
        "hurt", "hurting", "pain", "painful", "suffering", "grief", "grieving", "mourning",
        "hopeless", "despair", "lonely", "alone", "isolated", "down", "feeling down",
        "bummed", "gutted", "shattered", "melancholy", "gloomy", "blue", "venting",
    ],
    "excitement": [
        "happy", "yay", "excited", "great", "awesome", "amazing", "wonderful",
        "joy", "joyful", "cheerful", "glad", "pleased", "delighted", "thrilled", "ecstatic",
        "overjoyed", "elated", "euphoric", "fantastic", "excellent", "brilliant", "superb",
        "terrific", "marvelous", "splendid", "glorious", "blessed", "fortunate", "lucky",
        "yayyy", "yayy", "woohoo", "woo hoo", "hooray", "hurray", "yippee", "wooo", "hype",
    ],
    "complaint": [
        "angry", "mad", "furious", "pissed", "annoyed", "irritated",
        "rage", "raging", "enraged", "livid", "fuming", "seething", "outraged", "infuriated",
        "frustrated", "aggravated", "agitated", "heated", "triggered", "tilted",
        "pissed off", "ticked off", "fed up", "had enough", "losing it", "complain", "ugh",
    ],
    "joke": [
        "lol", "haha", "lmao", "rofl", "hilarious", "funny", "lmfao",
        "hahaha", "hahahaha", "lolol", "lololol", "xd", "xdd", "xddd",
        "kek", "kekw", "lul", "lulw", "omegalul", "laughing", "laugh",
        "dying", "im dead", "i'm dead", "deceased", "comedy", "joke",
        "hehe", "hehehe", "teehee", "giggle", "chuckle", "wheeze", "snort",
    ],
    "help": [
        "help", "how to", "how do", "assist", "support", "guide", "tutorial",
        "can someone help", "need help", "help me", "help pls", "help please",
        "assistance", "tips", "suggestion", "recommend", "recommendation",
        "stuck", "issue", "problem", "trouble", "struggling", "cant figure", "can't figure",
    ],
    "agreement": [
        "yes", "right", "correct", "yep", "yeah", "ok", "okay", "true", "agreed", "exactly",
        "yea", "ya", "yah", "yup", "yupp", "yuppp", "yessir", "yes sir", "affirmative",
        "absolutely", "definitely", "certainly", "indeed", "precisely", "totally",
        "for sure", "of course", "sure", "surely", "ofc", "facts", "fax", "fr", "for real",
        "same", "ikr", "i know right", "this", "based", "w", "dub",
    ],
    "disagreement": [
        "no", "wrong", "nope", "nah", "false", "disagree", "incorrect",
        "naw", "nay", "negative", "not really", "dont think so", "don't think so",
        "i disagree", "hard no", "hell no", "absolutely not", "no way", "noway",
        "cap", "thats cap", "that's cap", "bs", "bullshit", "lies", "lying",
        "doubt", "doubtful", "skeptical", "sus", "suspicious", "l", "ratio",
    ],
    "bored": [
        "bored", "boring", "dull", "nothing to do", "meh",
        "im bored", "i'm bored", "so bored", "boredom", "monotonous", "tedious",
        "uninteresting", "bland", "stale", "dry", "dead chat", "dead server",
        "entertain me", "amuse me", "someone talk", "anyone there",
    ],
    "flirt": [
        "flirt", "handsome", "pretty", "cute", "beautiful", "hot", "sexy", "gorgeous",
        "attractive", "good looking", "stunning", "lovely", "charming", "fine",
        "marry me", "date me", "be mine", "kiss", "hug", "cuddle", "snuggle",
        "wink", "smooch", "babe", "baby", "honey", "sweetie", "cutie", "hottie",
    ],
    "brag": [
        "pro", "best", "god", "amazing at", "skilled", "expert", "legend",
        "goat", "greatest", "legendary", "insane", "cracked", "goated", "built different",
        "im the best", "i'm the best", "too good", "ez", "easy", "ezpz", "gg ez",
        "destroyed", "dominated", "owned", "rekt", "wrecked", "smashed",
    ],
    "flex": [
        "money", "dollar", "$", "cash", "rich", "pay", "coin", "balance",
        "wealth", "wealthy", "fortune", "millionaire", "billionaire", "bank", "wallet",
        "earn", "earnings", "income", "salary", "wage", "profit",
        "expensive", "bought", "new car", "new phone", "flex", "drip",
    ],
    "beg": [
        "poor", "broke", "give me", "can i have", "please give", "need money",
        "spare", "donate", "donation", "pls", "plz", "plsss", "pretty please",
        "i need", "can you give", "gimme", "lend me", "borrow",
    ],
    "chitchat": [
        "eat", "hungry", "food", "yummy", "delicious", "dinner", "lunch", "breakfast", "snack",
        "eating", "meal", "starving", "famished", "appetite", "tasty", "yum",
        "pizza", "burger", "fries", "chicken", "rice", "noodles", "pasta", "steak",
        "cook", "cooking", "chef", "recipe", "restaurant", "takeout", "delivery",
        "weather", "today", "weekend", "plans", "doing",
    ],
    "advice": [
        "sleep", "sleepy", "tired", "exhausted", "rest", "nap", "zzz", "drowsy",
        "sleeping", "bedtime", "bed", "insomnia", "cant sleep", "can't sleep",
        "fatigue", "fatigued", "worn out", "drained", "burned out", "burnout",
        "yawn", "yawning", "passing out", "knocked out", "dead tired",
        "should i", "what should", "advice", "recommend",
    ],
    "compliment": [
        "good bot", "nice bot", "best bot", "smart", "clever", "impressive",
        "well done", "good job", "nice job", "great job", "awesome bot",
        "you're cool", "youre cool", "ur cool", "love this bot", "like you",
    ],
    "request": [
        "can you", "could you", "would you", "will you", "please", "pls",
        "do this", "do that", "make me", "get me", "show me", "tell me",
        "i want", "i need", "give me", "send me",
    ],
    "apology": [
        "sorry", "my bad", "apologize", "apologies", "forgive", "forgive me",
        "i apologize", "im sorry", "i'm sorry", "didnt mean", "didn't mean",
        "my fault", "i was wrong", "mb", "sry", "srry",
    ],
    "statement": [
        "i think", "i believe", "in my opinion", "imo", "personally",
        "just saying", "fyi", "btw", "by the way", "fun fact",
        "did you know", "apparently", "actually", "honestly",
    ],
    "sarcasm": [
        "wow", "oh wow", "amazing", "incredible", "unbelievable", "shocking",
        "no way", "really", "oh really", "you dont say", "you don't say",
        "totally", "sure jan", "right", "uh huh", "mhm", "suuure",
    ],
    "threat": [
        "fight me", "1v1", "square up", "catch these hands", "pull up",
        "ill beat", "i'll beat", "gonna hurt", "watch out", "be careful",
        "or else", "youll regret", "you'll regret", "dont make me", "don't make me",
    ],
    "command": [
        "do it", "just do", "now", "right now", "immediately", "hurry",
        "faster", "quickly", "go", "come", "stop", "start", "obey",
    ],
    "praise": [
        "good", "nice", "well done", "perfect", "excellent", "great work",
        "amazing work", "impressive", "proud", "respect", "props", "kudos",
    ],
    "spam": [
        "aaa", "aaaa", "aaaaa", "asdf", "qwerty", "spam", "test",
        "123", "1234", "12345", "abcd", "lskdjf", "dkfjsl",
    ],
    "excuse": [
        "because", "but", "its not my fault", "it's not my fault", "wasnt me", "wasn't me",
        "i didnt", "i didn't", "not my", "blame", "fault",
    ],
    "overshare": [
        "tmi", "too much info", "personal", "private", "secret",
        "dont tell", "don't tell", "between us", "confession", "confess",
    ],
    "challenge": [
        "bet", "dare", "challenge", "prove it", "i bet", "wanna bet",
        "try me", "test me", "lets see", "let's see", "show me",
    ],
    "opinion": [
        "think", "opinion", "feel like", "seems like", "looks like",
        "i reckon", "i guess", "suppose", "probably", "maybe",
    ],
    "lie": [
        "lying", "liar", "fake", "cap", "thats cap", "that's cap",
        "not true", "false", "bs", "bullshit", "you lie",
    ],
    "guilt": [
        "feel bad", "guilty", "regret", "wish i", "shouldnt have", "shouldn't have",
        "my fault", "blame myself", "i ruined", "messed up",
    ],
    "random": [
        "hmm", "meh", "whatever", "anyway", "so", "well",
        "dunno", "i guess", "perhaps", "possibly", "idc", "dont care",
        "don't care", "doesnt matter", "doesn't matter", "who cares", "bruh", "bruhhh",
        "lmk", "let me know", "ngl",
    ],
}

# Keywords that indicate negative sentiment (for rudeness detection)
NEGATIVE_KEYWORDS = [
    "fuck", "shit", "asshole", "bitch", "stupid", "dumb", 
    "idiot", "trash", "worst", "hate", "die", "kill"
]
