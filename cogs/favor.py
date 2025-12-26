import discord
from discord.ext import commands, tasks
import random
import logging
import asyncio
from datetime import datetime, timezone
from utils.economy import EconomyManager
from utils.affection_cache import affection_cache
from utils.knowledge_base import kb
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
            
            # Learn behavior: track who talks about whom
            kb.update_user_rating(str(message.author.id), score_delta=1)  # Track activity
            return
        
        # Only respond to @mentions or replies to the bot
        if self.bot.user not in message.mentions and not (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user):
            return
        
        logger.debug(f"[Favor] Mention detected from {message.author}: {message.content}")
        
        # Don't respond to commands
        if message.content.startswith("!"):
            return
        
        # Everyone gets cold treatment (affection = 0)
        affection = 0
        
        # Get user's rating/grade from AI
        user_rating = kb.get_user_rating(str(message.author.id))
        
        # Determine message type from content
        content_lower = message.content.lower()
        
        # Check for specific message types with priority order
        goodbye_words = ["bye", "goodbye", "see you", "later", "gotta go", "leaving", "gtg", "cya", "farewell"]
        thanks_words = ["thank", "thanks", "thx", "appreciate", "grateful", "ty"]
        apology_words = ["sorry", "apologize", "my bad", "my fault", "forgive", "apologies"]
        question_words = ["what", "why", "how", "where", "when", "who", "which", "can you", "could you", "would you", "should you", "is it", "are you", "do you"]
        compliment_words = ["good", "great", "love", "like", "awesome", "cool", "nice", "respect", "best", "amazing", "smart", "clever", "brilliant", "wonderful", "fantastic", "excellent"]
        request_words = ["help", "please", "can", "could", "would", "give", "do", "make", "show", "tell", "explain", "need"]
        insult_words = ["stupid", "dumb", "idiot", "bad", "terrible", "worst", "hate", "suck", "trash", "useless", "worthless"]
        joke_words = ["haha", "lol", "lmao", "joke", "funny", "hilarious", "comedy"]
        confusion_indicators = ["??" in content_lower, "huh" in content_lower, "what the" in content_lower, "confused" in content_lower]
        
        # Priority detection (more specific first)
        if any(word in content_lower for word in goodbye_words):
            message_type = "goodbye"
        elif any(word in content_lower for word in thanks_words):
            message_type = "thanks"
        elif any(word in content_lower for word in apology_words):
            message_type = "apology"
        elif any(word in content_lower for word in insult_words):
            message_type = "insult"
        elif any(word in content_lower for word in joke_words) or ("😂" in message.content or "🤣" in message.content):
            message_type = "joke"
        elif any(word in content_lower for word in question_words):
            message_type = "question"
        elif any(word in content_lower for word in compliment_words):
            message_type = "compliment"
        elif any(word in content_lower for word in request_words):
            message_type = "request"
        elif any(confusion_indicators):
            message_type = "confusion"
        elif len(content_lower.split()) <= 3 and not any(word in content_lower for word in ["hi", "hey", "hello"]):
            message_type = "random"
        else:
            message_type = "greeting"
        
        # Get premade response based on affection tier and message type
        response, affection_delta = get_response(affection, ai_grade=user_rating.get("grade"), message_type=message_type)
        
        logger.debug(f"[Favor] Affection: {affection}, Type: {message_type}, Response: {response}, Delta: {affection_delta}")
        
        # Update AI rating for this interaction
        kb.update_user_rating(str(message.author.id), chat_delta=1, score_delta=1)
        kb.add_affection_source(str(message.author.id), "chat", affection_delta)
        
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
        """Check the bot's behavioral analysis of a user."""
        member = member or ctx.author
        rating = kb.get_user_rating(str(member.id))
        
        embed = discord.Embed(title="📊 Behavioral Analysis", color=0x9C27B0)
        embed.description = "I track everything. Here's what I know about you."
        embed.add_field(name=f"Target: {member.display_name}", value=rating["grade"], inline=False)
        embed.add_field(name="Data Collected", value=
            f"Interactions Logged: {rating['chats']}\n"
            f"Activity Score: {rating['score']}\n"
            f"Contributions: {rating['qa_contrib']}", inline=False
        )
        if rating["notes"]:
            embed.add_field(name="My Notes", value=rating["notes"], inline=False)
        else:
            embed.add_field(name="My Notes", value="Not enough data yet. But I'm watching.", inline=False)
        
        await ctx.send(embed=embed)

async def setup(bot):
    await bot.add_cog(Favor(bot))