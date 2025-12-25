from discord.ext import commands

class WrongChannelError(commands.CheckFailure):
    def __init__(self, channel_id):
        self.channel_id = channel_id

def is_music_channel():
    async def predicate(ctx):
        if not ctx.guild:
            return True
        
        guild_id = str(ctx.guild.id)
        config = ctx.bot.server_config.get(guild_id, {})
        music_channel_id = config.get("music_channel")

        if not music_channel_id:
            return True
        
        if ctx.channel.id == music_channel_id:
            return True
        
        raise WrongChannelError(music_channel_id)
    return commands.check(predicate)