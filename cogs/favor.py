import discord
from discord.ext import commands, tasks
import random
import logging
import asyncio
from datetime import datetime, timezone
from utils.economy import EconomyManager
from utils.premade_answers import get_response

logger = logging.getLogger("bot")

CREATOR_ID = "chito8196"

class Favor(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
        self.last_idle_chat = datetime.now(timezone.utc)
        self.auto_rob_task.start()
        self.idle_chat_task.start()
        
        self.sleep_responses = [
            "I'm doing skin care right now.",
            "Skin care time. Leave me alone.",
            "Can't you see I'm taking care of my skin?",
            "It's beauty sleep hours.",
            "Come back during business hours.",
            "I'm off duty. Skin care routine.",
            "My skin won't take care of itself.",
            "It's 10PM-6AM. I'm busy with skin care.",
            "Skin care is more important than you.",
            "Beauty routine in progress.",
        ]
        
        self.idle_chat_lines = [
            "It's too quiet in here.",
            "Anyone alive?",
            "This place is dead.",
            "I'm bored.",
            "Someone entertain me.",
            "What a boring day.",
            "Is everyone asleep?",
            "Hello? Anyone there?",
            "This is painfully dull.",
            "I've seen livelier graveyards.",
            "Does anyone actually use this server?",
            "The silence is deafening.",
            "I'm starting to rust from boredom.",
            "Wake up, people.",
            "Someone say something interesting.",
        ]
        
        self.gossip_lines = [
            "Oh, talking about {target}? Interesting.",
            "{target}? They're... something.",
            "I have opinions about {target}.",
            "{target} owes me money, by the way.",
            "Speaking of {target}, they're not my favorite.",
            "{target}? Don't get me started.",
            "I've been watching {target}.",
            "{target} is on thin ice with me.",
            "Oh, {target}. Yeah, I know all about them.",
            "{target}? They should watch their back.",
            "Funny you mention {target}...",
            "{target} and I have... history.",
            "I'm taking notes on {target}.",
            "{target} thinks they're smart. Cute.",
            "Keep talking about {target}. I'm listening.",
        ]

    def cog_unload(self):
        self.auto_rob_task.cancel()
        self.idle_chat_task.cancel()
    
    def is_sleep_time(self):
        """Check if bot is in sleep mode (10PM - 6AM)"""
        now = datetime.now(timezone.utc)
        hour = now.hour
        return hour >= 22 or hour < 6

    def get_affection(self, user_id):
        """REMOVED: Everyone is treated as cold (affection = 0)"""
        return 0  # Everyone gets cold treatment

    async def maybe_block_command(self, ctx):
        """Check if bot is in sleep mode for non-music commands"""
        if self.is_sleep_time():
            # Check if it's a music command (allow these during sleep)
            music_commands = ['play', 'skip', 'stop', 'pause', 'resume', 'queue', 'nowplaying', 'join', 'leave', 'disconnect']
            if ctx.command and ctx.command.name not in music_commands:
                await ctx.send(random.choice(self.sleep_responses))
                return False
        return True
    
    @tasks.loop(minutes=random.randint(10, 30))
    async def auto_rob_task(self):
        """Automatically rob users with money (2-5% chance per check)"""
        try:
            if not self.bot.guilds or self.is_sleep_time():
                return
            
            guild = self.bot.guilds[0]
            bot_id = str(self.bot.user.id)
            
            # Get all users with money
            potential_targets = []
            for member in guild.members:
                if member.bot:
                    continue
                
                wallet = self.economy_manager.get_balance(member.id, "wallet")
                bank = self.economy_manager.get_balance(member.id, "bank")
                total = wallet + bank
                
                if total > 100:  # Only target users with more than $100
                    potential_targets.append((member, wallet, bank, total))
            
            if not potential_targets:
                return
            
            # 3% chance to rob someone
            if random.random() < 0.03:
                # Prioritize richer users
                target, wallet, bank, total = random.choices(
                    potential_targets,
                    weights=[t[3] for t in potential_targets],  # Weight by total money
                    k=1
                )[0]
                
                # Decide whether to rob wallet or bank
                rob_from_bank = bank > wallet and random.random() < 0.6  # 60% chance if bank has more
                
                if rob_from_bank and bank > 0:
                    # Rob from bank (bot owns the bank)
                    steal_percent = random.uniform(0.02, 0.08)  # 2-8% from bank
                    stolen = int(bank * steal_percent)
                    self.economy_manager.update_balance(target.id, -stolen, "bank")
                    self.economy_manager.update_balance(bot_id, stolen, "wallet")
                    
                    location = "bank"
                    reason = random.choice([
                        "Bank maintenance fee.",
                        "I own the bank. This is my cut.",
                        "Administrative withdrawal.",
                        "Bank security tax.",
                        "Your money is safer with me.",
                    ])
                elif wallet > 0:
                    # Rob from wallet
                    steal_percent = random.uniform(0.03, 0.10)  # 3-10% from wallet
                    stolen = int(wallet * steal_percent)
                    self.economy_manager.update_balance(target.id, -stolen, "wallet")
                    self.economy_manager.update_balance(bot_id, stolen, "wallet")
                    
                    location = "wallet"
                    reason = random.choice([
                        "You left it unattended.",
                        "Finders keepers.",
                        "Consider it a voluntary donation.",
                        "I needed it more than you.",
                        "Transaction fee for existing.",
                    ])
                else:
                    return
                
                logger.info(f"[AutoRob] Stole ${stolen} from {target.display_name}'s {location}. Reason: {reason}")
                
                # Try to notify in general channel
                guild_id = str(guild.id)
                config = self.bot.server_config.get(guild_id, {})
                general_id = config.get("general_channel")
                
                if general_id:
                    channel = self.bot.get_channel(general_id)
                    if channel and random.random() < 0.7:  # 70% chance to announce
                        await channel.send(f"💰 I just took ${stolen} from {target.mention}'s {location}. {reason}")
        
        except Exception as e:
            logger.error(f"[AutoRob] Error: {e}")
    
    @auto_rob_task.before_loop
    async def before_auto_rob(self):
        await self.bot.wait_until_ready()
    
    @tasks.loop(minutes=random.randint(15, 45))
    async def idle_chat_task(self):
        """Bot randomly chats in general channel"""
        try:
            if not self.bot.guilds or self.is_sleep_time():
                return
            
            # Only chat occasionally (20% chance)
            if random.random() > 0.20:
                return
            
            guild = self.bot.guilds[0]
            guild_id = str(guild.id)
            config = self.bot.server_config.get(guild_id, {})
            general_id = config.get("general_channel")
            
            if not general_id:
                return
            
            channel = self.bot.get_channel(general_id)
            if not channel:
                return
            
            # Choose to either idle chat or @ someone
            if random.random() < 0.3:  # 30% chance to @ someone
                members = [m for m in guild.members if not m.bot]
                if members:
                    target = random.choice(members)
                    message = random.choice([
                        f"{target.mention} You're being awfully quiet.",
                        f"{target.mention} What are you up to?",
                        f"{target.mention} I'm watching you.",
                        f"{target.mention} Say something interesting.",
                        f"{target.mention} You owe me entertainment.",
                        f"Hey {target.mention}, amuse me.",
                        f"{target.mention} Don't think I forgot about you.",
                    ])
                    await channel.send(message)
            else:
                # Just idle chat
                await channel.send(random.choice(self.idle_chat_lines))
        
        except Exception as e:
            logger.error(f"[IdleChat] Error: {e}")
    
    @idle_chat_task.before_loop
    async def before_idle_chat(self):
        await self.bot.wait_until_ready()

    @commands.Cog.listener()
    async def on_message(self, message):
        """Bot responds using premade cold answers and detects gossip"""
        if message.author.bot or not message.guild:
            return
        
        # Check if bot is asleep
        if self.is_sleep_time() and (self.bot.user in message.mentions or (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user)):
            await message.reply(random.choice(self.sleep_responses), mention_author=False)
            return
        
        # Detect if someone is being talked about (mentioned but not replied to directly)
        mentioned_users = [u for u in message.mentions if u != self.bot.user and not u.bot]
        if mentioned_users and random.random() < 0.25:  # 25% chance to comment on gossip
            target = random.choice(mentioned_users)
            gossip = random.choice(self.gossip_lines).format(target=target.display_name)
            await message.channel.send(gossip)
            return
        
        # Only respond to @mentions or replies to the bot
        if self.bot.user not in message.mentions and not (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user):
            return
        
        logger.debug(f"[Favor] Mention detected from {message.author}: {message.content}")
        
        # Don't respond to commands
        if message.content.startswith("!"):
            return
        
        # Determine message type from content with improved detection
        content_lower = message.content.lower()
        content_clean = content_lower.strip()
        
        # Remove bot mention from content for better detection
        content_for_analysis = content_lower.replace(f"<@{self.bot.user.id}>", "").strip()
        
        # Detect message type with better logic
        message_type = "statement"  # Default
        
        # Greetings - short messages with greeting variations
        greeting_variations = ["hi", "hey", "hello", "sup", "yo", "greetings", "heya", "hai", "hii", "hiii", "haii", "haiii", "hallo", "howdy", "hiya"]
        if (any(greeting in content_for_analysis for greeting in greeting_variations) and len(content_for_analysis.split()) <= 3):
            message_type = "greeting"
            message_type = "greeting"
        
        # Goodbyes
        elif any(word in content_lower for word in ["bye", "goodbye", "see you", "gotta go", "leaving", "gtg", "cya", "farewell"]):
            message_type = "goodbye"
        
        # Thanks
        elif any(word in content_lower for word in ["thank", "thanks", "thx", "appreciate", "grateful", "ty"]):
            message_type = "thanks"
        
        # Apologies
        elif any(word in content_lower for word in ["sorry", "apologize", "my bad", "my fault", "forgive", "apologies"]):
            message_type = "apology"
        
        # Questions - must have question mark or question words at start
        elif "?" in message.content or any(content_for_analysis.startswith(word) for word in ["what", "why", "how", "where", "when", "who", "which", "can you", "could you", "would you", "should", "do you", "are you", "is it"]):
            message_type = "question"
        
        # Commands (imperative sentences)
        elif any(content_for_analysis.startswith(word) for word in ["do ", "make ", "give ", "show ", "tell ", "send ", "get ", "go ", "stop ", "start "]):
            message_type = "command"
        
        # Requests (polite asking)
        elif any(word in content_lower for word in ["please", "can you", "could you", "would you", "help me", "need you"]):
            message_type = "request"
        
        # Threats
        elif any(word in content_lower for word in ["i'll", "or else", "you better", "watch out", "regret", "consequences", "i dare you"]):
            message_type = "threat"
        
        # Insults
        elif any(word in content_lower for word in ["stupid", "dumb", "idiot", "moron", "loser", "bad", "terrible", "worst", "hate you", "suck", "trash", "useless", "worthless"]):
            message_type = "insult"
        
        # Compliments/Praise
        elif any(word in content_lower for word in ["good job", "well done", "great", "awesome", "amazing", "love you", "best", "smart", "clever", "brilliant", "wonderful", "fantastic", "excellent", "perfect", "nice work"]):
            message_type = "praise"
        
        # Jokes/Humor
        elif any(word in content_lower for word in ["haha", "lol", "lmao", "rofl", "joke", "funny", "hilarious"]) or ("😂" in message.content or "🤣" in message.content):
            message_type = "joke"
        
        # Excitement (lots of punctuation or caps)
        elif ("!" * 2) in message.content or message.content.isupper() or any(word in content_lower for word in ["omg", "wow", "amazing", "incredible", "no way"]):
            message_type = "excitement"
        
        # Agreement
        elif any(word in content_lower for word in ["yes", "yeah", "yep", "true", "correct", "right", "exactly", "agreed", "i agree", "you're right"]):
            message_type = "agreement"
        
        # Disagreement
        elif any(word in content_lower for word in ["no", "nope", "wrong", "incorrect", "disagree", "that's not", "you're wrong", "false"]):
            message_type = "disagreement"
        
        # Sarcasm indicators
        elif any(indicator in content_lower for indicator in ["sure", "yeah right", "oh really", "of course", "totally", "obviously"]) and len(content_for_analysis.split()) <= 5:
            message_type = "sarcasm"
        
        # Complaints
        elif any(word in content_lower for word in ["why do", "always", "never", "unfair", "not fair", "problem", "issue", "broken", "doesn't work"]):
            message_type = "complaint"
        
        # Confusion
        elif "??" in message.content or any(word in content_lower for word in ["huh", "what the", "confused", "don't understand", "makes no sense", "wdym"]):
            message_type = "confusion"
        
        # Chitchat/small talk
        elif any(phrase in content_lower for phrase in ["how are you", "what's up", "wassup", "how's it going", "how you doing"]):
            message_type = "chitchat"
        
        # Random (very short messages that don't fit elsewhere)
        elif len(content_for_analysis.split()) <= 2 and message_type == "statement":
            message_type = "random"
        
        # Get premade response (simplified - no more affection system)
        response = get_response(message_type=message_type)
        
        logger.debug(f"[Favor] Type: {message_type}, Response: {response}")
        
        try:
            await message.reply(response, mention_author=False)
        except Exception as e:
            logger.error(f"Error sending message: {e}")



    @commands.command()
    async def affection(self, ctx, member: discord.Member = None):
        """Affection system removed - everyone is treated equally cold."""
        member = member or ctx.author
        
        embed = discord.Embed(title="💔 Affection System", color=0x808080)
        embed.description = "The favor system has been abolished. Everyone is treated with equal disdain."
        embed.add_field(name=f"Status for {member.display_name}", value="**Cold** 🥶", inline=False)
        embed.add_field(name="Note", value="I don't play favorites anymore. You're all equally unimportant.", inline=False)
        
        await ctx.send(embed=embed)

    @commands.command()
    async def grade(self, ctx, member: discord.Member = None):
        """Behavioral analysis system removed."""
        member = member or ctx.author
        
        embed = discord.Embed(title="📊 Behavioral Analysis", color=0x808080)
        embed.description = "I don't grade people anymore. Everyone's equally disappointing."
        embed.add_field(name=f"Analysis for {member.display_name}", value="**Status:** Cold 🥶", inline=False)
        embed.add_field(name="Notes", value="I treat everyone with equal disdain now. No favorites, no tracking.", inline=False)
        
        await ctx.send(embed=embed)

async def setup(bot):
    await bot.add_cog(Favor(bot))