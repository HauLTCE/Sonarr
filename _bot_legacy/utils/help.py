import discord
from discord.ext import commands
from utils.config import get_guild_config


# ── Emojis per cog ──
COG_ICONS = {
    "Music":      "🎵",
    "SonarrAI":   "🧠",
    "Admin":      "🔧",
    "Moderation": "🛡️",
    "Levels":     "📊",
    "Welcome":    "👋",
    "Utility":    "🔎",
    "AutoRole":   "🏷️",
    "Games":      "🎮",
    "Economy":    "💰",
    "Shop":       "🏪",
    "Adventure":  "🗡️",
}

# ── Channel-specific help profiles ──
CHANNEL_HELP_PROFILES = {
    "games_channel": {
        "title": "🎮 Casino & Games",
        "color": 0x2ECC71,
        "sections": [
            {
                "name": "🎲 Solo Games",
                "value": (
                    "`!coinflip <bet> <h/t>` — 50/50 coin toss, 1.9x\n"
                    "`!blackjack <bet>` — Classic 21 vs dealer\n"
                    "`!highlow <bet>` — Guess higher or lower\n"
                    "`!roulette <bet> <choice>` — European roulette\n"
                    "`!crash <bet>` — Cash out before crash\n"
                    "`!mines <bet> <mines>` — Minesweeper grid\n"
                    "`!arena <bet>` — PvE combat arena"
                ),
            },
            {
                "name": "⚔️ PvP Games",
                "value": (
                    "`!coinflip <bet> @user` — PvP coin toss\n"
                    "`!duel @user <bet>` — Russian roulette\n"
                    "`!ttt @user [bet]` — Tic-Tac-Toe\n"
                    "`!c4 @user [bet]` — Connect Four"
                ),
            },
            {
                "name": "👥 Group Games",
                "value": (
                    "`!heist <ante>` — Group heist (2+ players)\n"
                    "`!rob @user` — Pickpocket someone\n"
                    "`!wordchain` — Word chain (free)"
                ),
            },
            {
                "name": "💡 Tips",
                "value": "Bet range: **10–5,000 🪙** (coinflip: up to 10,000). All winnings are taxed 5% on PvP.",
            },
        ],
    },
    "dungeon_channel": {
        "title": "🗡️ Dungeon Adventure",
        "color": 0xE74C3C,
        "sections": [
            {
                "name": "⚔️ Core Commands",
                "value": (
                    "`!adventure` — Enter the dungeon\n"
                    "`!inventory` — View your gear\n"
                    "`!equip <item>` — Equip an item\n"
                    "`!dungeon_guide` — Full game guide (4 pages)\n"
                    "`!dungeon top` — Leaderboard"
                ),
            },
            {
                "name": "🎒 Item Tiers",
                "value": (
                    "⬜ Common → 🟩 Uncommon → 🟦 Rare → 🟪 Epic → 🟧 **Legendary**\n"
                    "Legendaries only drop from **bosses (Floor 35+)**"
                ),
            },
            {
                "name": "💡 Quick Tips",
                "value": (
                    "• Boss every **10 floors** — rest before them\n"
                    "• Death costs **2 floors** + **10% wallet tax**\n"
                    "• Items auto-equip to empty slots\n"
                    "• Add `show` to any command to keep it visible"
                ),
            },
        ],
    },
    "economy_channel": {
        "title": "💰 Economy & Shop",
        "color": 0xF1C40F,
        "sections": [
            {
                "name": "💵 Earning",
                "value": (
                    "`!daily` — Daily reward (24h)\n"
                    "`!work` — Work for coins (2h)\n"
                    "`!cashout <levels>` — Sell levels for coins"
                ),
            },
            {
                "name": "🏦 Banking",
                "value": (
                    "`!bal` — Check balance\n"
                    "`!deposit <amt>` — Move to bank\n"
                    "`!withdraw <amt>` — Move from bank\n"
                    "`!pay @user <amt>` — Send coins (5% tax)\n"
                    "`!baltop` — Wealth leaderboard"
                ),
            },
            {
                "name": "🏪 Shop & Profile",
                "value": (
                    "`!shop` — Browse shop\n"
                    "`!buy <item>` — Purchase\n"
                    "`!title [name]` — Switch title\n"
                    "`!profile` — View profile\n"
                    "`!achievements` — View achievements\n"
                    "`!prestige` — Reset for bonuses"
                ),
            },
        ],
    },
}

# ── Category groupings for the main help ──
HELP_CATEGORIES = [
    {
        "emoji": "🎮",
        "name": "Games",
        "desc": "Casino, PvP, and group games",
        "cogs": ["Games"],
        "channel_key": "games_channel",
    },
    {
        "emoji": "🗡️",
        "name": "Dungeon",
        "desc": "RPG adventure with items & bosses",
        "cogs": ["Adventure"],
        "channel_key": "dungeon_channel",
    },
    {
        "emoji": "💰",
        "name": "Economy",
        "desc": "Earning, banking, shop, titles",
        "cogs": ["Economy", "Shop"],
        "channel_key": "economy_channel",
    },
    {
        "emoji": "🎵",
        "name": "Music",
        "desc": "Music playback & playlists",
        "cogs": ["Music"],
        "channel_key": "music_channel",
    },
    {
        "emoji": "🔧",
        "name": "Admin",
        "desc": "Server configuration",
        "cogs": ["Admin"],
    },
    {
        "emoji": "🛡️",
        "name": "Moderation",
        "desc": "Moderation tools",
        "cogs": ["Moderation"],
    },
    {
        "emoji": "📊",
        "name": "Levels",
        "desc": "XP and leveling system",
        "cogs": ["Levels"],
    },
    {
        "emoji": "🔎",
        "name": "Utility",
        "desc": "Info and utility commands",
        "cogs": ["Utility"],
    },
]


def _detect_channel_profile(ctx):
    """Detect which help profile to use based on current channel."""
    if not ctx.guild:
        return None
    guild_id = ctx.guild.id
    channel_id = ctx.channel.id
    for config_key, profile in CHANNEL_HELP_PROFILES.items():
        set_channel_id = get_guild_config(guild_id, config_key)
        if set_channel_id and int(set_channel_id) == channel_id:
            return profile
    return None


def _get_channel_mention(ctx, config_key):
    """Get a channel mention string for a config key, or None."""
    if not ctx.guild:
        return None
    ch_id = get_guild_config(ctx.guild.id, config_key)
    if not ch_id:
        return None
    ch = ctx.guild.get_channel(int(ch_id))
    return ch.mention if ch else None


class PrettyHelp(commands.HelpCommand):
    """A polished, context-aware help command."""

    # ── Overview (!help) ──
    async def send_bot_help(self, mapping):
        ctx = self.context

        # Channel-specific help
        profile = _detect_channel_profile(ctx)
        if profile:
            await self._send_channel_help(profile)
            return

        # Main help page
        embed = discord.Embed(
            title="📖  SONARR — Command Guide",
            color=0x2b2d31,
        )

        # Build category list with channel links
        lines = []
        for cat in HELP_CATEGORIES:
            # Check if any cog exists and has commands
            has_commands = False
            for cog_name in cat["cogs"]:
                cog = ctx.bot.get_cog(cog_name)
                if cog and cog.get_commands():
                    has_commands = True
                    break
            if not has_commands:
                continue

            line = f"{cat['emoji']}  **{cat['name']}** — {cat['desc']}"
            ch_key = cat.get("channel_key")
            if ch_key:
                mention = _get_channel_mention(ctx, ch_key)
                if mention:
                    line += f"  →  {mention}"
            lines.append(line)

        embed.description = "\n".join(lines)

        # Quick commands section
        embed.add_field(
            name="⚡ Quick Start",
            value=(
                "`!adventure` — Start dungeon\n"
                "`!daily` — Claim daily coins\n"
                "`!coinflip 100 heads` — Quick gamble\n"
                "`!profile` — View your stats"
            ),
            inline=True,
        )
        embed.add_field(
            name="📚 Learn More",
            value=(
                "`!help <command>` — Command details\n"
                "`!help Games` — Category listing\n"
                "`!dungeon_guide` — Full RPG guide\n"
                "`!setchannel view` — Channel config"
            ),
            inline=True,
        )

        embed.set_footer(text="SONARR Bot  •  Commands auto-redirect to their set channels")
        await self.get_destination().send(embed=embed)

    async def _send_channel_help(self, profile):
        """Send a channel-specific help embed."""
        embed = discord.Embed(
            title=f"📖  {profile['title']}",
            color=profile.get("color", 0x2b2d31),
        )
        for section in profile.get("sections", []):
            embed.add_field(
                name=section["name"],
                value=section["value"],
                inline=False,
            )
        embed.set_footer(text="SONARR Bot  •  !help <command> for details")
        await self.get_destination().send(embed=embed)

    # ── Single command (!help play) ──
    async def send_command_help(self, command):
        sig = self.get_command_signature(command)

        # Get the cog icon
        cog_name = command.cog.qualified_name if command.cog else None
        icon = COG_ICONS.get(cog_name, "📌")

        embed = discord.Embed(
            title=f"{icon}  {sig}",
            description=command.help or command.brief or "No description.",
            color=0x2b2d31,
        )

        if command.aliases:
            embed.add_field(
                name="Aliases",
                value="  ".join(f"`!{a}`" for a in command.aliases),
                inline=False,
            )

        # Show which channel this command belongs to
        ctx = self.context
        if cog_name and ctx.guild:
            channel_map = {
                "Games": "games_channel",
                "Adventure": "dungeon_channel",
                "Economy": "economy_channel",
                "Shop": "economy_channel",
                "Music": "music_channel",
            }
            ch_key = channel_map.get(cog_name)
            if ch_key:
                mention = _get_channel_mention(ctx, ch_key)
                if mention:
                    embed.add_field(
                        name="Channel",
                        value=f"Use in → {mention}",
                        inline=False,
                    )

        embed.set_footer(text="<> = required  •  [] = optional")
        await self.get_destination().send(embed=embed)

    # ── Cog page (!help Music) ──
    async def send_cog_help(self, cog):
        ctx = self.context

        # Channel-specific override
        profile = _detect_channel_profile(ctx)
        if profile and cog.qualified_name in [c for cat in HELP_CATEGORIES for c in cat["cogs"] if cat.get("channel_key") and cat["channel_key"] in CHANNEL_HELP_PROFILES]:
            # Check if this cog is part of the current channel's profile
            for config_key, p in CHANNEL_HELP_PROFILES.items():
                set_ch = get_guild_config(ctx.guild.id, config_key) if ctx.guild else None
                if set_ch and int(set_ch) == ctx.channel.id:
                    await self._send_channel_help(p)
                    return

        icon = COG_ICONS.get(cog.qualified_name, "📂")
        embed = discord.Embed(
            title=f"{icon}  {cog.qualified_name} Commands",
            description=cog.description or "No description.",
            color=0x2b2d31,
        )

        filtered = await self.filter_commands(cog.get_commands(), sort=True)
        for cmd in filtered:
            # Use first line of help as brief
            brief = cmd.brief or cmd.help
            if brief:
                brief = brief.split("\n")[0]
                if len(brief) > 60:
                    brief = brief[:57] + "…"
            embed.add_field(
                name=f"`!{cmd.name}`",
                value=brief or "—",
                inline=True,
            )

        embed.set_footer(text="!help <command> for details")
        await self.get_destination().send(embed=embed)

    # ── Group page (!help playlist) ──
    async def send_group_help(self, group):
        sig = self.get_command_signature(group)
        cog_name = group.cog.qualified_name if group.cog else None
        icon = COG_ICONS.get(cog_name, "📌")

        embed = discord.Embed(
            title=f"{icon}  {sig}",
            description=group.help or group.brief or "No description.",
            color=0x2b2d31,
        )

        filtered = await self.filter_commands(group.commands, sort=True)
        for cmd in filtered:
            brief = cmd.brief or cmd.help
            if brief:
                brief = brief.split("\n")[0]
                if len(brief) > 60:
                    brief = brief[:57] + "…"
            embed.add_field(
                name=f"`!{group.name} {cmd.name}`",
                value=brief or "—",
                inline=True,
            )

        if group.aliases:
            embed.add_field(
                name="Aliases",
                value="  ".join(f"`!{a}`" for a in group.aliases),
                inline=False,
            )

        embed.set_footer(text="<> = required  •  [] = optional")
        await self.get_destination().send(embed=embed)

    async def send_error_message(self, error):
        import difflib
        ctx = self.context

        # Extract what the user typed
        invoked = ""
        if '"' in error:
            invoked = error.split('"')[1].lower()

        suggestion = None
        if invoked and len(invoked) >= 2:
            all_names = []
            for cmd in ctx.bot.commands:
                all_names.append(cmd.name.lower())
                all_names.extend(a.lower() for a in cmd.aliases)
                if isinstance(cmd, commands.Group):
                    for sub in cmd.commands:
                        all_names.append(f"{cmd.name} {sub.name}".lower())
            for cog_name in ctx.bot.cogs:
                all_names.append(cog_name.lower())
            all_names.extend(["games", "dungeon", "economy", "music", "adventure",
                              "dungeon_guide", "dguide", "shop", "admin", "levels"])

            matches = difflib.get_close_matches(invoked, all_names, n=1, cutoff=0.5)
            if matches:
                suggestion = matches[0]
            else:
                for name in all_names:
                    if name.startswith(invoked):
                        suggestion = name
                        break

        embed = discord.Embed(
            title="❌ Not found",
            description=error,
            color=0xff4444,
        )
        if suggestion:
            embed.add_field(
                name="💡 Did you mean...",
                value=f"**`!help {suggestion}`**",
                inline=False,
            )
        await self.get_destination().send(embed=embed)
