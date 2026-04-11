import discord
from discord.ext import commands


# ── Emojis per cog for visual flair ──
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
}


class PrettyHelp(commands.HelpCommand):
    """A polished, categorised help command with descriptions."""

    # ── Overview page (!help) ──
    async def send_bot_help(self, mapping):
        embed = discord.Embed(
            title="📖 SONARR — Command Reference",
            description=(
                "Use **`!help <command>`** for detailed info on any command.\n"
                "Use **`!help <category>`** for all commands in a category.\n"
                "Commands can be chained with `&&` — e.g. `!play song && !volume 80`"
            ),
            color=0x2b2d31,
        )

        for cog, cmds in mapping.items():
            if cog is None:
                continue
            name = cog.qualified_name
            filtered = await self.filter_commands(cmds, sort=True)
            if not filtered:
                continue

            icon = COG_ICONS.get(name, "📂")
            brief_list = "  ".join(f"`{c.name}`" for c in filtered)

            # Truncate if too long    
            if len(brief_list) > 1024:
                brief_list = brief_list[:1020] + " …"

            embed.add_field(
                name=f"{icon}  {name}",
                value=brief_list,
                inline=False,
            )

        embed.set_footer(text="SONARR Bot • !help <command> for details")
        await self.get_destination().send(embed=embed)

    # ── Single command page (!help play) ──
    async def send_command_help(self, command):
        sig = self.get_command_signature(command)
        embed = discord.Embed(
            title=f"📌  {sig}",
            description=command.help or command.brief or "No description.",
            color=0x2b2d31,
        )

        if command.aliases:
            embed.add_field(
                name="Aliases",
                value="  ".join(f"`!{a}`" for a in command.aliases),
                inline=False,
            )

        embed.set_footer(text="<> = required  •  [] = optional")
        await self.get_destination().send(embed=embed)

    # ── Cog page (!help Music) ──
    async def send_cog_help(self, cog):
        icon = COG_ICONS.get(cog.qualified_name, "📂")
        embed = discord.Embed(
            title=f"{icon}  {cog.qualified_name} Commands",
            description=cog.description or "No description.",
            color=0x2b2d31,
        )

        filtered = await self.filter_commands(cog.get_commands(), sort=True)
        for cmd in filtered:
            brief = cmd.brief or cmd.help
            if brief and len(brief) > 80:
                brief = brief[:77] + "…"
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
        embed = discord.Embed(
            title=f"📌  {sig}",
            description=group.help or group.brief or "No description.",
            color=0x2b2d31,
        )

        filtered = await self.filter_commands(group.commands, sort=True)
        for cmd in filtered:
            brief = cmd.brief or cmd.help
            if brief and len(brief) > 80:
                brief = brief[:77] + "…"
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
        embed = discord.Embed(
            title="❌ Command not found",
            description=error,
            color=0xff4444,
        )
        await self.get_destination().send(embed=embed)
