import os
import ast

MAPPING = {
    "social_greeting": ["social_greeting"],
    "social_goodbye": ["social_goodbye"],
    "social_thanks": ["social_thanks"],
    "social_chitchat": ["social_chitchat", "social_weather", "user_bored"],
    "user_compliment": ["user_compliment", "user_affection", "user_flirting", "user_love", "user_simping", "user_fishing"],
    "user_insult": ["user_insult", "mixed_affection_insult", "mixed_insult_affection", "user_sarcasm"],
    "user_threat": ["user_threat", "user_challenge", "mixed_question_threat"],
    "user_commanding": ["user_commanding", "user_demanding"],
    "user_complaint": ["user_complaint", "user_venting", "user_jealousy", "user_passive_aggressive", "user_angry"],
    "user_apology": ["social_apology", "user_guilt"],
    "user_emotional": ["user_excitement", "user_panicking", "user_relief", "user_drama", "user_trauma_dump", "user_happy", "user_overthinking"],
    "question_general": ["question_general", "question_philosophy", "question_capabilities", "question_hypothetical", "question_comparison", "neutral_preference", "question_age", "question_about_bot", "question_opinion", "question_memory", "question_relationship", "bot_test"],
    "request_general": ["request_general", "request_advice", "request_for_others", "user_begging"],
    "request_action": ["request_music", "request_search", "request_moderation"],
    "bot_wrong_name": ["bot_wrong_name", "bot_wrong_name_alexa", "bot_wrong_name_chatgpt", "bot_wrong_name_google", "bot_wrong_name_siri"],
    "disruptive_behavior": ["disruptive_spam", "disruptive_random", "disruptive_cringe", "disruptive_repetition", "disruptive_stalker", "disruptive_toxic_positivity", "disruptive_npc", "disruptive_main_character", "disruptive_delulu", "disruptive_roleplay", "disruptive_nonsense", "user_bragging", "user_oversharing", "user_lying", "user_receipts", "user_suspicious"],
    "user_confusion": ["user_confusion", "user_unclear", "user_confused"],
    "user_agreement": ["user_agreement"],
    "user_disagreement": ["user_disagreement"],
    "humor_laughing": ["humor_laughing", "humor_bad_joke"],
    "bot_injection": ["bot_injection"]
}

MAIN_DIR = "sonarr/responses/main"

def extract_responses(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Parse the ast to find the RESPONSES list
        tree = ast.parse(content)
        for node in tree.body:
            if isinstance(node, ast.Assign):
                for target in node.targets:
                    if getattr(target, 'id', None) == 'RESPONSES':
                        return ast.literal_eval(node.value)
    except Exception as e:
        print(f"Failed to read {filepath}: {e}")
    return []

def run():
    all_files = os.listdir(MAIN_DIR)
    
    for new_cat, old_cats in MAPPING.items():
        combined_responses = []
        for old_cat in old_cats:
            old_file = os.path.join(MAIN_DIR, f"{old_cat}.py")
            if os.path.exists(old_file):
                resps = extract_responses(old_file)
                combined_responses.extend(resps)
                
                # Delete the old file if it's not the same as the new one
                if old_cat != new_cat:
                    os.remove(old_file)
        
        # Deduplicate
        combined_responses = list(dict.fromkeys(combined_responses))
        
        # Write the new file
        new_file = os.path.join(MAIN_DIR, f"{new_cat}.py")
        with open(new_file, 'w', encoding='utf-8') as f:
            f.write(f'RESPONSES = [\n')
            for r in combined_responses:
                escaped = r.replace('"', '\\"')
                f.write(f'    "{escaped}",\n')
            f.write(f']\n')
            
if __name__ == "__main__":
    run()
    print("Done consolidating responses!")