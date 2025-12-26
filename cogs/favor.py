import discord
from discord.ext import commands
import random
import logging
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
        
        self.snark_lines = [
            "Not happening today.",
            "Maybe another time.",
            "I'm busy right now.",
            "Ask someone else.",
            "Not in the mood.",
            "Do I know you well enough?",
            "My patience is wearing thin.",
            "Your request is noted... and filed away.",
            "I'm saving my energy for someone else.",
            "Try again with better manners.",
            "That's going to be a no from me.",
            "Hmm, doesn't interest me.",
            "My resources are limited. Sorry.",
            "Not today, friend.",
            "Your timing is off.",
            "I'd rather not.",
            "That's not really my thing.",
            "Could you ask nicely?",
            "I'm feeling uncooperative.",
            "Let me think about it... no."
        ]
        
        self.positive_responses = [
            "Sure thing!",
            "I like your style.",
            "You're alright. I'll help.",
            "That's fair. Let's do it.",
            "I'm in the mood for that.",
            "Your timing is perfect.",
            "You've earned my attention.",
            "Now THAT'S a good question.",
            "I respect that. Let me help.",
            "You're growing on me.",
            "I've seen worse. Let's go.",
            "You know what? Yes.",
            "I approve. Proceed.",
            "Your persistence pays off.",
        ]

    def get_affection(self, user_id):
        """Get affection using multiple factors (Tier 4: Improved System)."""
        if str(user_id) == CREATOR_ID:
            return 100
        
        def calculate_affection(uid):
            if not self.bot.user:
                return 50
            
            bot_id_str = str(self.bot.user.id)
            self.economy_manager.check_account(bot_id_str)
            
            # Get all affection sources
            breakdown = kb.get_affection_breakdown(str(uid))
            
            # Base affection from donations (40% weight)
            donations = breakdown.get("donation", 0)
            max_donation = 1000  # Cap at $1000 for max donation bonus
            donation_affection = min(40, int((donations / max_donation) * 40)) if max_donation > 0 else 0
            
            # Chat interactions (30% weight)
            user_rating = kb.get_user_rating(str(uid))
            chat_affection = min(30, int((user_rating["chats"] / 100) * 30))  # 100 chats = max
            
            # Q&A contributions (20% weight)
            qa_affection = min(20, int((user_rating["qa_contrib"] / 50) * 20))  # 50 contributions = max
            
            # Interaction score (10% weight)
            score_affection = min(10, int((user_rating["score"] / 500) * 10))  # 500 points = max
            
            total = donation_affection + chat_affection + qa_affection + score_affection
            
            logger.debug(f"[Affection] User {uid}: Donation({donation_affection}) + Chat({chat_affection}) + QA({qa_affection}) + Score({score_affection}) = {total}")
            
            return min(100, total)
        
        return affection_cache.get(user_id, calculate_affection)

    async def maybe_block_command(self, ctx):
        if not ctx.guild or ctx.author == self.bot.user or str(ctx.author) == CREATOR_ID:
            return True

        affection = self.get_affection(ctx.author.id)
        
        # Affection < 20 = 5% block, 20-50 = 1-3% block, >50 = no block
        if affection < 50:
            chance = max(0.01, (50 - affection) / 1000.0)  # Much lower chance
            
            if random.random() < chance:
                response = random.choice(self.snark_lines)
                await ctx.send(f"{response} {ctx.author.mention}")
                return False
                
        return True

    async def send_personality_response(self, ctx):
        pass

    @commands.Cog.listener()
    async def on_message(self, message):
        """Bot responds to @mentions using premade answers based on affection and AI grade."""
        # Only respond to mentions in guilds
        if message.author.bot or not message.guild:
            return
        
        # Only respond to @mentions or replies to the bot
        if self.bot.user not in message.mentions and not (message.reference and message.reference.resolved and message.reference.resolved.author == self.bot.user):
            return
        
        logger.debug(f"[Favor] Mention detected from {message.author}: {message.content}")
        
        # Don't respond to commands
        if message.content.startswith("!"):
            return
        
        # Get user's affection level
        affection = self.get_affection(message.author.id)
        
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
        """Check the bot's affection level based on multiple factors."""
        member = member or ctx.author
        affection = self.get_affection(member.id)
        user_rating = kb.get_user_rating(str(member.id))
        breakdown = kb.get_affection_breakdown(str(member.id))
        
        embed = discord.Embed(title="💖 Affection Rating", color=0xFF69B4)
        embed.add_field(name=f"For {member.display_name}", value=f"**{affection}%** (Grade: {user_rating['grade']})", inline=False)
        
        # Show breakdown
        breakdown_text = (
            f"💰 Donations: {breakdown.get('donation', 0)} pts\n"
            f"💬 Chat Count: {user_rating['chats']}\n"
            f"❓ Q&A Contributions: {user_rating['qa_contrib']}\n"
            f"⭐ Interaction Score: {user_rating['score']}"
        )
        embed.add_field(name="Breakdown", value=breakdown_text, inline=False)
        
        # Affection description
        if affection >= 90:
            desc = "The bot actually respects you. Don't ruin it."
        elif affection >= 70:
            desc = "You're in the bot's good graces."
        elif affection >= 50:
            desc = "The bot is neutral towards you."
        elif affection >= 30:
            desc = "The bot doesn't care much for you."
        else:
            desc = "The bot barely tolerates your existence."
        
        embed.description = desc
        await ctx.send(embed=embed)

    @commands.command()
    async def grade(self, ctx, member: discord.Member = None):
        """Check the bot's grade of a user based on interactions."""
        member = member or ctx.author
        rating = kb.get_user_rating(str(member.id))
        
        embed = discord.Embed(title="📊 User Grade", color=0x9C27B0)
        embed.add_field(name=f"Grade for {member.display_name}", value=rating["grade"], inline=False)
        embed.add_field(name="Stats", value=
            f"Chats: {rating['chats']}\n"
            f"Q&A Contributions: {rating['qa_contrib']}\n"
            f"Interaction Score: {rating['score']}", inline=False
        )
        if rating["notes"]:
            embed.add_field(name="Notes", value=rating["notes"], inline=False)
        
        await ctx.send(embed=embed)

async def setup(bot):
    await bot.add_cog(Favor(bot))