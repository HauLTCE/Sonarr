import re
from sonarr.patterns import TARGET_ANCHORS, INSULT_WORDS, pattern_match

# The filler pattern used in pattern_match
filler = r"(?:\s+\S+){0,10}?\s+"

# Test: you + filler + stupid
text1 = "you are not stupid"
text2 = "you're not stupid"

pattern = r"(you)" + filler + r"(stupid)"
print(f"Pattern: {pattern}")
print(f"'{text1}': {re.search(pattern, text1)}")
print(f"'{text2}': {re.search(pattern, text2)}")

# The issue: "you're" - "you" is followed by "'re" not whitespace
# So the filler expects \s+ but gets 're
print("\n--- Issue Analysis ---")
print("The filler pattern requires \\s+ after the anchor")
print("'you are' has space after 'you' - WORKS")
print("'you're' has apostrophe after 'you' - FAILS")

# Solution: Make filler more flexible
filler2 = r"(?:['`]?\w*\s+\S*){0,10}?\s*"
pattern2 = r"(you)" + filler2 + r"(stupid)"
print(f"\nNew pattern: {pattern2}")
print(f"'{text1}': {re.search(pattern2, text1)}")
print(f"'{text2}': {re.search(pattern2, text2)}")
