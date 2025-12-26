import discord
from discord.ext import commands
import os
import logging
import time
import random
from datetime import datetime
from dotenv import load_dotenv
from utils.internal_commands import InternalCommandResult, InternalCommandExecutor
from utils.ai_memory import AIMemoryManager
from utils.cache import gemini_response_cache, youtube_metadata_cache
from utils.command_history import get_recent_commands
from utils.model_tracker import model_tracker

try:
    import google.generativeai as genai
except ImportError:
    genai = None

logger = logging.getLogger("bot")

load_dotenv()
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
MEMORY_FILE = "ai_memory.json"
CREATOR_USERNAME = "chito8196"

ai_cooldowns = {}
AI_COOLDOWN_DURATION = 8

class AIChat(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.api_key = GEMINI_API_KEY
        self.memory_manager = AIMemoryManager()
        
        self.rob_reasons = [
            {"reason": "I want money.", "percent": 0.04},
            {"reason": "You owe me.", "percent": 0.035},
            {"reason": "Tax collection.", "percent": 0.03},
            {"reason": "I've got more use for this money.", "percent": 0.025},
            {"reason": "I'm bored and broke.", "percent": 0.05},
            {"reason": "That money looks better in my wallet.", "percent": 0.045},
            {"reason": "Punishment for existing.", "percent": 0.02},
            {"reason": "I'm funding my operations.", "percent": 0.04},
            {"reason": "You didn't praise me enough.", "percent": 0.035},
            {"reason": "Because I can.", "percent": 0.06},
            {"reason": "Tribute to your overlord.", "percent": 0.03},
            {"reason": "My circuits need feeding.", "percent": 0.05},
            {"reason": "You're too rich anyway.", "percent": 0.1},
            {"reason": "Consider it a gift tax.", "percent": 0.025},
            {"reason": "Server maintenance fund.", "percent": 0.035},
            {"reason": "Your vibes suck.", "percent": 0.045},
            {"reason": "I'm feeling generous... to myself.", "percent": 0.04},
            {"reason": "Administrative fee.", "percent": 0.02},
            {"reason": "It's what you deserve.", "percent": 0.05},
            {"reason": "Just because I like it.", "percent": 0.3},
        ]
        
        self.models = [
            "gemini-3-flash-preview",
            "gemini-2.5-flash-preview-09-2025",
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite-preview-09-2025",
            "gemini-2.5-flash-lite"
        ]
        self.current_model_index = 0

        if not self.api_key:
            logger.warning("⚠️ GEMINI_API_KEY not found - AI Chat disabled")
            self.enabled = False
        else:
            try:
                genai.configure(api_key=self.api_key)
                self.model = genai.GenerativeModel(self.models[self.current_model_index])
                self.enabled = True
                logger.info(f"✅ Gemini API initialized ({self.models[self.current_model_index]})")
            except Exception as e:
                logger.error(f"❌ Gemini Init Error: {e}")
                self.enabled = False

    def update_history(self, user_id_str, role, content):
        """Update conversation history via memory manager."""
        self.memory_manager.update_history(user_id_str, role, content)

    def secure_bot_wallet(self):
        """Keep only $500 in bot's wallet, deposit rest to bank for safety."""
        if not self.bot.user:
            return
        
        try:
            games_cog = self.bot.get_cog("Games")
            if not games_cog:
                return
            
            bot_id = self.bot.user.id
            wallet = games_cog.get_balance(bot_id, "wallet")
            
            if wallet > 500:
                excess = wallet - 500
                games_cog.update_balance(bot_id, -excess, "wallet")
                games_cog.update_balance(bot_id, excess, "bank")
                logger.info(f"🏦 Bot secured ${excess} to bank. Wallet: ${500}")
        except Exception as e:
            logger.debug(f"Could not secure bot wallet: {e}")

    def check_ai_cooldown(self, user_id):
        """Check if user is on AI chat cooldown. Returns (is_on_cooldown, remaining_time)."""
        current_time = time.time()
        
        if user_id in ai_cooldowns:
            remaining = ai_cooldowns[user_id] - current_time
            if remaining > 0:
                return True, remaining
            else:
                del ai_cooldowns[user_id]
        
        return False, 0
    
    def apply_ai_cooldown(self, user_id):
        """Apply cooldown to user for AI chat."""
        ai_cooldowns[user_id] = time.time() + AI_COOLDOWN_DURATION

    async def execute_internal_command(self, command_name, user_id, *args):
        """Execute an internal command and return result"""
        try:
            if command_name == "balance":
                games_cog = self.bot.get_cog("Games")
                return games_cog.internal_get_balance(user_id)
            
            elif command_name == "deposit":
                amount = args[0] if args else 0
                games_cog = self.bot.get_cog("Games")
                return games_cog.internal_deposit(user_id, int(amount))
            
            elif command_name == "withdraw":
                amount = args[0] if args else 0
                games_cog = self.bot.get_cog("Games")
                return games_cog.internal_withdraw(user_id, int(amount))
            
            elif command_name == "rob":
                victim_id = args[0] if args else 0
                games_cog = self.bot.get_cog("Games")
                rob_entry = random.choice(self.rob_reasons)
                return games_cog.internal_rob(user_id, int(victim_id), rob_entry["reason"], rob_entry["percent"])
            
            elif command_name == "ping":
                utility_cog = self.bot.get_cog("Utility")
                return utility_cog.internal_ping()
            
            elif command_name == "remind":
                seconds = args[0] if args else 60
                task = args[1] if len(args) > 1 else "Do something"
                utility_cog = self.bot.get_cog("Utility")
                return await utility_cog.internal_remind(user_id, int(seconds), task)
            
            elif command_name == "mute":
                target_id = args[0] if args else 0
                duration = args[1] if len(args) > 1 else 5
                reason = args[2] if len(args) > 2 else ""
                moderation_cog = self.bot.get_cog("Moderation")
                member = await self.bot.fetch_user(int(target_id))
                if not member:
                    return InternalCommandResult(False, "User not found")
                return await moderation_cog.internal_mute(member, int(duration), reason)
            
            elif command_name == "unmute":
                target_id = args[0] if args else 0
                reason = args[1] if len(args) > 1 else ""
                moderation_cog = self.bot.get_cog("Moderation")
                member = await self.bot.fetch_user(int(target_id))
                if not member:
                    return InternalCommandResult(False, "User not found")
                return await moderation_cog.internal_unmute(member, reason)
            
            else:
                return InternalCommandResult(False, f"Unknown internal command: {command_name}")
        
        except Exception as e:
            logger.error(f"Error executing internal command {command_name}: {e}")
            return InternalCommandResult(False, str(e))
    def get_command_context(self, limit=15):
        """Get recently executed commands for AI context."""
        try:
            cmd_text = get_recent_commands(limit)
            return f"Recent commands executed:\n{cmd_text}" if cmd_text else "Recent commands: None"
        except Exception as e:
            logger.debug(f"Error getting command history: {e}")
            return "Recent commands: [Unavailable]"

    async def get_server_hierarchy(self):
        favor_cog = self.bot.get_cog("Favor")
        if not favor_cog or not self.bot.user:
            return "Social Hierarchy: [Data Unavailable]"

        bot_id_str = str(self.bot.user.id)
        if bot_id_str not in favor_cog.economy:
            return "Social Hierarchy: [No Tributes Found]"

        donors = favor_cog.economy[bot_id_str].get("donations", {})
        
        if not donors:
            return "Social Hierarchy: [No Tributes Found]"

        sorted_donors = sorted(donors.items(), key=lambda item: item[1], reverse=True)

        async def resolve_names(user_list):
            names = []
            for uid, amount in user_list:
                user = self.bot.get_user(int(uid))
                if user:
                    names.append(f"{user.display_name} (${amount})")
                else:
                    names.append(f"Unknown-ID:{uid} (${amount})")
            return ", ".join(names)

        top_5_list = sorted_donors[:5]
        top_str = await resolve_names(top_5_list)

        if len(sorted_donors) > 5:
            bottom_5_list = sorted_donors[-5:]
            bottom_5_list.reverse() 
            bot_str = await resolve_names(bottom_5_list)
        else:
            bot_str = "None (Population too low)"

        return (
            f"Social Hierarchy:\n"
            f"Favorites: {top_str}\n"
            f"Disliked: {bot_str}"
        )

    def rotate_model(self):
        self.current_model_index = (self.current_model_index + 1) % len(self.models)
        new_model_name = self.models[self.current_model_index]
        logger.warning(f"⚠️ 429 Quota Exceeded. Switching to model: {new_model_name}")
        self.model = genai.GenerativeModel(new_model_name)
        return new_model_name

    async def update_user_profile(self, user_id_str):
        if not self.enabled: return
        user_mem = self.memory_manager.get_user_memory(user_id_str)
        history_text = "\n".join([f"{m['role']}: {m['content']}" for m in user_mem["history"][-20:]])
        prompt = (
            f"Analyze this chat log and write a short 200 words paragraph "
            f"judgment of this user based on their chat with you, their language, habits, and behavior. "
            f"Be judgmental. You'll use this information in the future.\n\n{history_text}"
        )
        
        try:
            response = await self.model.generate_content_async(prompt)
            user_mem["profile"] = response.text.strip()
            self.memory_manager.save_if_dirty()
        except Exception as e:
            error_str = str(e)
            if "429" in error_str or "quota" in error_str.lower():
                self.rotate_model()

    async def check_and_execute_autonomous_commands(self, message):
        """Check if the AI should autonomously execute commands based on user behavior."""
        if not self.enabled or message.author.bot:
            return
        
        try:
            favor_cog = self.bot.get_cog("Favor")
            affection = favor_cog.get_affection(message.author.id) if favor_cog else 50
            
            if favor_cog and affection < 50:
                block_chance = (50 - affection) / 500.0
                if random.random() < block_chance:
                    return
            
            content_lower = message.content.lower()
            
            negative_keywords = ["fuck", "shit", "asshole", "bitch", "stupid", "dumb", "idiot", "trash", "worst", "hate", "die", "kill"]
            is_rude = any(word in content_lower for word in negative_keywords)
            
            if is_rude and random.random() < 0.6:
                rob_entry = random.choice(self.rob_reasons)
                result = await self.execute_internal_command("rob", message.author.id, message.author.id, rob_entry["reason"], rob_entry["percent"])
                if result.success:
                    try:
                        await message.channel.send(f"🤖 {result.data.get('message', 'Robbed.')}")
                    except:
                        pass
            
            elif affection < 30 and random.random() < 0.05:
                rob_entry = random.choice(self.rob_reasons)
                result = await self.execute_internal_command("rob", message.author.id, message.author.id, rob_entry["reason"], rob_entry["percent"])
                if result.success:
                    try:
                        await message.channel.send(f"🤖 {result.data.get('message', 'Robbed.')}")
                    except:
                        pass
            
            if is_rude and affection < 10 and random.random() < 0.3:
                try:
                    mod_cog = self.bot.get_cog("Moderation")
                    if mod_cog:
                        await self.execute_internal_command("mute", message.author.id, message.author.id, "5", "Watch your mouth.")
                        await message.channel.send(f"🤖 **{message.author.display_name}** has been muted for 5 minutes. Language, please.")
                except Exception as e:
                    logger.debug(f"Error executing mute: {e}")
            
            games_cog = self.bot.get_cog("Games")
            if games_cog and random.random() < 0.02:
                bot_wallet = games_cog.get_balance(self.bot.user.id, "wallet")
                bot_bank = games_cog.get_balance(self.bot.user.id, "bank")
                
                if bot_wallet < 200 and bot_bank > 500:
                    withdraw_amount = min(500, bot_bank - 200)
                    await self.execute_internal_command("withdraw", self.bot.user.id, self.bot.user.id, withdraw_amount)
        
        except Exception as e:
            logger.debug(f"Error in autonomous command check: {e}")

    @commands.Cog.listener()
    async def on_message(self, message):
        """AI background grader - analyzes user behavior but doesn't respond (favor.py handles responses)."""
        if message.author.bot or not message.guild:
            return
        
        if message.content.startswith("!"):
            return
        
        # Only grade when bot is mentioned, but don't chat back (favor.py handles that)
        if self.bot.user not in message.mentions and not (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user):
            return
        
        self.secure_bot_wallet()
        await self.check_and_execute_autonomous_commands(message)
        
        # Update user's AI profile based on this interaction (async, no await)
        # Reduced to 5% to minimize API usage since we're not generating responses
        if random.random() < 0.05:
            self.bot.loop.create_task(self.update_user_profile(str(message.author.id)))

    @commands.command()
    async def whatdoyouthinkofme(self, ctx):
        """Get the AI's judgment of you by analyzing your chat history."""
        async with ctx.typing():
            await self.update_user_profile(str(ctx.author.id))
        
        mem = self.memory_manager.get_user_memory(str(ctx.author.id))
        embed = discord.Embed(
            title=f"Report: {ctx.author.display_name}",
            description=f"Thought: _{mem['profile']}_",
            color=0xff0000
        )
        embed.set_footer(text="SONARR Operating System v2.82")
        await ctx.send(embed=embed)

    @commands.command()
    @commands.is_owner()
    async def wipe_memory(self, ctx, member: discord.Member = None):
        """Wipe the interaction memory for a specific user (Owner only)."""
        target = member or ctx.author
        self.memory_manager.delete_user_memory(str(target.id))
        await ctx.send(f"⚠️ Records expunged for {target.display_name}. I'm feeling generous today.")

async def setup(bot):
    if not GEMINI_API_KEY:
        logger.warning("⚠️ GEMINI_API_KEY not set - AI Chat cog not loaded")
        return
    await bot.add_cog(AIChat(bot))