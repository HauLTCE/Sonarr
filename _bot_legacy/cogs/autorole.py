import discord
from discord.ext import commands
import logging

from utils.config import get_guild_config, set_guild_config

logger = logging.getLogger("bot")


class AutoRole(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        # Fallback role name used only when a guild hasn't configured an autorole.
        # Was the sole (hardcoded) source, so any guild without a role by this exact
        # name silently got no autorole. Now configurable via !autorole.
        self.default_role_name = "Thành viên"

    def _resolve_role(self, guild: discord.Guild):
        """Configured autorole (by id) for this guild, else the legacy name fallback."""
        role_id = get_guild_config(guild.id, "autorole_id")
        if role_id:
            role = guild.get_role(int(role_id))
            if role:
                return role
        return discord.utils.get(guild.roles, name=self.default_role_name)

    @commands.Cog.listener()
    async def on_member_join(self, member: discord.Member):
        """Automatically assign the configured role to new members."""
        if member.bot:
            return

        role = self._resolve_role(member.guild)

        if not role:
            logger.warning(f"[AutoRole] No autorole configured/found in {member.guild.name}. "
                           "Set one with !autorole <role>.")
            return

        try:
            await member.add_roles(role, reason="Auto-assigned on join")
            logger.info(f"[AutoRole] Assigned '{role.name}' to {member.display_name} in {member.guild.name}")
        except discord.Forbidden:
            logger.error(f"[AutoRole] No permission to assign role to {member.display_name}")
        except discord.HTTPException as e:
            logger.error(f"[AutoRole] Failed to assign role: {e}")

    @commands.command(name="autorole")
    @commands.has_permissions(administrator=True)
    async def autorole(self, ctx, *, role: discord.Role = None):
        """Set (or show) the role auto-assigned to new members. Admin only.

        Usage: `!autorole @Member` — set it. `!autorole` — show the current one.
        """
        if role is None:
            current = self._resolve_role(ctx.guild)
            if current:
                await ctx.send(f"New members currently receive: **{current.name}**. "
                               "Use `!autorole <role>` to change it.")
            else:
                await ctx.send("No autorole is configured. Use `!autorole <role>` to set one.")
            return

        # Guard against assigning a role above the bot (it couldn't grant it anyway).
        if ctx.guild.me.top_role <= role and ctx.guild.me != role.guild.owner:
            await ctx.send(f"⚠️ I can't assign **{role.name}** — it's not below my highest role.")
            return

        set_guild_config(ctx.guild.id, "autorole_id", role.id)
        await ctx.send(f"✅ New members will now automatically receive **{role.name}**.")


async def setup(bot):
    await bot.add_cog(AutoRole(bot))
