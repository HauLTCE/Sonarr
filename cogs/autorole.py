import discord
from discord.ext import commands
import logging

logger = logging.getLogger("bot")

class AutoRole(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.default_role_name = "Thành viên"
    
    @commands.Cog.listener()
    async def on_member_join(self, member: discord.Member):
        """Automatically assign the default role to new members."""
        if member.bot:
            return
        
        role = discord.utils.get(member.guild.roles, name=self.default_role_name)
        
        if not role:
            logger.warning(f"[AutoRole] Role '{self.default_role_name}' not found in {member.guild.name}")
            return
        
        try:
            await member.add_roles(role, reason="Auto-assigned on join")
            logger.info(f"[AutoRole] Assigned '{self.default_role_name}' to {member.display_name} in {member.guild.name}")
        except discord.Forbidden:
            logger.error(f"[AutoRole] No permission to assign role to {member.display_name}")
        except discord.HTTPException as e:
            logger.error(f"[AutoRole] Failed to assign role: {e}")


async def setup(bot):
    await bot.add_cog(AutoRole(bot))
