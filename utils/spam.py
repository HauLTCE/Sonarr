import time
import logging

logger = logging.getLogger("bot")

command_counts = {}
user_cooldowns = {}

COMMAND_THRESHOLD = 10
TIME_WINDOW = 10
COOLDOWN_DURATION = 300

def check_spam(user_id, chain_count=1):
    """Check if user is spamming. Returns (is_spamming, remaining_cooldown)."""
    current_time = time.time()
    
    if user_id in user_cooldowns:
        remaining = user_cooldowns[user_id] - current_time
        if remaining > 0:
            return True, remaining
        else:
            del user_cooldowns[user_id]
    
    if user_id not in command_counts:
        command_counts[user_id] = []
    
    command_counts[user_id] = [
        (ts, count) for ts, count in command_counts[user_id]
        if current_time - ts < TIME_WINDOW
    ]
    
    command_counts[user_id].append((current_time, chain_count))
    
    total_commands = sum(count for _, count in command_counts[user_id])
    
    if total_commands > COMMAND_THRESHOLD:
        user_cooldowns[user_id] = current_time + COOLDOWN_DURATION
        command_counts[user_id] = []
        return True, COOLDOWN_DURATION
    
    return False, 0
