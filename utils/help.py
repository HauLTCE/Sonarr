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



# import discord
# from discord.ext import commands

# class PrettyHelp(commands.HelpCommand):
#     async def send_bot_help(self, mapping):
#         """Displays the main help menu with categories."""
#         embed = discord.Embed(
#             title="🤖 Bot Help Menu", 
#             description="Select a category below by typing `!help <category>`.\n*Example: `!help music`*", 
#             color=0x00ff00
#         )
        
#         cog_desc_map = {
#             "Moderation": "🛡️ **Moderator** (Kick, Ban, Purge)",
#             "Admin": "🔒 **Admin** (Set channels, Config)",
#             "Levels": "📊 **Levels** (Rank, Leaderboard)",
#             "AIChat": "🧠 **AI Chat** (Chat with SONARR)",
#             "Games": "🎲 **Games** (Economy, Gambling, Shop)",
#             "Favor": "💖 **Favor** (Affection system)",
#             "Utility": "🛠️ **Utility** (Ping, Remind)",
#             "Fun": "🎉 **Fun** (Polls, 8ball)",
#             "Music": "🎵 **Music** (Play, Queue, Playlist)",
#             "BotEconomy": "🤖 **Bot Eco** (Owner only)"
#         }

#         found_cogs = []

#         for cog, cmds in mapping.items():
#             if cog:
#                 filtered = await self.filter_commands(cmds, sort=True)
                
#                 if filtered:
#                     name = cog.qualified_name
#                     display_text = cog_desc_map.get(name, f"📂 **{name}**")
                    
#                     found_cogs.append(f"{display_text}\n`!help {name}`")

#         if found_cogs:
#             embed.add_field(name="Categories", value="\n\n".join(found_cogs), inline=False)
#         else:
#             embed.description = "No commands available for you."

#         embed.set_footer(text="Type !help <category> to see commands inside!")
#         await self.get_destination().send(embed=embed)

#     async def send_cog_help(self, cog):
#         """Displays commands within a specific category (Cog)."""
#         embed = discord.Embed(
#             title=f"📂 {cog.qualified_name} Commands", 
#             description=cog.description or "No description provided.", 
#             color=0x00ff00
#         )
        
#         cmds = cog.get_commands()
#         filtered = await self.filter_commands(cmds, sort=True)
        
#         if filtered:
#             for c in filtered:
#                 embed.add_field(
#                     name=f"!{c.name}", 
#                     value=c.short_doc or "No description", 
#                     inline=False
#                 )
#             embed.set_footer(text=f"Total: {len(filtered)} commands")
#         else:
#             embed.description = "❌ You don't have permission to use any commands in this category."
        
#         await self.get_destination().send(embed=embed)

#     async def send_command_help(self, command):
#         """Displays help for a specific command."""
#         embed = discord.Embed(title=f"!{command.name}", description=command.help or "No description.", color=0x00ff00)
        
#         if command.aliases:
#             embed.add_field(name="Aliases", value=", ".join(command.aliases), inline=False)
        
#         if command.usage:
#             embed.add_field(name="Usage", value=f"`!{command.name} {command.usage}`", inline=False)
            
#         await self.get_destination().send(embed=embed)

#     async def send_error_message(self, error):
#         """Handles errors like typing '!help nonexist'."""
#         embed = discord.Embed(title="❌ Error", description=error, color=0xff0000)
#         await self.get_destination().send(embed=embed)