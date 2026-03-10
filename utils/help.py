import discord
from discord.ext import commands

class PrettyHelp(commands.HelpCommand):
    async def send_bot_help(self, mapping):
        embed = discord.Embed(title="🤖 Bot Command List", description="Here are the available commands:", color=0x00ff00)
        
        ignored_cogs = []

        for cog, cmds in mapping.items():
            if cog and cog.qualified_name not in ignored_cogs:
                name = cog.qualified_name
                filtered = await self.filter_commands(cmds, sort=True)
                
                if filtered:
                    command_names = [f"`!{c.name}`" for c in filtered]
                    chunk = ", ".join(command_names)
                    if len(chunk) > 1024:
                        chunk = chunk[:1020] + "..."
                    embed.add_field(name=f"📂 {name}", value=chunk, inline=False)

        embed.set_footer(text="Type !help <command> for more details.")
        await self.get_destination().send(embed=embed)

    async def send_command_help(self, command):
        embed = discord.Embed(title=f"!{command.name}", description=command.help, color=0x00ff00)
        if command.aliases:
            embed.add_field(name="Aliases", value=", ".join(command.aliases))
        await self.get_destination().send(embed=embed)
