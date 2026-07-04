"""
cogs.sonarr_ai — Sonarr's chat personality cog.

The SonarrAI cog (cog.py) gates on mention/reply, then hands the message to the
E.L.A.I.N.E engine (elaine.discord_bot.ElaineBot) — a symbolic finite state machine
with per-user affect and memory (no LLM). It sends back the deterministic text reply.
"""

from .cog import SonarrAI


async def setup(bot):
    await bot.add_cog(SonarrAI(bot))
