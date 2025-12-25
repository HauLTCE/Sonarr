import time
import logging

logger = logging.getLogger("bot")

command_counts = {}
user_cooldowns = {}
command_type_spam = {}  # Track spam by command type

COMMAND_THRESHOLD = 10
TIME_WINDOW = 10
COOLDOWN_DURATION = 300

# Per-command spam settings
PER_COMMAND_THRESHOLD = 5  # Max uses of same command in 5 minutes
PER_COMMAND_TIME_WINDOW = 300  # 5 minutes
PER_COMMAND_COOLDOWN = 600  # 10 minutes timeout

# Commands exempt from per-command spam detection (music, utility, etc.)
SPAM_EXEMPT_COMMANDS = {
    'play', 'queue', 'skip', 'stop', 'pause', 'resume', 'join', 'leave', 'disconnect',
    'nowplaying', 'now_playing', 'loop', 'shuffle', 'clear', 'remove',
    'playlist_save', 'playlist_load', 'playlist_list',
    'ping', 'echo', 'remind', 'status', 'help'
}

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

def check_command_type_spam(user_id, command_name):
    """Check if user is spamming a specific command type. Returns (is_spamming, remaining_cooldown)."""
    # Skip spam check for exempt commands
    if command_name in SPAM_EXEMPT_COMMANDS:
        return False, 0
    
    current_time = time.time()
    user_key = f"{user_id}"
    command_key = f"{user_id}:{command_name}"
    
    # Check if user is in command-type timeout
    if command_key in command_type_spam:
        timeout_end = command_type_spam[command_key]
        remaining = timeout_end - current_time
        if remaining > 0:
            logger.debug(f"[Spam] User {user_id} is in timeout for !{command_name} ({remaining:.0f}s remaining)")
            return True, remaining
        else:
            del command_type_spam[command_key]
    
    if user_key not in command_counts:
        command_counts[user_key] = {}
    
    # Initialize command type tracking if needed
    if command_name not in command_counts[user_key]:
        command_counts[user_key][command_name] = []
    
    # Clean up old timestamps (outside 5-minute window)
    command_counts[user_key][command_name] = [
        ts for ts in command_counts[user_key][command_name]
        if current_time - ts < PER_COMMAND_TIME_WINDOW
    ]
    
    # Add current command use
    command_counts[user_key][command_name].append(current_time)
    count = len(command_counts[user_key][command_name])
    
    # Check if threshold exceeded
    if count > PER_COMMAND_THRESHOLD:
        timeout_end = current_time + PER_COMMAND_COOLDOWN
        command_type_spam[command_key] = timeout_end
        logger.warning(f"[Spam] User {user_id} exceeded spam threshold for !{command_name} ({count} uses in {PER_COMMAND_TIME_WINDOW}s). Timeout for 10 minutes.")
        return True, PER_COMMAND_COOLDOWN
    
    logger.debug(f"[Spam] User {user_id} used !{command_name} ({count}/{PER_COMMAND_THRESHOLD})")
    return False, 0

def cleanup_spam_data():
    """Clean up expired spam tracking data."""
    current_time = time.time()
    
    # Clean up user cooldowns
    expired_users = [uid for uid, timeout in user_cooldowns.items() if timeout <= current_time]
    for uid in expired_users:
        del user_cooldowns[uid]
    
    # Clean up command type spam
    expired_commands = [key for key, timeout in command_type_spam.items() if timeout <= current_time]
    for key in expired_commands:
        del command_type_spam[key]
