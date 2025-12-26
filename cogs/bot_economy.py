import discord
from discord.ext import commands, tasks
import random
import logging
from utils.economy import EconomyManager

logger = logging.getLogger("bot")


class BotEconomy(commands.Cog):
    """
    Bot Economy Cog - Manages the bot's own economy.
    """
    
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
        
        self.daily_income = 100 
        self.work_income_min = 50
        self.work_income_max = 150
        self.work_interval_minutes = 60
        self.daily_interval_hours = 24 
        
        self.bot_daily_task.start()
        self.bot_work_task.start()

    def cog_unload(self):
        self.bot_daily_task.cancel()
        self.bot_work_task.cancel()

    def get_announce_channel(self):
        if not self.bot.guilds: return None
        guild = self.bot.guilds[0]
        guild_id = str(guild.id)
        config = self.bot.server_config.get(guild_id, {})
        channel_id = config.get("announce_channel") or config.get("general_channel")
        if channel_id: return self.bot.get_channel(channel_id)
        for channel in guild.text_channels:
            if channel.permissions_for(guild.me).send_messages: return channel
        return None

    @tasks.loop(hours=24)
    async def bot_daily_task(self):
        try:
            if not self.bot.user: return
            bot_id = self.bot.user.id
            income = self.daily_income
            self.economy_manager.update_balance(bot_id, income, "wallet")
            
            self.economy_manager.update_balance(bot_id, -income, "wallet")
            self.economy_manager.update_balance(bot_id, income, "bank")
            
            logger.info(f"Bot daily task executed: +${income} (deposited to bank)")
        except Exception as e:
            logger.error(f"Error in bot daily task: {e}")

    @bot_daily_task.before_loop
    async def before_daily_task(self):
        await self.bot.wait_until_ready()

    @tasks.loop(minutes=60)
    async def bot_work_task(self):
        try:
            if not self.bot.user: return
            bot_id = self.bot.user.id
            income = random.randint(self.work_income_min, self.work_income_max)
            self.economy_manager.update_balance(bot_id, income, "wallet")
            
            self.economy_manager.update_balance(bot_id, -income, "wallet")
            self.economy_manager.update_balance(bot_id, income, "bank")
            
            logger.info(f"Bot work task executed: +${income} (deposited to bank)")
        except Exception as e:
            logger.error(f"Error in bot work task: {e}")

    @bot_work_task.before_loop
    async def before_work_task(self):
        await self.bot.wait_until_ready()

    @commands.command(name="bot_balance", hidden=True)
    @commands.is_owner()
    async def show_bot_balance(self, ctx):
        wallet = self.economy_manager.get_balance(self.bot.user.id, "wallet")
        bank = self.economy_manager.get_balance(self.bot.user.id, "bank")
        total = wallet + bank
        embed = discord.Embed(title="🤖 Bot Balance", color=0xFF69B4)
        embed.add_field(name="👛 Wallet", value=f"${wallet}", inline=True)
        embed.add_field(name="🏦 Bank", value=f"${bank}", inline=True)
        embed.add_field(name="💰 Total", value=f"${total}", inline=False)
        await ctx.send(embed=embed)

    @commands.command(name="bot_config", hidden=True)
    @commands.is_owner()
    async def config_bot_economy(self, ctx, setting: str = None, value: int = None):
        if setting is None:
            embed = discord.Embed(title="🤖 Bot Economy Config", color=0xFF69B4)
            embed.add_field(name="Daily Income", value=f"${self.daily_income}", inline=True)
            embed.add_field(name="Work Income", value=f"${self.work_income_min}-${self.work_income_max}", inline=True)
            embed.add_field(name="Work Interval", value=f"{self.work_interval_minutes} minutes", inline=True)
            embed.add_field(name="Daily Interval", value=f"{self.daily_interval_hours} hours", inline=True)
            await ctx.send(embed=embed)
            return

        if value is None:
            return await ctx.send("❌ Please provide a value.")

        setting = setting.lower()
        if setting == "daily_income":
            self.daily_income = value
            await ctx.send(f"✅ Daily income set to ${value}")
        elif setting == "work_min":
            self.work_income_min = value
            await ctx.send(f"✅ Minimum work income set to ${value}")
        elif setting == "work_max":
            self.work_income_max = value
            await ctx.send(f"✅ Maximum work income set to ${value}")
        elif setting == "work_interval":
            self.work_interval_minutes = value
            self.bot_work_task.change_interval(minutes=value)
            await ctx.send(f"✅ Work interval set to {value} minutes")
        elif setting == "daily_interval":
            self.daily_interval_hours = value
            self.bot_daily_task.change_interval(hours=value)
            await ctx.send(f"✅ Daily interval set to {value} hours")
        else:
            await ctx.send("❌ Unknown setting. Available: daily_income, work_min, work_max, work_interval, daily_interval")

    @commands.command(name="bot_give", hidden=True)
    @commands.is_owner()
    async def give_bot_money(self, ctx, amount: int, location: str = "wallet"):
        if amount <= 0: return await ctx.send("❌ Amount must be positive.")
        location = location.lower()
        if location not in ["wallet", "bank"]: return await ctx.send("❌ Location must be 'wallet' or 'bank'.")

        self.economy_manager.update_balance(self.bot.user.id, amount, location)
        await ctx.send(f"✅ Gave the bot ${amount} in {location}.")

    @commands.command(name="bot_tasks_status", hidden=True)
    @commands.is_owner()
    async def check_bot_tasks(self, ctx):
        daily_running = self.bot_daily_task.is_running()
        work_running = self.bot_work_task.is_running()
        embed = discord.Embed(title="🤖 Bot Tasks Status", color=0xFF69B4)
        embed.add_field(name="Daily Task", value="✅ Running" if daily_running else "❌ Stopped", inline=True)
        embed.add_field(name="Work Task", value="✅ Running" if work_running else "❌ Stopped", inline=True)
        await ctx.send(embed=embed)

    @commands.command(name="bot_secure", hidden=True)
    @commands.is_owner()
    async def secure_bot_funds(self, ctx):
        """Immediately secure any excess wallet funds to the bank, keeping only $500."""
        wallet = self.economy_manager.get_balance(self.bot.user.id, "wallet")
        
        if wallet <= 500:
            return await ctx.send(f"✅ Bot wallet is safe (${wallet}). No excess to secure.")
        
        excess = wallet - 500
        self.economy_manager.update_balance(self.bot.user.id, -excess, "wallet")
        self.economy_manager.update_balance(self.bot.user.id, excess, "bank")
        await ctx.send(f"🏦 Secured **${excess}** to bank. Bot wallet now: **$500**")

async def setup(bot):
    await bot.add_cog(BotEconomy(bot))