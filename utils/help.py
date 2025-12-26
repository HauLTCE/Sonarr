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
                    command_lines = [f"`!{c.name}`: {c.short_doc}" for c in filtered]
                    
                    current_chunk = ""
                    field_counter = 1
                    
                    for line in command_lines:
                        if len(current_chunk) + len(line) + 1 > 1024:
                            field_name = f"📂 {name}" if field_counter == 1 else f"📂 {name} (Part {field_counter})"
                            embed.add_field(name=field_name, value=current_chunk, inline=False)
                            
                            current_chunk = line + "\n"
                            field_counter += 1
                        else:
                            current_chunk += line + "\n"
                    
                    if current_chunk:
                        field_name = f"📂 {name}" if field_counter == 1 else f"📂 {name} (Part {field_counter})"
                        embed.add_field(name=field_name, value=current_chunk, inline=False)
        
        embed.set_footer(text="Type !help <command> for more details.")
        await self.get_destination().send(embed=embed)

    async def send_command_help(self, command):
        embed = discord.Embed(title=f"!{command.name}", description=command.help, color=0x00ff00)
        if command.aliases:
            embed.add_field(name="Aliases", value=", ".join(command.aliases))
        await self.get_destination().send(embed=embed)