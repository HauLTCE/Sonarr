"""
cogs.sonarr_ai — Sonarr AI personality cog package.

Split into modules:
- cog.py:                 SonarrAI class (init, on_message, lifecycle)
- brain_integration.py:   Brain-based response selection
- classifier_pipeline.py: Message classification pipeline
- background_tasks.py:    Idle chat, memory cleanup, gossip
"""

from .cog import SonarrAI


async def setup(bot):
    await bot.add_cog(SonarrAI(bot))
