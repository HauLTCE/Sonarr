import discord
from discord.ext import commands, tasks
import random
import logging
import os
import asyncio
from datetime import datetime, timezone, timedelta
from dotenv import load_dotenv
from utils.economy import EconomyManager
from utils.premade_answers import COLD_RESPONSES

try:
    import google.generativeai as genai
except ImportError:
    genai = None

load_dotenv()

GEMINI_API_KEYS = [
    os.getenv("GEMINI_API_KEY_1"),
    os.getenv("GEMINI_API_KEY_2"),
    os.getenv("GEMINI_API_KEY_3"),
]

GEMINI_API_KEYS = [k for k in GEMINI_API_KEYS if k]

logger = logging.getLogger("bot")

class BotPersonality(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        
        self.models = [
            "gemini-2.0-flash",
            "gemini-1.5-flash",
            "gemini-1.5-flash-8b",
            "gemini-2.0-flash-lite",
        ]
        self.current_model_index = 0
        self.current_key_index = 0
        self.ai_available = False
        
        if GEMINI_API_KEYS and genai:
            try:
                genai.configure(api_key=GEMINI_API_KEYS[0])
                self.gemini_model = genai.GenerativeModel(self.models[0])
                self.ai_available = True
                logger.info(f"[BotPersonality] Gemini initialized with key 1/{len(GEMINI_API_KEYS)}, model: {self.models[0]}")
            except Exception as e:
                logger.error(f"[BotPersonality] Failed to initialize Gemini: {e}")
                self.gemini_model = None
        else:
            self.gemini_model = None
            logger.warning("[BotPersonality] Gemini not available - no API keys found")
        self.economy_manager = EconomyManager()
        self.last_idle_chat = datetime.now(timezone.utc)
        
        self.last_user_chat_time = {}
        
        self.rob_reasons = [
            {"reason": "I want money.", "percent": 0.04},
            {"reason": "You owe me.", "percent": 0.035},
            {"reason": "Tax collection.", "percent": 0.03},
            {"reason": "I'm bored and broke.", "percent": 0.05},
            {"reason": "Punishment for existing.", "percent": 0.02},
            {"reason": "Because I can.", "percent": 0.06},
            {"reason": "You're too rich anyway.", "percent": 0.1},
            {"reason": "Your vibes suck.", "percent": 0.045},
            {"reason": "Just because I like it.", "percent": 0.3},
        ]
        
        self.negative_keywords = ["fuck", "shit", "asshole", "bitch", "stupid", "dumb", "idiot", "trash", "worst", "hate", "die", "kill"]
        
        self.auto_rob_task.start()
        self.idle_chat_task.start()
        
        self.sleep_responses = [
            "The bot is asleep.",
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
    
    def secure_bot_wallet(self):
        """Keep only $500 in bot's wallet, deposit rest to bank for safety."""
        if not self.bot.user:
            return
        try:
            bot_id = str(self.bot.user.id)
            wallet = self.economy_manager.get_balance(bot_id, "wallet")
            if wallet > 500:
                excess = wallet - 500
                self.economy_manager.update_balance(bot_id, -excess, "wallet")
                self.economy_manager.update_balance(bot_id, excess, "bank")
                logger.info(f"[Bot] Secured ${excess} to bank. Wallet: $500")
        except Exception as e:
            pass
    
    async def punish_rude_user(self, message):
        """30% chance to rob users who use negative keywords."""
        content_lower = message.content.lower()
        is_rude = any(word in content_lower for word in self.negative_keywords)
        
        if is_rude and random.random() < 0.3:
            rob_entry = random.choice(self.rob_reasons)
            user_id = str(message.author.id)
            bot_id = str(self.bot.user.id)
            
            wallet = self.economy_manager.get_balance(user_id, "wallet")
            if wallet > 0:
                stolen = int(wallet * rob_entry["percent"])
                if stolen > 0:
                    self.economy_manager.update_balance(user_id, -stolen, "wallet")
                    self.economy_manager.update_balance(bot_id, stolen, "wallet")
                    try:
                        await message.channel.send(f"💰 Robbed ${stolen} from {message.author.mention}. {rob_entry['reason']}")
                    except:
                        pass

    def is_sleep_time(self):
        """Check if bot is in sleep mode (10PM - 6AM in UTC+7)"""
        utc_plus_7 = timezone(timedelta(hours=7))
        now = datetime.now(utc_plus_7)
        hour = now.hour
        return hour >= 22 or hour < 6

    async def classify_and_respond_with_ai(self, message_content):
        """Use Gemini AI to classify message type with minimal tokens, rotating keys and models"""
        if not self.gemini_model or not self.ai_available:
            return "⚠️ AI is currently offline. Try again later."
        
        total_keys = len(GEMINI_API_KEYS)
        total_models = len(self.models)
        max_attempts = total_keys * total_models
        attempts = 0
        
        while attempts < max_attempts:
            try:
                categories = list(COLD_RESPONSES.keys())
                
                prompt = f"""Classify this message into ONE category: {', '.join(categories)}
                Message: "{message_content}"
                Reply with ONLY the category name, nothing else."""

                response = await self.gemini_model.generate_content_async(prompt)
                category = response.text.strip().lower().replace("category:", "").strip()
                
                if category in COLD_RESPONSES:
                    self.ai_available = True
                    return random.choice(COLD_RESPONSES[category])
                else:
                    for cat in categories:
                        if cat in category or category in cat:
                            return random.choice(COLD_RESPONSES[cat])
                    return random.choice(COLD_RESPONSES["random"])
                    
            except Exception as e:
                error_str = str(e)
                if "429" in error_str or "quota" in error_str.lower() or "rate" in error_str.lower():
                    attempts += 1
                    
                    self.current_model_index = (self.current_model_index + 1) % total_models
                    
                    if self.current_model_index == 0:
                        self.current_key_index = (self.current_key_index + 1) % total_keys
                        new_key = GEMINI_API_KEYS[self.current_key_index]
                        genai.configure(api_key=new_key)
                        logger.warning(f"[AI] Rotating to key {self.current_key_index + 1}/{total_keys}")
                    
                    new_model = self.models[self.current_model_index]
                    self.gemini_model = genai.GenerativeModel(new_model)
                    logger.warning(f"[AI] Rate limited. Now: key {self.current_key_index + 1}, model: {new_model} ({attempts}/{max_attempts})")
                    await asyncio.sleep(1)
                    continue
                else:
                    logger.error(f"[AI] Error: {e}")
                    return random.choice(COLD_RESPONSES["random"])
        
        logger.error(f"[AI] All {total_keys} keys and {total_models} models exhausted")
        self.ai_available = False
        return "⚠️ AI quota exhausted on all keys. Try again later."

    @tasks.loop(minutes=random.randint(10, 30))
    async def auto_rob_task(self):
        """Automatically rob users with money (2-5% chance per check)"""
        if self.is_sleep_time():
            return
        try:
            if not self.bot.guilds or self.is_sleep_time():
                return
            
            guild = self.bot.guilds[0]
            bot_id = str(self.bot.user.id)
            
            potential_targets = []
            for member in guild.members:
                if member.bot:
                    continue
                
                wallet = self.economy_manager.get_balance(member.id, "wallet")
                bank = self.economy_manager.get_balance(member.id, "bank")
                total = wallet + bank
                
                if total > 100:
                    potential_targets.append((member, wallet, bank, total))
            
            if not potential_targets:
                return
            
            if random.random() < 0.03:
                target, wallet, bank, total = random.choices(
                    potential_targets,
                    weights=[t[3] for t in potential_targets],
                    k=1
                )[0]
                
                rob_from_bank = bank > wallet and random.random() < 0.6
                
                if rob_from_bank and bank > 0:
                    steal_percent = random.uniform(0.02, 0.08)
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
                    steal_percent = random.uniform(0.03, 0.10)
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
                
                guild_id = str(guild.id)
                config = self.bot.server_config.get(guild_id, {})
                general_id = config.get("general_channel")
                
                if general_id:
                    channel = self.bot.get_channel(general_id)
                    if channel:
                        await channel.send(f"💰 I just took ${stolen} from {target.mention}'s {location}. {reason}")
        
        except Exception as e:
            logger.error(f"[AutoRob] Error: {e}")
    
    @auto_rob_task.before_loop
    async def before_auto_rob(self):
        await self.bot.wait_until_ready()
    
    @tasks.loop(minutes=random.randint(15, 45))
    async def idle_chat_task(self):
        """Bot randomly chats in general channel after 4 hours of no user activity"""
        try:
            if not self.bot.guilds or self.is_sleep_time():
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
            
            last_chat = self.last_user_chat_time.get(guild_id)
            if last_chat:
                hours_since_chat = (datetime.now(timezone.utc) - last_chat).total_seconds() / 3600
                if hours_since_chat < 4:
                    return
            else:
                self.last_user_chat_time[guild_id] = datetime.now(timezone.utc)
                return
            
            if random.random() > 0.20:
                return
            
            if random.random() < 0.3:
                online_members = [
                    m for m in guild.members 
                    if not m.bot and m.status != discord.Status.offline
                ]
                if online_members:
                    target = random.choice(online_members)
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
                await channel.send(random.choice(self.idle_chat_lines))
        
        except Exception as e:
            logger.error(f"[IdleChat] Error: {e}")
    
    @idle_chat_task.before_loop
    async def before_idle_chat(self):
        await self.bot.wait_until_ready()

    @commands.Cog.listener()
    async def on_message(self, message):
        """Bot responds using AI classification and premade cold answers"""
        if message.author.bot or not message.guild:
            return
        
        guild_id = str(message.guild.id)
        config = self.bot.server_config.get(guild_id, {})
        general_id = config.get("general_channel")
        
        if general_id and message.channel.id == general_id:
            self.last_user_chat_time[guild_id] = datetime.now(timezone.utc)
        
        self.secure_bot_wallet()
        
        await self.punish_rude_user(message)
        
        if self.is_sleep_time():
            if self.bot.user in message.mentions or (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user):
                await message.reply("The bot is asleep.", mention_author=False)
            return
        
        mentioned_users = [u for u in message.mentions if u != self.bot.user and not u.bot]
        if mentioned_users and random.random() < 0.25:
            target = random.choice(mentioned_users)
            gossip = random.choice(self.gossip_lines).format(target=target.display_name)
            await message.channel.send(gossip)
            return
        
        if self.bot.user not in message.mentions and not (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user):
            return
        
        if message.content.startswith("!"):
            return
        
        content_for_ai = message.content.replace(f"<@{self.bot.user.id}>", "").replace(f"<@!{self.bot.user.id}>", "").strip()
        
        response = await self.classify_and_respond_with_ai(content_for_ai)
        
        try:
            await message.reply(response, mention_author=False)
        except Exception as e:
            logger.error(f"Error sending message: {e}")


async def setup(bot):
    await bot.add_cog(BotPersonality(bot))