"""Command resolution helpers for the `!`-prefix router in main.py.

Extracted from main.py so the on_message chain handler stays readable and these
pieces become unit-testable. Both take the bot explicitly instead of closing
over a module global.
"""
import difflib
from discord.ext import commands

# Help-related pseudo-commands that aren't real Command objects but should still
# be offered as "did you mean?" suggestions.
_HELP_KEYWORDS = [
    "help", "help games", "help dungeon", "help economy", "help music",
    "help adventure", "dungeon_guide", "dguide", "advguide",
]


def _all_command_names(bot) -> list:
    """Every command name + alias (incl. group subcommands) the bot knows."""
    names = []
    for cmd in bot.commands:
        names.append(cmd.name)
        names.extend(cmd.aliases)
        if isinstance(cmd, commands.Group):
            for sub in cmd.commands:
                names.append(f"{cmd.name} {sub.name}")
                names.extend(f"{cmd.name} {a}" for a in sub.aliases)
    return names


def fuzzy_suggest(bot, invoked: str) -> str | None:
    """Closest matching command name for a typo, or None."""
    all_names = _all_command_names(bot) + _HELP_KEYWORDS

    matches = difflib.get_close_matches(invoked, all_names, n=1, cutoff=0.6)
    if matches:
        return matches[0]

    # Fallback: prefix match either direction.
    for name in all_names:
        if name.startswith(invoked) or invoked.startswith(name):
            return name
    return None


def find_command_by_prefix(bot, cmd_name: str) -> list:
    """Commands whose name/alias starts with cmd_name (for `&&` chain resolution).

    An exact name/alias match short-circuits to a single result so a full name
    never reads as ambiguous against longer commands sharing its prefix.
    """
    cmd_name = cmd_name.lower()
    if not cmd_name:
        return []

    exact = bot.get_command(cmd_name)
    if exact:
        return [exact]

    matches = []
    for cmd in bot.commands:
        names = [cmd.name] + list(cmd.aliases)
        if any(n.lower().startswith(cmd_name) for n in names):
            matches.append(cmd)
    return matches
