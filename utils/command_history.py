import time
import logging
from collections import deque

logger = logging.getLogger("bot")

# Use deque for O(1) append and bounded size
command_history = deque(maxlen=50)

def log_command(command_name, user_display_name, args=""):
    """Log a command execution to history."""
    try:
        cmd_entry = {
            "timestamp": time.time(),
            "user": user_display_name,
            "command": command_name,
            "args": args,
            "status": "pending"
        }
        command_history.append(cmd_entry)
    except Exception as e:
        logger.debug(f"Error logging command to history: {e}")

def update_command_status(status_msg):
    """Update the last command's status."""
    try:
        if command_history:
            command_history[-1]["status"] = status_msg
    except Exception as e:
        logger.debug(f"Error updating command status: {e}")

def get_recent_commands(limit=15):
    """Get recently executed commands formatted for display."""
    try:
        if not command_history:
            return "Recent commands: None"
        
        recent = list(command_history)[-limit:]
        cmd_text = "Recent commands executed:\n"
        
        for cmd in recent:
            timestamp_ago = int(time.time() - cmd.get("timestamp", time.time()))
            if timestamp_ago < 60:
                time_str = f"{timestamp_ago}s ago"
            else:
                time_str = f"{timestamp_ago // 60}m ago"
            
            cmd_name = cmd.get("command", "unknown")
            user = cmd.get("user", "unknown")
            args = cmd.get("args", "")
            status = cmd.get("status", "pending")
            
            arg_text = f" {args}" if args else ""
            cmd_text += f"  • {user}: !{cmd_name}{arg_text} ({status}) [{time_str}]\n"
        
        return cmd_text
    except Exception as e:
        logger.debug(f"Error getting command history: {e}")
        return "Recent commands: [Unavailable]"
