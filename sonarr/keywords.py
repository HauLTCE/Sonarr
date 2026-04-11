"""
Keyword mapping for message classification.

Contains the keyword_map dictionary, stopwords, and negative sentiment filters.
Expanded for modern Discord slang, gamer terminology, and broader context awareness.
"""

# Stopwords for filtering out common words during text processing
# Expanded to include more text-speak and fillers
STOPWORDS = {
    "i", "me", "my", "myself", "we", "our", "ours", "us", "you", "your", "yours",
    "he", "she", "it", "they", "them", "their", "theirs", "him", "her",
    "the", "a", "an", "and", "but", "or", "for", "nor", "on", "at", "to", "from",
    "is", "am", "are", "was", "were", "be", "been", "being", "have", "has", "had",
    "do", "does", "did", "doing", "will", "would", "could", "should", "may", "might", "must",
    "of", "in", "with", "as", "by", "about", "into", "through", "during", "before",
    "after", "above", "below", "between", "under", "again", "further", "then", "once",
    "here", "there", "when", "where", "why", "how", "all", "any", "both", "each",
    "few", "more", "most", "other", "some", "such", "no", "nor", "not", "only",
    "own", "same", "so", "than", "too", "very", "s", "t", "can", "just", "don",
    "should", "now", "d", "ll", "m", "o", "re", "ve", "y", "ain", "aren",
    "couldn", "didn", "doesn", "hadn", "hasn", "haven", "isn", "ma", "mightn",
    "mustn", "needn", "shan", "shouldn", "wasn", "weren", "won", "wouldn",
    # Slang / Text-speak contractions
    "im", "ive", "id", "ill", "youre", "youve", "youll", "hes", "shes", "its",
    "were", "theyve", "theyll", "wont", "dont", "doesnt", "didnt", "cant", "couldnt",
    "wouldnt", "shouldnt", "isnt", "arent", "wasnt", "werent", "hasnt", "havent",
    "u", "ur", "r", "b", "c", "n", "tho", "though", "rlly", "rly", "actually",
    "basically", "literally", "probably", "maybe", "kinda", "sorta", "like",
    "think", "know", "feel", "want", "need", "got", "get", "going", "gonna",
    "wanna", "gotta", "finna", "tryna", "boutta", "rn", "bc", "cuz", "cos",
    "tbh", "ngl", "imo", "imho", "afaik", "idk", "idek", "wdym",
}

# Keywords mapped to categories for simple classification
KEYWORD_MAP = {
    "social_greeting": [
        "hello", "hi", "hey", "yo", "morning", "afternoon", "evening", "sup", "howdy", "greetings",
        "oi", "oii", "oiii", "ahoy", "ahoyyy", "welcome", "bonjour", "hola",
        "hiya", "heya", "ello", "helo", "henlo", "hewwo", "hii", "hiii", "hiiii", "heyyy",
        "wassup", "whats up", "what's up", "wazzup", "waddup", "good morning", "good afternoon",
        "good evening", "gm", "gn", "good day", "salutations", "ayo", "ayoo", "yoo", "yooo",
        "anyone here", "dead chat", "chat dead", "wake up",
    ],
    "social_thanks": [
        "thank", "thx", "ty", "thanks", "appreciate", "grateful", "thankyou", "thank you",
        "tysm", "tyvm", "thnx", "thnks", "cheers", "ta", "much appreciated", "danke",
        "thanks a lot", "thanks so much", "many thanks", "big thanks", "bless", "blessed",
        "lifesaver", "you rock", "u rock", "mvp", "legend", "hero",
    ],
    "social_goodbye": [
        "bye", "goodbye", "later", "cya", "see you", "good night", "gn", "farewell", "peace out",
        "byebye", "bye bye", "bai", "buh bye", "ttyl", "gtg", "gotta go", "leaving", "im out",
        "i'm out", "peace", "take care", "see ya", "seeya", "laterz", "laters", "night",
        "nighty night", "goodnight", "gnite", "adios", "ciao", "sayonara", "brb", "afk",
        "heading out", "signing off", "logging off",
    ],
    "question_general": [
        "anyone know", "does anyone", "wondering", "curious", "idea", "thoughts", "suppose",
    ],
    "user_confusion": [
        "confused", "huh", "wat", "wut", "????", "?????", "idk", "don't understand", "makes no sense",
        "what do you mean", "wdym", "i dont get it", "i don't get it", "dont get it",
        "lost", "im lost", "i'm lost", "unclear", "no idea", "clueless", "bewildered",
        "puzzled", "perplexed", "baffled", "wtf", "huhh", "huhhh", "ehh", "uhh", "umm",
        "wait what", "wait wat", "context", "context?", "elaborate",
    ],
    "user_insult": [
        "stupid", "idiot", "dumb", "trash", "fuck", "shit", "ass", "bitch", "suck", "worst", "hate",
        "moron", "retard", "loser", "pathetic", "useless", "garbage", "worthless", "terrible",
        "awful", "disgusting", "ugly", "dumbass", "asshole", "bastard", "dick", "piss off",
        "screw you", "go away", "shut up", "stfu", "kys", "die", "kill yourself", "brain dead",
        "braindead", "moronic", "idiotic", "brainless", "crap", "crappy", "sucks", "sucked",
        "mid", "cringe", "npc", "clown", "boomer", "zoomer", "kid", "child", "bot", "dogwater",
        "smooth brain", "room temp iq", "fatherless", "maidenless", "ratio", "flop", "L",
        "skill issue", "get good", "git gud", "noob", "newb", "tryhard", "sweat",
    ],
    "user_affection": [
        "love", "like you", "miss", "adore", "crush", "heart", "fond",
        "care about", "love you", "luv", "ily", "ilysm", "i love", "loving", "beloved",
        "darling", "sweetheart", "dear", "precious", "cherish", "devotion", "soulmate",
        "goat", "goated", "based", "W", "common W", "king", "queen", "bestie", "besty",
        "marry me", "simping", "simping", "stan", "fan", "idol",
    ],
    "user_venting": [
        "sad", "depressed", "cry", "upset", "unhappy", "miserable", "heartbroken",
        "crying", "tears", "sob", "sobbing", "weep", "weeping", "devastated", "broken",
        "hurt", "hurting", "pain", "painful", "suffering", "grief", "grieving", "mourning",
        "hopeless", "despair", "lonely", "alone", "isolated", "down", "feeling down",
        "bummed", "gutted", "shattered", "melancholy", "gloomy", "blue", "venting",
        "fml", "fuck my life", "i hate my life", "doom", "doomed", "its over", "it's over",
        "crying rn", "im done", "i'm done", "kms", "tilt", "tilted",
    ],
    "user_excitement": [
        "happy", "yay", "excited", "great", "awesome", "amazing", "wonderful",
        "joy", "joyful", "cheerful", "glad", "pleased", "delighted", "thrilled", "ecstatic",
        "overjoyed", "elated", "euphoric", "fantastic", "excellent", "brilliant", "superb",
        "terrific", "marvelous", "splendid", "glorious", "blessed", "fortunate", "lucky",
        "yayyy", "yayy", "woohoo", "woo hoo", "hooray", "hurray", "yippee", "wooo", "hype",
        "pog", "poggers", "pogchamp", "lets go", "let's go", "less go", "fire", "lit",
        "insane", "cracked", "wild", "crazy", "slay", "ate", "bussin", "sheesh",
    ],
    "user_complaint": [
        "angry", "mad", "furious", "pissed", "annoyed", "irritated",
        "rage", "raging", "enraged", "livid", "fuming", "seething", "outraged", "infuriated",
        "frustrated", "aggravated", "agitated", "heated", "triggered", "tilted",
        "pissed off", "ticked off", "fed up", "had enough", "losing it", "complain", "ugh",
        "salty", "malding", "seething", "coping", "cope", "toxic", "toxicity",
        "broken", "buggy", "lag", "lagging", "glitch", "glitched", "nerf", "unplayable",
    ],
    "humor_laughing": [
        "lol", "haha", "lmao", "rofl", "hilarious", "funny", "lmfao",
        "hahaha", "hahahaha", "lolol", "lololol", "xd", "xdd", "xddd",
        "kek", "kekw", "lul", "lulw", "omegalul", "laughing", "laugh",
        "dying", "im dead", "i'm dead", "deceased", "comedy", "joke",
        "hehe", "hehehe", "teehee", "giggle", "chuckle", "wheeze", "snort",
        "skull", "💀", "😭", "😂", "rolling", "wheezing",
    ],
    "user_agreement": [
        "yes", "right", "correct", "yep", "yeah", "ok", "okay", "true", "agreed", "exactly",
        "yea", "ya", "yah", "yup", "yupp", "yuppp", "yessir", "yes sir", "affirmative",
        "absolutely", "definitely", "certainly", "indeed", "precisely", "totally",
        "for sure", "of course", "sure", "surely", "ofc", "facts", "fax", "fr", "for real",
        "same", "ikr", "i know right", "this", "based", "w", "dub", "big w",
        "vouch", "+1", "no cap", "nocap", "bet", "say less", "valid", "real", "tru",
    ],
    "user_disagreement": [
        "no", "wrong", "nope", "nah", "false", "disagree", "incorrect",
        "naw", "nay", "negative", "not really", "dont think so", "don't think so",
        "i disagree", "hard no", "hell no", "absolutely not", "no way", "noway",
        "cap", "thats cap", "that's cap", "bs", "bullshit", "lies", "lying",
        "doubt", "doubtful", "skeptical", "sus", "suspicious", "l", "ratio",
        "stop capping", "fake", "staged", "never", "ain't no way", "aint no way",
    ],
    "user_flirting": [
        "flirt", "handsome", "pretty", "cute", "beautiful", "hot", "sexy", "gorgeous",
        "attractive", "good looking", "stunning", "lovely", "charming", "fine",
        "marry me", "date me", "be mine", "kiss", "hug", "cuddle", "snuggle",
        "wink", "smooch", "babe", "baby", "honey", "sweetie", "cutie", "hottie",
        "rizz", "rizzing", "rizzler", "smooth", "charmer", "daddy", "mommy",
        "princess", "prince", "angel", "mine",
    ],
    "user_bragging": [
        "pro", "best", "amazing at", "skilled", "expert", "legend",
        "goat", "greatest", "legendary", "insane", "cracked", "goated", "built different",
        "im the best", "i'm the best", "too good", "ez", "easy", "ezpz", "gg ez",
        "destroyed", "dominated", "owned", "rekt", "wrecked", "smashed",
        "money", "dollar", "$", "cash", "rich", "pay", "coin", "balance",
        "wealth", "wealthy", "fortune", "millionaire", "billionaire", "bank", "wallet",
        "earn", "earnings", "income", "salary", "wage", "profit",
        "expensive", "bought", "new car", "new phone", "flex", "drip", "drippy",
        "gap", "gapped", "diff", "diffed", "carry", "carrying", "clutch", "clutched",
    ],
    "user_begging": [
        "poor", "broke", "give me", "can i have", "please give", "need money",
        "spare", "donate", "donation", "plsss", "pretty please",
        "can you give", "lend me", "borrow", "short on cash", "down bad",
        "venmo me", "cashapp me", "paypal me", "scammed",
    ],
    "social_chitchat": [
        "eat", "hungry", "food", "yummy", "delicious", "dinner", "lunch", "breakfast", "snack",
        "eating", "meal", "starving", "famished", "appetite", "tasty", "yum",
        "pizza", "burger", "fries", "chicken", "rice", "noodles", "pasta", "steak",
        "cook", "cooking", "chef", "recipe", "restaurant", "takeout", "delivery",
        "weather", "today", "weekend", "plans", "doing",
        "bored", "boring", "dull", "nothing to do", "meh",
        "im bored", "i'm bored", "so bored", "boredom", "monotonous", "tedious",
        "entertain me", "amuse me", "game", "gaming", "watch", "watching",
        "movie", "show", "series", "music", "vibing", "chilling",
    ],
    "request_advice": [
        "sleep", "sleepy", "tired", "exhausted", "rest", "nap", "zzz", "drowsy",
        "sleeping", "bedtime", "bed", "insomnia", "cant sleep", "can't sleep",
        "fatigue", "fatigued", "worn out", "drained", "burned out", "burnout",
        "yawn", "yawning", "passing out", "knocked out", "dead tired",
        "should i", "what should", "advice", "recommend", "suggestion", "tips",
        "help me decide", "opinion on", "thoughts on",
    ],
    "user_compliment": [
        "good bot", "nice bot", "best bot", "smart", "clever", "impressive",
        "well done", "good job", "nice job", "great job", "awesome bot",
        "you're cool", "youre cool", "ur cool", "love this bot", "like you",
        "good", "nice", "perfect", "excellent", "great work",
        "amazing work", "proud", "respect", "props", "kudos",
        "valid bot", "w bot", "based bot", "goated bot",
    ],
    "request_general": [
        "can you", "could you", "would you", "will you", "please", "pls", "plz",
        "do this", "do that", "make me", "get me", "show me", "tell me",
        "i want", "i need", "give me", "send me", "gimme", "search for",
        "find me", "play", "play me", "put on", "mute", "ban", "kick",
        "help", "how to", "how do", "assist", "support", "guide", "tutorial",
        "need help", "help me", "help pls", "help please", "assistance",
        "stuck", "issue", "problem", "trouble", "struggling", "fix", "repair",
    ],
    "social_apology": [
        "sorry", "my bad", "apologize", "apologies", "forgive", "forgive me",
        "i apologize", "im sorry", "i'm sorry", "didnt mean", "didn't mean",
        "my fault", "i was wrong", "mb", "sry", "srry", "my mistake",
        "didn't mean to", "won't happen again", "regret", "pardon", "excuse me",
    ],
    "neutral_statement": [
        "i think", "i believe", "in my opinion", "imo", "personally",
        "just saying", "fyi", "btw", "by the way", "fun fact",
        "did you know", "apparently", "actually", "honestly",
        "hot take", "unpopular opinion", "real talk", "lowkey", "highkey",
    ],
    "user_sarcasm": [
        "wow", "oh wow", "amazing", "incredible", "unbelievable", "shocking",
        "no way", "really", "oh really", "you dont say", "you don't say",
        "totally", "sure jan", "right", "uh huh", "mhm", "suuure",
        "groundbreaking", "fascinating", "riveting", "cool story", "crazy",
    ],
    "user_threat": [
        "fight me", "1v1", "square up", "catch these hands", "pull up",
        "ill beat", "i'll beat", "gonna hurt", "watch out", "be careful",
        "or else", "youll regret", "you'll regret", "dont make me", "don't make me",
        "im coming for you", "track you down", "find you", "hunt you", "destroy you",
        "end you", "game over", "punch", "kick", "slap",
    ],
    "user_commanding": [
        "do it", "just do", "now", "right now", "immediately", "hurry",
        "faster", "quickly", "go", "come", "stop", "start", "obey",
        "shut up", "silence", "quiet", "speak", "listen",
    ],
    "disruptive_spam": [
        "aaa", "aaaa", "aaaaa", "asdf", "qwerty", "spam", "test",
        "123", "1234", "12345", "abcd", "lskdjf", "dkfjsl",
        "zxcv", "poiuy", "mnbvc", "keyboard smash",
    ],
    # Specific wrong name categories for targeted responses
    "bot_wrong_name_siri": [
        "siri", "hey siri", "apple",
    ],
    "bot_wrong_name_alexa": [
        "alexa", "hey alexa", "echo", "amazon",
    ],
    "bot_wrong_name_google": [
        "hey google", "ok google", "okay google", "assistant",
    ],
    "bot_wrong_name_chatgpt": [
        "chatgpt", "chat gpt", "gpt", "openai", "gpt3", "gpt4",
    ],
    # Generic wrong name fallback
    "bot_wrong_name": [
        "cortana", "bard", "claude", "copilot", "bing", "gemini", "llama", "meta ai",
        "jarvis", "friday", "hal", "glados",
    ],
    # Request subcategories
    "request_music": [
        "play music", "play song", "play me", "put on music", "some music",
        "dj", "track", "spotify", "youtube", "soundcloud", "queue", "skip",
    ],
    "request_moderation": [
        "mute", "ban", "kick", "timeout", "warn", "purge", "clear", "nuke",
        "mod", "admin", "owner", "staff",
    ],
    "request_search": [
        "search for", "look up", "find me", "google", "wiki", "define",
        "meaning of", "what is", "who is",
    ],

    # Knowledge about a person / relationship check
    "know_person": [
        "do you know this person", "do you know this guy", "do you know this girl",
        "do you know them", "do you know him", "do you know her",
        "do you know", "what do you know about", "tell me about",
        "have you met", "who is this person", "who is this",
        "do we know", "you know this person",
    ],

    "topic_math": [
        "solve", "equation", "integral", "derivative", "matrix", "calculus", "algebra",
        "geometry", "trigonometry", "probability", "statistics", "formula",
    ],

    "topic_science": [
        "physics", "chemistry", "biology", "astronomy", "geology", "science",
        "quantum", "atom", "molecule", "dna", "neuron", "cell", "gravity",
        "relativity", "evolution", "thermodynamics", "photosynthesis", "electricity",
    ],

    "topic_art": [
        "art", "drawing", "draw", "sketch", "painting", "paint", "illustration",
        "design", "graphic design", "typography", "color theory", "photography",
        "sculpture", "museum", "aesthetic",
    ],

    "topic_music": [
        "music", "song", "track", "album", "artist", "band", "genre",
        "playlist", "lyrics", "instrumental", "beat", "melody",
        "spotify", "soundcloud", "youtube music",
    ],

    "roast_someone": [
        "blame", "flame", "roast", "drag", "cook", "call out", "expose",
        "go after", "clown", "pack", "smoke", "violate",
    ],
    "user_oversharing": [
        "tmi", "too much info", "personal", "private", "secret",
        "dont tell", "don't tell", "between us", "confession", "confess",
        "trauma", "fetish", "kink", "naked", "nude",
    ],
    "user_challenge": [
        "bet", "dare", "challenge", "prove it", "i bet", "wanna bet",
        "try me", "test me", "lets see", "let's see", "show me",
        "you scared", "chicken", "do it then",
    ],
    "neutral_opinion": [
        "think", "opinion", "feel like", "seems like", "looks like",
        "i reckon", "i guess", "suppose", "probably", "maybe",
        "rate", "ranking", "tier",
    ],
    "user_lying": [
        "lying", "liar", "fake", "cap", "thats cap", "that's cap",
        "not true", "false", "bs", "bullshit", "you lie",
        "its not my fault", "it's not my fault", "wasnt me", "wasn't me",
        "i didnt", "i didn't", "not my fault", "blame",
        "didn't do it", "someone else", "hacked",
    ],
    "user_guilt": [
        "feel bad", "guilty", "regret", "wish i", "shouldnt have", "shouldn't have",
        "my fault", "blame myself", "i ruined", "messed up", "fucked up",
        "mistake", "error", "sorry for",
    ],
    "disruptive_random": [
        "hmm", "meh", "whatever", "anyway", "so", "well",
        "dunno", "i guess", "perhaps", "possibly", "idc", "dont care",
        "don't care", "doesnt matter", "doesn't matter", "who cares", "bruh", "bruhhh",
        "lmk", "let me know", "ngl", "random", "stuff", "things",
        "ok and", "did i ask", "who asked",
    ],

    # ================================================================
    # EXPANDED KEYWORD MAP — Categories that previously had no keywords
    # ================================================================

    # ----- Emotional States -----
    "user_angry": [
        "angry", "furious", "enraged", "livid", "fuming", "rage mode",
        "seeing red", "so mad", "im mad", "i'm mad", "pissed off",
    ],
    "user_happy": [
        "happy", "glad", "cheerful", "joyful", "feeling good", "in a good mood",
        "so happy", "im happy", "i'm happy", "content", "satisfied",
    ],
    "user_love": [
        "love", "in love", "i love", "love you", "loving", "adore",
        "heart", "soulmate", "affectionate", "sweetheart",
    ],
    "user_confused": [
        "confused", "confusing", "dont understand", "don't understand",
        "make no sense", "lost", "wait what", "i dont get it",
    ],
    "user_bored": [
        "bored", "boring", "nothing to do", "so bored", "boredom",
        "entertain me", "amuse me", "im bored", "i'm bored",
    ],
    "user_panicking": [
        "panic", "panicking", "freaking out", "oh no", "oh god",
        "help me", "emergency", "sos", "im panicking", "im freaking out",
    ],
    "user_overthinking": [
        "overthinking", "cant stop thinking", "spiral", "spiraling",
        "what if", "anxious", "anxiety", "overanalyzing", "too deep",
    ],
    "user_drama": [
        "drama", "dramatic", "not over it", "petty", "messy",
        "the drama", "so dramatic", "dramaaa", "spicy", "tea",
    ],
    "user_jealousy": [
        "jealous", "jealousy", "envious", "envy", "not fair",
        "why not me", "they got", "unfair", "favoritism",
    ],
    "user_relief": [
        "relief", "relieved", "thank god", "phew", "dodged a bullet",
        "close call", "that was close", "finally", "what a relief",
    ],
    "user_trauma_dump": [
        "trauma", "traumatic", "trigger warning", "tw", "dark place",
        "heavy stuff", "deep stuff", "unpack", "childhood", "baggage",
    ],
    "user_validation_seeking": [
        "am i right", "right?", "tell me im right", "validate me",
        "back me up", "agree with me", "dont you think", "isn't that true",
    ],

    # ----- Disruptive Behavior -----
    "disruptive_cringe": [
        "cringe", "cringy", "cringey", "yikes", "secondhand embarrassment",
        "thats cringe", "so cringe", "cringe af",
    ],
    "disruptive_delulu": [
        "delulu", "delusional", "delusion", "in denial", "copium",
        "coping hard", "reality check", "wake up", "touch grass",
    ],
    "disruptive_npc": [
        "npc", "npc behavior", "scripted", "basic", "generic",
        "predictable", "robotic", "dialogue options", "side character",
    ],
    "disruptive_main_character": [
        "main character", "protagonist", "center of attention", "its about me",
        "main character energy", "mc energy", "plot armor", "chosen one",
    ],
    "disruptive_stalker": [
        "stalker", "stalking", "creepy", "watching you", "following",
        "obsessed", "creep", "where do you live", "personal info",
    ],
    "disruptive_roleplay": [
        "roleplay", "rp", "pretend", "imagine", "lets pretend",
        "in character", "act as", "you are now", "play as",
    ],
    "disruptive_toxic_positivity": [
        "good vibes only", "positive vibes", "just be happy", "look on the bright side",
        "everything happens for a reason", "stay positive", "no negativity",
        "just smile", "cheer up", "it could be worse",
    ],
    "disruptive_repetition": [
        "said that already", "you already said", "repeating", "broken record",
        "you just said that", "we heard you", "stop repeating",
    ],
    "disruptive_nonsense": [
        "nonsense", "gibberish", "random words", "word salad", "makes no sense",
        "what are you saying", "incoherent", "rambling",
    ],
    "user_simping": [
        "simp", "simping", "simp for", "down bad", "whipped",
        "do anything for", "ill do anything", "at your service", "your servant",
    ],
    "user_receipts": [
        "receipts", "screenshot", "proof", "evidence", "caught in 4k",
        "recorded", "saved", "dont delete", "i have proof",
    ],

    # ----- Questions / Inquiry -----
    "question_about_bot": [
        "are you a bot", "what are you", "who are you", "who made you",
        "your creator", "about you", "tell me about yourself",
        "are you real", "are you ai", "are you human",
    ],
    "question_capabilities": [
        "what can you do", "your features", "your abilities", "commands",
        "what do you do", "your powers", "can you help", "your purpose",
    ],
    "question_age": [
        "how old are you", "your age", "when were you made", "your birthday",
        "when were you born", "how long have you been", "age",
    ],
    "question_philosophy": [
        "meaning of life", "existence", "purpose", "why are we here",
        "consciousness", "free will", "what is real", "simulation",
        "philosophical", "deep question", "existential",
    ],
    "question_hypothetical": [
        "what if", "hypothetically", "imagine if", "would you rather",
        "in theory", "theoretically", "suppose that", "lets say",
    ],
    "question_comparison": [
        "better than", "worse than", "compared to", "vs", "versus",
        "which is better", "who would win", "or", "difference between",
    ],
    "question_memory": [
        "do you remember", "remember when", "recall", "you forgot",
        "did you forget", "you said before", "last time",
    ],
    "question_relationship": [
        "are we friends", "do you like me", "what am i to you",
        "our relationship", "how do you feel about me", "are we close",
    ],
    "question_opinion": [
        "what do you think", "your opinion", "your take", "your thoughts",
        "how do you feel about", "whats your stance", "your view",
    ],
    "neutral_preference": [
        "favorite", "prefer", "rather", "which do you like",
        "do you like", "best", "top pick", "go to",
    ],

    # ----- Behavior Patterns -----
    "user_suspicious": [
        "sus", "suspicious", "sussy", "sussy baka", "acting sus",
        "shady", "sketchy", "fishy", "something off", "weird vibe",
    ],
    "user_unclear": [
        "unclear", "what do you mean", "be more specific",
        "elaborate", "explain", "clarify", "huh",
    ],
    "user_demanding": [
        "do it now", "right now", "immediately", "hurry up", "faster",
        "i said now", "this instant", "chop chop", "asap",
    ],
    "user_passive_aggressive": [
        "fine", "whatever you say", "sure thing", "if you say so", "noted",
        "cool cool cool", "interesting choice", "good for you", "ok then",
        "i mean its fine", "not like i care",
    ],
    "user_fishing": [
        "am i pretty", "am i smart", "do you think im", "compliment me",
        "say something nice", "tell me im", "rate me", "how do i look",
    ],

    # ----- Gossip -----
    "gossip_sharing": [
        "did you hear", "guess what", "apparently", "rumor", "tea",
        "spill the tea", "you wont believe", "i heard that", "word is",
    ],
    "gossip_asking": [
        "whats the tea", "any gossip", "tell me the drama", "whats happening",
        "any news", "fill me in", "what did i miss", "catch me up",
    ],

    # ----- Humor -----
    "humor_bad_joke": [
        "knock knock", "why did the", "get it", "bad joke", "pun",
        "dad joke", "heard this one", "a man walks into", "guess what",
    ],

    # ----- Bot Meta -----
    "bot_test": [
        "test", "testing", "are you alive", "are you there", "you there",
        "hello?", "anyone there", "ping", "are you on", "you awake",
    ],
    "social_weather": [
        "weather", "rain", "raining", "sunny", "cold outside",
        "hot today", "forecast", "temperature", "snowing", "windy",
    ],

    # ----- Requests -----
    "request_for_others": [
        "can you help them", "for my friend", "for someone", "tell them",
        "help them", "on behalf of", "asking for a friend", "for them",
    ],

    # ----- Memory Commands -----
    "memory_save": [
        "remember this", "save this", "dont forget", "don't forget",
        "keep this in mind", "note this", "store this", "memorize",
    ],
    "memory_retrieve": [
        "what did i say", "what do you remember", "any memories",
        "bring up", "what was it", "you remember", "recall",
    ],
}

# Keywords that indicate negative sentiment (for rudeness detection)
# Expanded to catch softer aggression and modern insults
NEGATIVE_KEYWORDS = [
    "fuck", "shit", "asshole", "bitch", "stupid", "dumb", 
    "idiot", "trash", "worst", "hate", "die", "kill",
    "useless", "shut up", "stfu", "kys", "dick", "cock",
    "cunt", "pussy", "bastard", "moron", "retard", "clown",
    "loser", "garbage", "awful", "terrible", "disgusting",
    "ugly", "dumbass", "brainless", "braindead", "cringe",
    "L", "ratio", "mid", "npc", "bot", "dogwater",
    "fatherless", "maidenless", "skill issue", "get good",
    "noob", "tryhard", "sweat", "toxic", "cancer",
]