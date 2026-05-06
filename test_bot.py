"""
Interactive terminal testing tool for Sonarr's intent classification.
Allows you to type messages and see how the HuggingFace Zero-Shot 
model, keyword matching, and fuzzy fallback map to Sonarr's intents.
"""

import sys
import logging

# Disable debug spam for cleaner output
logging.getLogger("bot").setLevel(logging.ERROR)

from sonarr.input_check.classifier import get_classifier
from sonarr.input_check.keywords import KEYWORD_MAP

def main():
    print("=" * 60)
    print("SONARR INTENT CLASSIFIER - TERMINAL TESTER")
    print("Loading NLP models in background... (might take a few seconds)")
    print("=" * 60)
    
    classifier = get_classifier()
    
    print("\nType a message to see how Sonarr interprets it.")
    print("Type 'exit' or 'quit' to stop.\n")
    
    while True:
        try:
            text = input("You: ")
            if text.lower().strip() in ['exit', 'quit']:
                break
            if not text.strip():
                continue
                
            print("\n  [Thinking...]")
            category, confidence, mods = classifier.smart_classify_full(text, is_reply_to_bot=False)
            
            print(f"\n  [Result]")
            print(f"  Category:   {category}")
            print(f"  Confidence: {confidence}/4")
            print(f"  Sentiment:  {mods.get('sentiment_label', 'neutral')} (VADER)")
            print("-" * 60)
            
        except KeyboardInterrupt:
            break
        except Exception as e:
            print(f"Error: {e}")

if __name__ == "__main__":
    main()
