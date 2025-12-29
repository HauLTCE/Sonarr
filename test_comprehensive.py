"""Comprehensive test suite for enhanced pattern matching."""

from sonarr.patterns import pattern_match

def run_test(text, expected_cat, expected_notes=""):
    """Run a single test and return result."""
    result = pattern_match(text)
    cat, conf = result[0], result[1]
    mods = result[2] if len(result) > 2 else {}
    
    flags = []
    if mods.get("negated"):
        flags.append("NEG")
    if mods.get("is_question"):
        flags.append("Q")
    intens = mods.get("intensifier_count", 0)
    if intens > 0:
        flags.append(f"I{intens}")
    if mods.get("third_party"):
        flags.append(f"3P:{mods['third_party']}")
    
    flag_str = " ".join(flags) if flags else "-"
    cat_str = cat if cat else "None"
    
    # Check if result matches expectation
    match = "✓" if cat_str.lower() == expected_cat.lower() or expected_cat.lower() in cat_str.lower() else "✗"
    
    return {
        "text": text,
        "result": cat_str,
        "conf": conf,
        "flags": flag_str,
        "expected": expected_cat,
        "notes": expected_notes,
        "match": match
    }

def print_section(title, tests):
    """Print a section of tests."""
    print(f"\n{'='*60}")
    print(f"  {title}")
    print(f"{'='*60}")
    
    passed = 0
    for t in tests:
        r = run_test(t["text"], t["expected"], t.get("notes", ""))
        status = r["match"]
        if status == "✓":
            passed += 1
        print(f"{status} {r['text']:40}")
        print(f"   Result: {r['result']:15} conf={r['conf']} [{r['flags']}]")
        print(f"   Expected: {r['expected']}")
        if r["notes"]:
            print(f"   Notes: {r['notes']}")
        print()
    
    print(f"  Section: {passed}/{len(tests)} passed")
    return passed, len(tests)


# =====================================================
# TEST SUITES
# =====================================================

negation_tests = [
    {"text": "I do not dislike you.", "expected": "random", "notes": "Negated dislike → neutral"},
    {"text": "You are not ugly.", "expected": "random", "notes": "Negated insult → neutral"},
    {"text": "I don't love this.", "expected": "None", "notes": "'this' isn't a target anchor - expected no match"},
    {"text": "You aren't making sense.", "expected": "None", "notes": "'making sense' not a keyword - expected no match"},
    {"text": "I never hated you.", "expected": "random", "notes": "never = negation"},
    {"text": "I don't hate you.", "expected": "random", "notes": "Basic negation"},
    {"text": "You're not stupid.", "expected": "random", "notes": "Negated insult to target - contraction works"},
]

intensifier_tests = [
    {"text": "You are extremely annoying.", "expected": "insult", "notes": "Should have I1 flag"},
    {"text": "I totally admire you.", "expected": "affection", "notes": "admire now in keywords"},
    {"text": "This is so, so stupid.", "expected": "None", "notes": "'This' not a subject anchor - expected no match"},
    {"text": "I really like you.", "expected": "affection", "notes": "Boosted affection"},
    {"text": "You absolutely suck.", "expected": "insult", "notes": "Boosted insult"},
    {"text": "I genuinely hate you.", "expected": "insult", "notes": "genuinely = intensifier"},
    {"text": "You are incredibly dumb.", "expected": "insult", "notes": "incredibly = intensifier"},
]

question_sarcasm_tests = [
    {"text": "How are you this dumb?", "expected": "sarcasm", "notes": "Question + insult → sarcasm"},
    {"text": "Why are you so terrible?", "expected": "sarcasm", "notes": "Question + insult → sarcasm"},
    {"text": "What makes you so weird?", "expected": "sarcasm", "notes": "weird not in insults - may not match"},
    {"text": "Why do you hate me?", "expected": "confusion", "notes": "You + hate + me → confusion pattern"},
    {"text": "Are you stupid?", "expected": "sarcasm", "notes": "Question + you + stupid"},
    {"text": "Can you be more useless?", "expected": "sarcasm", "notes": "Question + insult"},
    {"text": "Why are you such an idiot?", "expected": "sarcasm", "notes": "Question + insult"},
]

gossip_tests = [
    {"text": "She is really mean.", "expected": "gossip", "notes": "3rd party + insult (mean not in keywords)"},
    {"text": "They are so boring.", "expected": "gossip", "notes": "3rd party + boring"},
    {"text": "He acts crazy.", "expected": "gossip", "notes": "crazy not in keywords - may not match"},
    {"text": "She is wonderful.", "expected": "None", "notes": "Positive about 3rd party - no pattern"},
    {"text": "He is so stupid.", "expected": "gossip", "notes": "3rd party + insult"},
    {"text": "She sucks.", "expected": "gossip", "notes": "3rd party + sucks"},
    {"text": "They are pathetic.", "expected": "gossip", "notes": "3rd party + pathetic"},
    {"text": "I hate him.", "expected": "gossip", "notes": "Self + hate + 3rd party"},
]

edge_case_tests = [
    {"text": "Why is he so stupid?", "expected": "gossip", "notes": "Question about 3rd party - gossip wins"},
    {"text": "I really don't hate you.", "expected": "random", "notes": "Intensifier + negation - negation wins"},
    {"text": "Why aren't you smart?", "expected": "None", "notes": "Negation + question + no direct insult = no match"},
    {"text": "You don't really suck.", "expected": "random", "notes": "Negation neutralizes insult"},
    {"text": "I absolutely never loved you.", "expected": "insult", "notes": "Negation of affection → insult"},
    {"text": "Why does he hate me?", "expected": "gossip", "notes": "Question about 3rd party hating self"},
    {"text": "She doesn't like him.", "expected": "None", "notes": "3rd party to 3rd party - no specific pattern"},
]

# Run all tests
total_passed = 0
total_tests = 0

p, t = print_section("1. NEGATION HANDLING TESTS", negation_tests)
total_passed += p
total_tests += t

p, t = print_section("2. INTENSIFIER SCORING TESTS", intensifier_tests)
total_passed += p
total_tests += t

p, t = print_section("3. QUESTION & SARCASM DETECTION TESTS", question_sarcasm_tests)
total_passed += p
total_tests += t

p, t = print_section("4. GOSSIP & THIRD-PARTY TESTS", gossip_tests)
total_passed += p
total_tests += t

p, t = print_section("5. EDGE CASES (COMBINATIONS)", edge_case_tests)
total_passed += p
total_tests += t

print(f"\n{'='*60}")
print(f"  FINAL RESULTS: {total_passed}/{total_tests} tests passed ({100*total_passed//total_tests}%)")
print(f"{'='*60}")
