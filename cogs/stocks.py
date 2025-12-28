import discord
from discord.ext import commands, tasks
import logging
import random
import math
from datetime import datetime, timezone, timedelta
from collections import defaultdict

from utils.economy import EconomyManager
from utils.database import db

logger = logging.getLogger("bot")

# ========== CONFIGURATION ==========
MARKET_UPDATE_HOURS = 1  # How often prices update
NEWS_CYCLE_HOURS = 4  # How often news is generated
PRICE_HISTORY_DAYS = 7  # Days of price history to keep

# === PRICE VOLATILITY ===
PRICE_CHANGE_MIN = -0.30  # Max daily crash (before multipliers)
PRICE_CHANGE_MAX = 0.40   # Max daily boom (before multipliers)
MIN_STOCK_PRICE = 10      # Floor price (prevent penny stocks)
MAX_STOCK_PRICE = 5000    # Ceiling price (prevent runaway inflation)

# === MARKET EVENTS ===
CRASH_PROBABILITY = 0.02  # 2% chance of crash each update
BOOM_PROBABILITY = 0.03   # 3% chance of boom each update

# === TRADING MECHANICS ===
TRADING_FEE_PERCENT = 0.01  # 1% fee on buy/sell (future feature)
MIN_SHARES_PER_TRADE = 1
MAX_PORTFOLIO_STOCKS = 8    # Max different stocks you can own
MAX_SHARES_PER_STOCK = 1000 # Can't hoard too much of one stock

# === NEWS IMPACT ===
NEWS_EFFECT_MIN = -0.25
NEWS_EFFECT_MAX = 0.22
MIN_NEWS_PER_CYCLE = 2      # At least 2 news items per cycle
MAX_NEWS_PER_CYCLE = 4      # At most 4 per cycle

# === MANIPULATION DETECTION ===
MANIPULATION_THRESHOLD = 2.0      # 200% above average = manipulation
RUBBER_BAND_CORRECTION = 0.30     # 30% correction when manipulation detected

# === NEWS WEIGHTING (probabilities of each category) ===
NEWS_CATEGORY_WEIGHTS = {
    "positive": 0.18,
    "negative": 0.18,
    "neutral": 0.10,
    "bullish": 0.09,
    "bearish": 0.09,
    "rumor": 0.08,
    "milestone": 0.06,
    "sector": 0.08,
    "macro": 0.06,
    "market_wide": 0.08,
}

# === SECTOR GROUPS (for cascading effects) ===
SECTOR_GROUPS = {
    "finance": ["BANK", "GAMB"],
    "tech": ["TECH", "MEME"],
    "commodities": ["FOOD", "ENRG"],
    "gaming": ["GAMB", "PKMN", "MEME"],
    "labor": ["LABOR", "TECH", "FOOD"],
}

# === SEASONAL NEWS (month-based events) ===
SEASONAL_NEWS = {
    "01": [  # January
        ("🎉 {stock} New Year earnings beat!", 0.12),
        ("📊 {stock} posts strongest January sales on record", 0.10),
    ],
    "02": [  # February
        ("💝 {stock} Valentine's promotion drives sales", 0.08),
        ("❄️ {stock} winter clearance impacts margins", -0.06),
    ],
    "03": [  # March
        ("🌸 Spring launch season boosts {stock}", 0.11),
        ("📈 {stock} Q1 guidance exceeds expectations", 0.13),
    ],
    "04": [  # April
        ("🎭 {stock} April Fools' viral campaign backfires", -0.08),
        ("🌍 Earth Day initiative improves {stock} reputation", 0.07),
    ],
    "05": [  # May
        ("🏖️ {stock} summer travel partnerships announced", 0.09),
        ("🎓 {stock} expands educational programs", 0.06),
    ],
    "06": [  # June
        ("🎉 {stock} Pride Month initiatives resonate", 0.08),
        ("📉 {stock} mid-year profit warning issued", -0.14),
    ],
    "07": [  # July
        ("🎆 {stock} Independence Day sales surge", 0.10),
        ("☀️ Summer heat impacts {stock} operations", -0.05),
    ],
    "08": [  # August
        ("👶 Back-to-school season boosts {stock}", 0.11),
        ("🎬 {stock} summer blockbuster partnership", 0.09),
    ],
    "09": [  # September
        ("🍂 Fall refresh drives {stock} demand", 0.10),
        ("📚 Education sector tailwinds benefit {stock}", 0.08),
    ],
    "10": [  # October
        ("🎃 Halloween promotions drive {stock} sales", 0.07),
        ("📉 Recession fears pressure {stock}", -0.12),
    ],
    "11": [  # November
        ("🦃 Thanksgiving demand supports {stock}", 0.09),
        ("🛍️ Black Friday preview excites {stock} investors", 0.13),
    ],
    "12": [  # December
        ("🎄 Holiday shopping season saves {stock}", 0.15),
        ("❄️ Winter weather disrupts {stock} supply chain", -0.09),
        ("🎁 Year-end bonuses fuel consumer spending for {stock}", 0.11),
    ],
}

# Stock definitions with base prices and volatility
INITIAL_STOCKS = {
    "LABOR": {
        "name": "Labor Industries",
        "base_price": 100,
        "volatility": "medium",  # Tied to !work activity
        "description": "Tracks worker productivity"
    },
    "TECH": {
        "name": "TechCorp Holdings",
        "base_price": 250,
        "volatility": "high",
        "description": "High risk, high reward tech sector"
    },
    "BANK": {
        "name": "First National Bank",
        "base_price": 500,
        "volatility": "low",
        "description": "Stable banking sector investment"
    },
    "MEME": {
        "name": "Meme Stonks Inc",
        "base_price": 50,
        "volatility": "extreme",
        "description": "🚀 To the moon! (Or the ground)"
    },
    "PKMN": {
        "name": "Pokemon Corp",
        "base_price": 150,
        "volatility": "medium",
        "description": "Pokemon hunting economy"
    },
    "GAMB": {
        "name": "Lucky Casino Group",
        "base_price": 200,
        "volatility": "high",
        "description": "Gambling industry giants"
    },
    "FOOD": {
        "name": "Global Foods Ltd",
        "base_price": 75,
        "volatility": "low",
        "description": "Essential commodities, stable growth"
    },
    "ENRG": {
        "name": "Energy Dynamics",
        "base_price": 300,
        "volatility": "medium",
        "description": "Power and utilities sector"
    }
}

# Volatility settings (daily % range)
VOLATILITY_RANGES = {
    "low": {"min": -0.03, "max": 0.05, "crash_chance": 0.005, "boom_chance": 0.01},
    "medium": {"min": -0.08, "max": 0.10, "crash_chance": 0.01, "boom_chance": 0.02},
    "high": {"min": -0.15, "max": 0.20, "crash_chance": 0.02, "boom_chance": 0.03},
    "extreme": {"min": -0.30, "max": 0.40, "crash_chance": 0.04, "boom_chance": 0.06}
}

# Market events
CRASH_MULTIPLIER = 0.7  # -30% during crash
BOOM_MULTIPLIER = 1.3  # +30% during boom
NEWS_TEMPLATES = {
    "positive": [
        ("📈 {stock} reports record quarterly earnings!", 0.15),
        ("🎉 Breaking: {stock} announces major expansion plans", 0.12),
        ("💹 Analysts upgrade {stock} to 'Strong Buy'", 0.10),
        ("🌟 {stock} wins prestigious industry award", 0.08),
        ("📊 {stock} exceeds all analyst expectations", 0.18),
        ("🚀 Institutional investors pile into {stock}", 0.14),
        ("💰 {stock} announces special dividend payout", 0.11),
        ("🏆 {stock} ranked #1 in customer satisfaction", 0.09),
        ("🔥 {stock} stock splits 2-for-1!", 0.13),
        ("✨ New product launch from {stock} gets rave reviews", 0.16),
        ("📱 {stock} secures major government contract", 0.20),
        ("🎯 {stock} crushes Q4 guidance, raises forecast", 0.17),
        ("💎 Berkshire Hathaway increases stake in {stock}", 0.22),
        ("🌐 {stock} expands to 15 new international markets", 0.12),
        ("🔬 Breakthrough innovation from {stock} labs", 0.19),
        ("🚀 {stock} becomes most mentioned stock on social media", 0.08),
    ],
    "negative": [
        ("📉 {stock} misses earnings estimates badly", -0.15),
        ("⚠️ Breaking: {stock} under regulatory investigation", -0.20),
        ("💸 {stock} announces surprise layoffs", -0.12),
        ("🔴 Analysts downgrade {stock} to 'Sell'", -0.10),
        ("😱 {stock} CEO resigns amid scandal", -0.25),
        ("📰 Leaked documents reveal {stock} troubles", -0.18),
        ("💔 {stock} loses major client partnership", -0.14),
        ("🚨 SEC filing reveals accounting issues at {stock}", -0.22),
        ("📉 {stock} faces massive product recall", -0.17),
        ("⛔ {stock} fails safety compliance audit", -0.19),
        ("🏚️ {stock} facilities caught polluting environment", -0.21),
        ("👎 Customer class action lawsuit filed against {stock}", -0.16),
        ("🔓 Data breach exposes {stock} customer records", -0.18),
        ("📊 {stock} loses top talent to competitor", -0.11),
        ("⚡ Supply chain crisis hits {stock} hard", -0.13),
        ("😤 {stock} caught in price fixing scandal", -0.24),
    ],
    "neutral": [
        ("📰 {stock} holds annual shareholder meeting", 0),
        ("🔄 {stock} restructures management team", 0.02),
        ("📋 {stock} releases routine quarterly report", 0),
        ("🤝 {stock} explores potential partnerships", 0.03),
        ("📌 {stock} announces dividend payment", 0.01),
        ("🏢 {stock} opens new headquarters", 0.02),
        ("👔 {stock} hires new CFO from rival company", 0.01),
        ("📱 {stock} updates investor relations app", 0),
        ("🌍 {stock} establishes sustainability initiative", 0.04),
        ("🎓 {stock} launches employee training program", 0),
    ],
    "bullish": [
        ("🚀 Momentum building! {stock} breaks through resistance", 0.11),
        ("📊 Technical analysis shows {stock} poised for rally", 0.09),
        ("💹 {stock} forms bullish triangle pattern", 0.10),
        ("🎯 {stock} hits new 52-week high!", 0.12),
        ("👁️ Smart money accumulating {stock} quietly", 0.13),
        ("📈 {stock} options market pricing in huge move", 0.08),
    ],
    "bearish": [
        ("📉 Warning signs emerging for {stock}", -0.11),
        ("⚠️ {stock} technical setup looks bearish", -0.09),
        ("💔 Insider selling at {stock} accelerates", -0.12),
        ("🔻 {stock} breaks key support level", -0.10),
        ("😰 Short interest surges in {stock}", -0.08),
        ("📊 {stock} volume suggests distribution phase", -0.10),
    ],
    "rumor": [
        ("💬 Rumor: {stock} considering acquisition bid", 0.06),
        ("🗣️ Industry sources: {stock} near major announcement", 0.07),
        ("👀 Speculation mounts: {stock} buyback incoming?", 0.05),
        ("🎤 Twitter buzz: {stock} trending among traders", 0.04),
        ("📡 Unconfirmed: {stock} exploring merger deal", 0.08),
    ],
    "milestone": [
        ("🎊 {stock} celebrates 10 years of growth!", 0.05),
        ("🏅 {stock} awarded ISO certification", 0.03),
        ("💯 {stock} reaches 1 million customers", 0.07),
        ("🌟 {stock} named to 'Best Companies' list", 0.06),
        ("🚀 {stock} IPO anniversary - still going strong!", 0.04),
    ],
    "sector": [
        ("🏭 Sector boom! Entire {stock} sector rallying", 0.10),
        ("🌍 Trade war impacts {stock} sector negatively", -0.12),
        ("💨 {stock} sector hit by commodity crash", -0.11),
        ("⚡ {stock} sector benefits from new regulations", 0.09),
        ("📊 {stock} sector undergoing consolidation wave", 0.06),
    ],
    "macro": [
        ("💰 Interest rate cut boosts {stock}!", 0.08),
        ("📉 Inflation fears pressure {stock}", -0.10),
        ("🌐 Strong dollar headwind for {stock}", -0.07),
        ("💵 Weak dollar benefits {stock} exports", 0.09),
        ("📊 Economic growth data favors {stock}", 0.07),
    ],
    "market_wide": [
        ("🌍 Global markets rally on economic optimism!", "boom"),
        ("💥 Market crash! Panic selling across all sectors!", "crash"),
        ("📊 Fed announces interest rate decision", "volatile"),
        ("🌐 Trade tensions ease, markets stabilize", "recovery"),
        ("⚡ Flash crash detected! Circuit breakers triggered", "crash"),
        ("🚀 Tech stocks soaring! Nasdaq up 3%!", "boom"),
        ("😱 Banking crisis fears trigger market selloff!", "crash"),
        ("📈 Positive GDP numbers boost all sectors!", "boom"),
        ("💼 Corporate earnings season exceeds expectations!", "boom"),
        ("🌪️ Geopolitical tensions create market turbulence", "volatile"),
        ("🏦 Fed hikes rates again - risk-off market", "crash"),
        ("💎 Safe haven assets surge amid uncertainty", "volatile"),
        ("🎯 Fed signals pause on rate hikes - relief rally!", "boom"),
        ("📰 Breaking: Major merger deal announced!", "boom"),
        ("⚠️ Supply chain disruptions widen market selloff", "crash"),
    ]
}

# Rubber band algorithm settings
MANIPULATION_THRESHOLD = 2.0  # 200% above average = manipulation detected
RUBBER_BAND_CORRECTION = 0.3  # 30% correction when manipulation detected


class StockView(discord.ui.View):
    """Interactive view for stock market."""
    
    def __init__(self, cog, ctx, page=0):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.page = page
    
    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        return interaction.user.id == self.ctx.author.id
    
    @discord.ui.button(label="Refresh", style=discord.ButtonStyle.primary, emoji="🔄")
    async def refresh(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = await self.cog.build_market_embed()
        await interaction.response.edit_message(embed=embed, view=self)
    
    @discord.ui.button(label="My Portfolio", style=discord.ButtonStyle.success, emoji="💼")
    async def portfolio(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = await self.cog.build_portfolio_embed(interaction.user.id)
        await interaction.response.edit_message(embed=embed, view=self)
    
    @discord.ui.button(label="News", style=discord.ButtonStyle.secondary, emoji="📰")
    async def news(self, interaction: discord.Interaction, button: discord.ui.Button):
        embed = await self.cog.build_news_embed()
        await interaction.response.edit_message(embed=embed, view=self)


class BuyStockModal(discord.ui.Modal, title="Buy Stocks"):
    """Modal for buying stocks."""
    
    ticker = discord.ui.TextInput(
        label="Stock Ticker",
        placeholder="e.g., TECH, MEME, LABOR",
        max_length=10,
        required=True
    )
    
    shares = discord.ui.TextInput(
        label="Number of Shares",
        placeholder="Enter amount or 'max'",
        max_length=20,
        required=True
    )
    
    def __init__(self, cog):
        super().__init__()
        self.cog = cog
    
    async def on_submit(self, interaction: discord.Interaction):
        ticker = self.ticker.value.upper().strip()
        shares_input = self.shares.value.strip().lower()
        
        stock = db.get_stock(ticker)
        if not stock:
            return await interaction.response.send_message(f"❌ Unknown ticker: {ticker}", ephemeral=True)
        
        price = stock["price"]
        wallet = self.cog.economy_manager.get_balance(interaction.user.id, "wallet")
        
        if shares_input == "max" or shares_input == "all":
            shares = wallet // price
        else:
            try:
                shares = int(shares_input)
            except ValueError:
                return await interaction.response.send_message("❌ Invalid number of shares", ephemeral=True)
        
        if shares <= 0:
            return await interaction.response.send_message("❌ Must buy at least 1 share", ephemeral=True)
        
        total_cost = shares * price
        if total_cost > wallet:
            return await interaction.response.send_message(
                f"❌ Not enough money. Cost: **${total_cost:,}**, You have: **${wallet:,}**",
                ephemeral=True
            )
        
        # Process purchase
        self.cog.economy_manager.update_balance(interaction.user.id, -total_cost, "wallet")
        db.buy_stock(str(interaction.user.id), ticker, shares, price)
        
        await interaction.response.send_message(
            f"✅ Bought **{shares:,}** shares of **{ticker}** at **${price:,}**/share\n"
            f"Total: **${total_cost:,}**",
            ephemeral=True
        )


class SellStockModal(discord.ui.Modal, title="Sell Stocks"):
    """Modal for selling stocks."""
    
    ticker = discord.ui.TextInput(
        label="Stock Ticker",
        placeholder="e.g., TECH, MEME, LABOR",
        max_length=10,
        required=True
    )
    
    shares = discord.ui.TextInput(
        label="Number of Shares",
        placeholder="Enter amount or 'all'",
        max_length=20,
        required=True
    )
    
    def __init__(self, cog):
        super().__init__()
        self.cog = cog
    
    async def on_submit(self, interaction: discord.Interaction):
        ticker = self.ticker.value.upper().strip()
        shares_input = self.shares.value.strip().lower()
        
        stock = db.get_stock(ticker)
        if not stock:
            return await interaction.response.send_message(f"❌ Unknown ticker: {ticker}", ephemeral=True)
        
        position = db.get_portfolio_position(str(interaction.user.id), ticker)
        if position["shares"] == 0:
            return await interaction.response.send_message(f"❌ You don't own any {ticker}", ephemeral=True)
        
        if shares_input == "all" or shares_input == "max":
            shares = position["shares"]
        else:
            try:
                shares = int(shares_input)
            except ValueError:
                return await interaction.response.send_message("❌ Invalid number of shares", ephemeral=True)
        
        if shares <= 0:
            return await interaction.response.send_message("❌ Must sell at least 1 share", ephemeral=True)
        
        if shares > position["shares"]:
            shares = position["shares"]
        
        price = stock["price"]
        total_value = shares * price
        
        # Calculate profit/loss
        avg_buy = position["avg_buy_price"]
        profit = (price - avg_buy) * shares
        profit_pct = ((price / avg_buy) - 1) * 100 if avg_buy > 0 else 0
        
        # Process sale
        db.sell_stock(str(interaction.user.id), ticker, shares)
        self.cog.economy_manager.update_balance(interaction.user.id, total_value, "wallet")
        
        profit_str = f"+${profit:,}" if profit >= 0 else f"-${abs(profit):,}"
        profit_emoji = "📈" if profit >= 0 else "📉"
        
        await interaction.response.send_message(
            f"✅ Sold **{shares:,}** shares of **{ticker}** at **${price:,}**/share\n"
            f"Total: **${total_value:,}** | {profit_emoji} P/L: **{profit_str}** ({profit_pct:+.1f}%)",
            ephemeral=True
        )


class TradeView(discord.ui.View):
    """View with buy/sell buttons."""
    
    def __init__(self, cog, ctx):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
    
    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        return interaction.user.id == self.ctx.author.id
    
    @discord.ui.button(label="Buy", style=discord.ButtonStyle.success, emoji="💰")
    async def buy(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.send_modal(BuyStockModal(self.cog))
    
    @discord.ui.button(label="Sell", style=discord.ButtonStyle.danger, emoji="💸")
    async def sell(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.send_modal(SellStockModal(self.cog))


class Stocks(commands.Cog):
    """
    Stock Market System - Invest in fake companies with dynamic prices.
    Features market news, crashes/booms, and manipulation detection.
    """
    
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
        self.initialize_stocks()
        self.market_update_task.start()
        self.news_cycle_task.start()
    
    def cog_unload(self):
        self.market_update_task.cancel()
        self.news_cycle_task.cancel()
    
    def initialize_stocks(self):
        """Initialize stocks if they don't exist."""
        existing = db.get_all_stocks()
        existing_tickers = {s["ticker"] for s in existing}
        
        for ticker, data in INITIAL_STOCKS.items():
            if ticker not in existing_tickers:
                db.upsert_stock(
                    ticker,
                    data["name"],
                    data["base_price"],
                    data["volatility"]
                )
                logger.info(f"[Stocks] Initialized {ticker} at ${data['base_price']}")
    
    def get_work_activity_deviation(self) -> float:
        """
        Calculate how much current work activity deviates from average.
        Used for LABOR stock and manipulation detection.
        """
        activity = db.get_work_activity(7)
        if not activity:
            return 0
        
        values = list(activity.values())
        if len(values) < 2:
            return 0
        
        avg = sum(values) / len(values)
        if avg == 0:
            return 0
        
        # Get today's count
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        today_count = activity.get(today, 0)
        
        deviation = today_count / avg if avg > 0 else 0
        return deviation
    
    def calculate_price_change(self, stock: dict) -> tuple[int, str]:
        """
        Calculate new price for a stock using comprehensive balancing:
        1. Rubber Band: Prices tend to return toward base price
        2. Market Cap Pressure: User holdings affect volatility
        3. Momentum: Recent price trends influence reversal chance
        4. News Sentiment: Recent news affects direction
        Returns (new_price, event_type)
        """
        ticker = stock["ticker"]
        current_price = stock["price"]
        volatility = stock["volatility"]
        vol_config = VOLATILITY_RANGES.get(volatility, VOLATILITY_RANGES["medium"])
        base_price = INITIAL_STOCKS.get(ticker, {}).get("base_price", 100)
        
        # Get base chances
        crash_chance = vol_config["crash_chance"]
        boom_chance = vol_config["boom_chance"]
        
        # === BALANCING FACTOR 1: Rubber Band to Base Price ===
        price_ratio = current_price / base_price  # 0.5 = undervalued, 2.0 = overvalued
        
        if price_ratio > 1.5:  # Overvalued - more likely to crash
            overvalue_factor = min(3.0, price_ratio - 0.5)  # Scale up crash chance
            crash_chance *= overvalue_factor
            boom_chance *= max(0.2, 2 - price_ratio)  # Reduce boom chance
        elif price_ratio < 0.7:  # Undervalued - more likely to boom
            undervalue_factor = min(3.0, 1.5 - price_ratio)  # Scale up boom chance
            boom_chance *= undervalue_factor
            crash_chance *= max(0.2, price_ratio)  # Reduce crash chance
        
        # === BALANCING FACTOR 2: Market Cap Pressure ===
        total_shares = db.get_total_shares_held(ticker)
        if total_shares > 100:  # Significant holdings
            # High demand = higher volatility, bubble risk
            demand_pressure = 1 + (total_shares / 500)  # 500 shares = 2x volatility
            if price_ratio > 1.2:  # Overvalued + high demand = bubble
                crash_chance *= demand_pressure
            elif price_ratio < 0.8:  # Undervalued + high demand = buying pressure
                boom_chance *= demand_pressure
        
        # === BALANCING FACTOR 3: Momentum (Price History) ===
        history = db.get_stock_price_history(ticker, 24)
        if len(history) >= 3:
            # Check last 3 price changes
            recent_changes = []
            for i in range(min(3, len(history) - 1)):
                if history[i + 1]["price"] > 0:
                    change = (history[i]["price"] - history[i + 1]["price"]) / history[i + 1]["price"]
                    recent_changes.append(change)
            
            if recent_changes:
                # Count consecutive gains/losses
                gains = sum(1 for c in recent_changes if c > 0.02)
                losses = sum(1 for c in recent_changes if c < -0.02)
                
                if gains >= 2:  # Winning streak - reversal more likely
                    crash_chance *= (1 + gains * 0.3)
                    boom_chance *= max(0.3, 1 - gains * 0.2)
                elif losses >= 2:  # Losing streak - recovery more likely
                    boom_chance *= (1 + losses * 0.3)
                    crash_chance *= max(0.3, 1 - losses * 0.2)
        
        # === BALANCING FACTOR 4: Recent News Sentiment ===
        recent_news = db.get_recent_news(5)
        news_for_stock = [n for n in recent_news if n.get("affected_ticker") == ticker]
        if news_for_stock:
            # Check most recent news effect
            last_effect = news_for_stock[0].get("effect", "neutral")
            if "+" in str(last_effect) or last_effect in ("boom", "recovery"):
                # Positive news recently - slight correction bias
                crash_chance *= 1.2
            elif "-" in str(last_effect) or last_effect == "crash":
                # Negative news recently - slight recovery bias
                boom_chance *= 1.2
        
        # Cap probabilities at reasonable levels
        crash_chance = min(0.25, crash_chance)
        boom_chance = min(0.25, boom_chance)
        
        event_type = "normal"
        
        # Check for special events (crash/boom)
        roll = random.random()
        if roll < crash_chance:
            # Market crash for this stock
            multiplier = CRASH_MULTIPLIER + random.uniform(-0.1, 0.1)
            new_price = int(current_price * multiplier)
            event_type = "crash"
        elif roll < crash_chance + boom_chance:
            # Market boom for this stock
            multiplier = BOOM_MULTIPLIER + random.uniform(-0.1, 0.2)
            new_price = int(current_price * multiplier)
            event_type = "boom"
        else:
            # Normal fluctuation with bias toward base price
            change_pct = random.uniform(vol_config["min"], vol_config["max"])
            
            # Add gentle drift toward base price
            if price_ratio > 1.2:
                change_pct -= (price_ratio - 1) * 0.02  # Slight downward pressure
            elif price_ratio < 0.8:
                change_pct += (1 - price_ratio) * 0.02  # Slight upward pressure
            
            # Special handling for LABOR - tied to work activity
            if ticker == "LABOR":
                deviation = self.get_work_activity_deviation()
                if deviation > MANIPULATION_THRESHOLD:
                    # Manipulation detected! Apply correction
                    change_pct = -RUBBER_BAND_CORRECTION
                    event_type = "correction"
                    logger.warning(f"[Stocks] LABOR manipulation detected (deviation: {deviation:.2f}), applying correction")
                elif deviation > 1.1:
                    # Above average activity boosts LABOR
                    change_pct += (deviation - 1) * 0.05
                elif deviation < 0.9 and deviation > 0:
                    # Below average activity hurts LABOR
                    change_pct -= (1 - deviation) * 0.03
            
            new_price = int(current_price * (1 + change_pct))
        
        # Apply price floor (can't go below 10% of base price)
        min_price = max(1, int(base_price * 0.1))
        max_price = int(base_price * 10)  # Cap at 10x base
        
        new_price = max(min_price, min(max_price, new_price))
        
        return new_price, event_type
    
    async def generate_news(self) -> dict | None:
        """Generate a random news event that affects stock prices with weighted categories."""
        all_stocks = db.get_all_stocks()
        if not all_stocks:
            return None
        
        # Check if we should do seasonal news
        now = datetime.now(timezone.utc)
        month = str(now.month).zfill(2)
        use_seasonal = random.random() < 0.25 and month in SEASONAL_NEWS
        
        # Decide news category with weighted probabilities
        roll = random.random()
        cumulative = 0
        selected_category = "positive"
        
        for category, weight in NEWS_CATEGORY_WEIGHTS.items():
            cumulative += weight
            if roll < cumulative:
                selected_category = category
                break
        
        # Market-wide event
        if selected_category == "market_wide":
            headline_data = random.choice(NEWS_TEMPLATES["market_wide"])
            headline = headline_data[0]
            effect = headline_data[1]
            
            db.add_market_news(headline, None, effect)
            
            # Apply market-wide effect to all stocks
            for stock in all_stocks:
                if effect == "boom":
                    new_price = int(stock["price"] * (1 + random.uniform(0.1, 0.25)))
                elif effect == "crash":
                    new_price = int(stock["price"] * (1 - random.uniform(0.15, 0.35)))
                elif effect == "volatile":
                    change = random.uniform(-0.15, 0.15)
                    new_price = int(stock["price"] * (1 + change))
                else:  # recovery
                    new_price = int(stock["price"] * (1 + random.uniform(0.05, 0.15)))
                
                db.update_stock_price(stock["ticker"], new_price)
            
            return {"headline": headline, "effect": effect, "type": "market_wide"}
        
        # Stock-specific news
        target_stock = random.choice(all_stocks)
        
        # Try seasonal news first
        if use_seasonal:
            seasonal_list = SEASONAL_NEWS.get(month, [])
            if seasonal_list:
                headline_template, effect_pct = random.choice(seasonal_list)
                headline = headline_template.format(stock=target_stock["name"])
                
                # Apply effect
                new_price = int(target_stock["price"] * (1 + effect_pct))
                db.update_stock_price(target_stock["ticker"], new_price)
                db.add_market_news(headline, target_stock["ticker"], f"{effect_pct:+.0%}")
                
                # Check for cascading sector effects
                if effect_pct != 0:
                    self._apply_sector_cascade(target_stock["ticker"], effect_pct)
                
                return {"headline": headline, "ticker": target_stock["ticker"], "effect": effect_pct, "seasonal": True}
        
        # Regular category news
        if selected_category in NEWS_TEMPLATES:
            template_list = NEWS_TEMPLATES[selected_category]
            headline_template, effect_pct = random.choice(template_list)
            headline = headline_template.format(stock=target_stock["name"])
            
            # Apply effect
            new_price = int(target_stock["price"] * (1 + effect_pct))
            db.update_stock_price(target_stock["ticker"], new_price)
            db.add_market_news(headline, target_stock["ticker"], f"{effect_pct:+.0%}")
            
            # Check for cascading sector effects
            if effect_pct != 0:
                self._apply_sector_cascade(target_stock["ticker"], effect_pct)
            
            return {"headline": headline, "ticker": target_stock["ticker"], "effect": effect_pct, "category": selected_category}
        
        # Fallback to neutral
        headline_template, _ = random.choice(NEWS_TEMPLATES["neutral"])
        headline = headline_template.format(stock=target_stock["name"])
        db.add_market_news(headline, target_stock["ticker"], "neutral")
        return {"headline": headline, "ticker": target_stock["ticker"], "effect": 0}
    
    def _apply_sector_cascade(self, affected_ticker: str, effect_pct: float):
        """Apply cascading effects to other stocks in the same sector."""
        # Find which sectors this stock belongs to
        affected_sectors = []
        for sector, tickers in SECTOR_GROUPS.items():
            if affected_ticker in tickers:
                affected_sectors.append(sector)
        
        # Apply reduced effect to other stocks in same sectors
        cascade_effect = effect_pct * 0.4  # 40% of original effect
        
        for sector in affected_sectors:
            for ticker in SECTOR_GROUPS[sector]:
                if ticker != affected_ticker:
                    stock = db.get_stock(ticker)
                    if stock:
                        new_price = int(stock["price"] * (1 + cascade_effect))
                        db.update_stock_price(ticker, new_price)
    
    @tasks.loop(hours=MARKET_UPDATE_HOURS)
    async def market_update_task(self):
        """Update stock prices periodically."""
        try:
            all_stocks = db.get_all_stocks()
            events = []
            
            for stock in all_stocks:
                new_price, event_type = self.calculate_price_change(stock)
                old_price = stock["price"]
                
                if new_price != old_price:
                    db.update_stock_price(stock["ticker"], new_price)
                    
                    if event_type in ("crash", "boom", "correction"):
                        change_pct = ((new_price / old_price) - 1) * 100
                        events.append({
                            "ticker": stock["ticker"],
                            "type": event_type,
                            "change": change_pct,
                            "old": old_price,
                            "new": new_price
                        })
            
            # Log significant events
            for event in events:
                if event["type"] == "crash":
                    logger.info(f"[Stocks] 💥 {event['ticker']} CRASH: ${event['old']} → ${event['new']} ({event['change']:+.1f}%)")
                elif event["type"] == "boom":
                    logger.info(f"[Stocks] 🚀 {event['ticker']} BOOM: ${event['old']} → ${event['new']} ({event['change']:+.1f}%)")
                elif event["type"] == "correction":
                    logger.info(f"[Stocks] ⚖️ {event['ticker']} CORRECTION: ${event['old']} → ${event['new']}")
            
            # Announce to market channels
            if events:
                await self.announce_market_update(events)
            
        except Exception as e:
            logger.error(f"[Stocks] Market update error: {e}")
    
    @market_update_task.before_loop
    async def before_market_update(self):
        await self.bot.wait_until_ready()
    
    @tasks.loop(hours=NEWS_CYCLE_HOURS)
    async def news_cycle_task(self):
        """Generate market news periodically."""
        try:
            news = await self.generate_news()
            if news:
                logger.info(f"[Stocks] News generated: {news.get('headline', 'N/A')}")
                await self.announce_market_news(news)
        except Exception as e:
            logger.error(f"[Stocks] News cycle error: {e}")
    
    @news_cycle_task.before_loop
    async def before_news_cycle(self):
        await self.bot.wait_until_ready()
        # Wait a bit before first news
        await discord.utils.sleep_until(datetime.now(timezone.utc) + timedelta(minutes=30))
    
    async def announce_market_update(self, events):
        """Announce significant price changes to market channels."""
        for guild in self.bot.guilds:
            guild_id = str(guild.id)
            channel_id = db.get_market_channel(guild_id)
            if not channel_id:
                continue
            
            channel = self.bot.get_channel(channel_id)
            if not channel:
                continue
            
            try:
                # Filter events for this guild (only show some to avoid spam)
                guild_events = events[:3]  # Show up to 3 events
                
                embed = discord.Embed(
                    title="📊 Market Update",
                    color=discord.Color.blue(),
                    timestamp=datetime.now(timezone.utc)
                )
                
                for event in guild_events:
                    stock = db.get_stock(event["ticker"])
                    if not stock:
                        continue
                    
                    emoji = "💥" if event["type"] == "crash" else "🚀" if event["type"] == "boom" else "⚖️"
                    event_type = "CRASH" if event["type"] == "crash" else "BOOM" if event["type"] == "boom" else "CORRECTION"
                    
                    value = f"${event['old']} → ${event['new']}\n{event['change']:+.1f}%"
                    embed.add_field(
                        name=f"{emoji} {event['ticker']} {event_type}",
                        value=value,
                        inline=True
                    )
                
                await channel.send(embed=embed)
            except Exception as e:
                logger.error(f"[Stocks] Failed to announce market update to guild {guild_id}: {e}")
    
    async def announce_market_news(self, news):
        """Announce market news to all market channels."""
        for guild in self.bot.guilds:
            guild_id = str(guild.id)
            channel_id = db.get_market_channel(guild_id)
            if not channel_id:
                continue
            
            channel = self.bot.get_channel(channel_id)
            if not channel:
                continue
            
            try:
                embed = discord.Embed(
                    title="📰 Market News",
                    description=news.get("headline", "Breaking news from the markets"),
                    color=discord.Color.gold(),
                    timestamp=datetime.now(timezone.utc)
                )
                
                if news.get("ticker"):
                    stock = db.get_stock(news["ticker"])
                    if stock:
                        embed.add_field(name="Affected Stock", value=f"**{news['ticker']}** - {stock['name']}", inline=False)
                
                if news.get("effect"):
                    effect_str = f"{news['effect']:+.1f}%" if isinstance(news['effect'], float) else str(news['effect'])
                    embed.add_field(name="Market Impact", value=effect_str, inline=True)
                
                await channel.send(embed=embed)
            except Exception as e:
                logger.error(f"[Stocks] Failed to announce news to guild {guild_id}: {e}")
    
    async def build_market_embed(self) -> discord.Embed:
        """Build the market overview embed with clean table format."""
        stocks = db.get_all_stocks()
        
        # Sort stocks by ticker for consistent ordering
        stocks = sorted(stocks, key=lambda s: s["ticker"])
        
        # Build table header
        lines = []
        lines.append("```")
        lines.append("╔════════╤══════════╤════════╤════════╤══════╗")
        lines.append("║ TICKER │   PRICE  │  +/-   │   %    │ RISK ║")
        lines.append("╠════════╪══════════╪════════╪════════╪══════╣")
        
        # Volatility text
        vol_text = {"low": "LOW ", "medium": "MED ", "high": "HIGH", "extreme": "EXTR"}
        
        for stock in stocks:
            ticker = stock["ticker"]
            price = stock["price"]
            prev_price = stock["previous_price"]
            volatility = stock["volatility"]
            
            # Calculate change
            if prev_price > 0:
                change = price - prev_price
                change_pct = ((price / prev_price) - 1) * 100
            else:
                change = 0
                change_pct = 0
            
            # Format change columns
            if change > 0:
                change_str = f"+${change}"
                pct_str = f"+{change_pct:.1f}%"
            elif change < 0:
                change_str = f"-${abs(change)}"
                pct_str = f"{change_pct:.1f}%"
            else:
                change_str = "$0"
                pct_str = "0.0%"
            
            # Format price
            price_str = f"${price:,}"
            
            # Get volatility text
            vol = vol_text.get(volatility, "??? ")
            
            # Build row
            lines.append(f"║ {ticker:<6} │ {price_str:>8} │ {change_str:>6} │ {pct_str:>6} │ {vol} ║")
        
        lines.append("╚════════╧══════════╧════════╧════════╧══════╝")
        lines.append("```")
        
        table_text = "\n".join(lines)
        
        # Calculate market summary
        total_change = sum(
            ((s["price"] / s["previous_price"]) - 1) * 100 
            for s in stocks if s["previous_price"] > 0
        )
        avg_change = total_change / len(stocks) if stocks else 0
        
        if avg_change > 2:
            market_status = "🟢 **BULL MARKET** - Prices rising!"
        elif avg_change < -2:
            market_status = "🔴 **BEAR MARKET** - Prices falling!"
        else:
            market_status = "🟡 **STABLE** - Normal trading"
        
        embed = discord.Embed(
            title="📊 STOCK MARKET",
            description=f"{market_status}\n{table_text}",
            color=0x2F3136,
            timestamp=datetime.now(timezone.utc)
        )
        
        # Add legend
        embed.add_field(
            name="📋 Risk Legend",
            value="LOW = Safe | MED = Moderate | HIGH = Risky | EXTR = Volatile",
            inline=False
        )
        
        embed.add_field(
            name="💡 Commands",
            value="`!buy_stock` `!sell_stock` `!portfolio`",
            inline=False
        )
        
        embed.set_footer(text="Prices update hourly • Use buttons below to navigate")
        return embed
    
    async def build_portfolio_embed(self, user_id: int) -> discord.Embed:
        """Build portfolio embed for a user with table format."""
        portfolio = db.get_portfolio(str(user_id))
        
        if not portfolio:
            embed = discord.Embed(
                title="💼 Your Portfolio",
                description="```\n📭 Empty portfolio!\n\nUse !buy_stock <TICKER> <amount> to invest.\nExample: !buy_stock TECH 10\n```",
                color=0x808080
            )
            return embed
        
        total_value = 0
        total_cost = 0
        
        # Build table
        lines = []
        lines.append("```")
        lines.append("╔════════╤════════╤══════════╤═════════╤═════════╗")
        lines.append("║ TICKER │ SHARES │   VALUE  │   P/$   │   P/%   ║")
        lines.append("╠════════╪════════╪══════════╪═════════╪═════════╣")
        
        for position in portfolio:
            ticker = position["ticker"]
            shares = position["shares"]
            avg_buy = position["avg_buy_price"]
            
            stock = db.get_stock(ticker)
            if not stock:
                continue
            
            current_price = stock["price"]
            position_value = shares * current_price
            position_cost = shares * avg_buy
            profit = position_value - position_cost
            profit_pct = ((current_price / avg_buy) - 1) * 100 if avg_buy > 0 else 0
            
            total_value += position_value
            total_cost += position_cost
            
            # Format P/$ and P/% separately
            if profit >= 0:
                pl_dollar = f"+${profit:,}"
                pl_percent = f"+{profit_pct:.1f}%"
            else:
                pl_dollar = f"-${abs(profit):,}"
                pl_percent = f"{profit_pct:.1f}%"
            
            lines.append(f"║ {ticker:<6} │ {shares:>6} │ ${position_value:>7,} │ {pl_dollar:>7} │ {pl_percent:>7} ║")
        
        lines.append("╠════════╧════════╧══════════╧═════════╧═════════╣")
        
        # Total row
        total_profit = total_value - total_cost
        total_pct = ((total_value / total_cost) - 1) * 100 if total_cost > 0 else 0
        
        if total_profit >= 0:
            total_pl_dollar = f"+${total_profit:,}"
            total_pl_pct = f"+{total_pct:.1f}%"
        else:
            total_pl_dollar = f"-${abs(total_profit):,}"
            total_pl_pct = f"{total_pct:.1f}%"
        
        lines.append(f"║ TOTAL  │        │ ${total_value:>7,} │ {total_pl_dollar:>7} │ {total_pl_pct:>7} ║")
        lines.append("╚════════╧════════╧══════════╧═════════╧═════════╝")
        lines.append("```")
        
        table_text = "\n".join(lines)
        
        # Set color based on profit
        if total_profit > 0:
            color = 0x00FF00  # Green
            status = "📈 **PROFIT**"
        elif total_profit < 0:
            color = 0xFF0000  # Red
            status = "📉 **LOSS**"
        else:
            color = 0xFFFF00  # Yellow
            status = "➡️ **BREAK EVEN**"
        
        embed = discord.Embed(
            title="💼 PORTFOLIO",
            description=f"{status}\n{table_text}",
            color=color,
            timestamp=datetime.now(timezone.utc)
        )
        
        embed.set_footer(text="Use !sell_stock <TICKER> <amount> to sell")
        return embed
    
    async def build_news_embed(self) -> discord.Embed:
        """Build news embed."""
        news_items = db.get_recent_news(10)
        
        embed = discord.Embed(
            title="📰 Market News",
            description="Recent headlines affecting the market",
            color=0x1DA1F2
        )
        
        if not news_items:
            embed.description = "No recent news. The market is quiet..."
            return embed
        
        for item in news_items[:5]:
            timestamp = datetime.fromtimestamp(item["timestamp"], tz=timezone.utc)
            time_str = f"<t:{int(item['timestamp'])}:R>"
            
            effect_str = ""
            if item["effect"]:
                if item["effect"] in ("boom", "crash", "volatile", "recovery"):
                    effect_str = f" [{item['effect'].upper()}]"
                elif item["effect"] != "neutral":
                    effect_str = f" [{item['effect']}]"
            
            ticker_str = f" **[{item['affected_ticker']}]**" if item["affected_ticker"] else ""
            
            embed.add_field(
                name=f"{time_str}{ticker_str}{effect_str}",
                value=item["headline"],
                inline=False
            )
        
        return embed
    
    # ========== COMMANDS ==========
    
    @commands.command(name="stocks", aliases=["market", "stonks"])
    async def stocks(self, ctx):
        """View the stock market."""
        embed = await self.build_market_embed()
        view = StockView(self, ctx)
        await ctx.send(embed=embed, view=view)
    
    @commands.command(name="buy_stock", aliases=["buystock"])
    async def buy_stock(self, ctx, ticker: str, amount: str):
        """Buy shares of a stock. Usage: !buy_stock TECH 10"""
        ticker = ticker.upper().strip()
        
        stock = db.get_stock(ticker)
        if not stock:
            available = ", ".join(INITIAL_STOCKS.keys())
            return await ctx.send(f"❌ Unknown ticker: {ticker}\nAvailable: {available}")
        
        price = stock["price"]
        wallet = self.economy_manager.get_balance(ctx.author.id, "wallet")
        
        # Handle 'all' or 'max'
        if amount.lower() in ("all", "max"):
            shares = wallet // price
        else:
            try:
                shares = int(amount)
            except ValueError:
                return await ctx.send("❌ Invalid number. Use a number or 'all'/'max'.")
        
        if shares <= 0:
            return await ctx.send("❌ Must buy at least 1 share.")
        
        total_cost = shares * price
        if total_cost > wallet:
            max_affordable = wallet // price
            return await ctx.send(
                f"❌ Not enough money!\n"
                f"Cost: **${total_cost:,}** | You have: **${wallet:,}**\n"
                f"You can afford **{max_affordable:,}** shares."
            )
        
        # Process purchase
        self.economy_manager.update_balance(ctx.author.id, -total_cost, "wallet")
        new_shares = db.buy_stock(str(ctx.author.id), ticker, shares, price)
        
        embed = discord.Embed(
            title="✅ Purchase Complete",
            description=f"Bought **{shares:,}** shares of **{ticker}**",
            color=0x00FF00
        )
        embed.add_field(name="Price/Share", value=f"${price:,}", inline=True)
        embed.add_field(name="Total Cost", value=f"${total_cost:,}", inline=True)
        embed.add_field(name="Your Position", value=f"{new_shares:,} shares", inline=True)
        
        await ctx.send(embed=embed)
    
    @commands.command(name="sell_stock", aliases=["sellstock"])
    async def sell_stock(self, ctx, ticker: str, amount: str):
        """Sell shares of a stock. Usage: !sell_stock TECH 10"""
        ticker = ticker.upper().strip()
        
        stock = db.get_stock(ticker)
        if not stock:
            return await ctx.send(f"❌ Unknown ticker: {ticker}")
        
        position = db.get_portfolio_position(str(ctx.author.id), ticker)
        if position["shares"] == 0:
            return await ctx.send(f"❌ You don't own any {ticker} shares.")
        
        # Handle 'all'
        if amount.lower() in ("all", "max"):
            shares = position["shares"]
        else:
            try:
                shares = int(amount)
            except ValueError:
                return await ctx.send("❌ Invalid number. Use a number or 'all'.")
        
        if shares <= 0:
            return await ctx.send("❌ Must sell at least 1 share.")
        
        if shares > position["shares"]:
            return await ctx.send(f"❌ You only own **{position['shares']:,}** shares of {ticker}.")
        
        price = stock["price"]
        total_value = shares * price
        
        # Calculate P/L
        avg_buy = position["avg_buy_price"]
        profit = (price - avg_buy) * shares
        profit_pct = ((price / avg_buy) - 1) * 100 if avg_buy > 0 else 0
        
        # Process sale
        db.sell_stock(str(ctx.author.id), ticker, shares)
        self.economy_manager.update_balance(ctx.author.id, total_value, "wallet")
        
        profit_emoji = "📈" if profit >= 0 else "📉"
        profit_str = f"+${profit:,}" if profit >= 0 else f"-${abs(profit):,}"
        
        embed = discord.Embed(
            title="✅ Sale Complete",
            description=f"Sold **{shares:,}** shares of **{ticker}**",
            color=0x00FF00 if profit >= 0 else 0xFF0000
        )
        embed.add_field(name="Price/Share", value=f"${price:,}", inline=True)
        embed.add_field(name="Total Value", value=f"${total_value:,}", inline=True)
        embed.add_field(name=f"{profit_emoji} Profit/Loss", value=f"{profit_str} ({profit_pct:+.1f}%)", inline=True)
        
        await ctx.send(embed=embed)
    
    @commands.command(name="portfolio", aliases=["folio", "holdings"])
    async def portfolio(self, ctx, member: discord.Member = None):
        """View your stock portfolio."""
        member = member or ctx.author
        embed = await self.build_portfolio_embed(member.id)
        embed.set_author(name=f"{member.display_name}'s Portfolio", icon_url=member.display_avatar.url)
        await ctx.send(embed=embed)
    
    @commands.command(name="stock_info", aliases=["stockinfo", "ticker"])
    async def stock_info(self, ctx, ticker: str):
        """Get detailed info about a stock."""
        ticker = ticker.upper().strip()
        
        stock = db.get_stock(ticker)
        if not stock:
            return await ctx.send(f"❌ Unknown ticker: {ticker}")
        
        stock_def = INITIAL_STOCKS.get(ticker, {})
        history = db.get_stock_history(ticker, 24)
        
        # Calculate stats
        current = stock["price"]
        base = stock_def.get("base_price", current)
        from_base_pct = ((current / base) - 1) * 100 if base > 0 else 0
        
        # Price range from history
        if history:
            prices = [h["price"] for h in history]
            high_24h = max(prices)
            low_24h = min(prices)
        else:
            high_24h = current
            low_24h = current
        
        # Volatility description
        vol_desc = {
            "low": "🟢 Low - Stable, predictable movements",
            "medium": "🟡 Medium - Moderate fluctuations",
            "high": "🟠 High - Significant price swings",
            "extreme": "🔴 Extreme - Wild, unpredictable movements"
        }
        
        embed = discord.Embed(
            title=f"📊 {ticker} - {stock['name']}",
            description=stock_def.get("description", "No description"),
            color=0x00FF00 if from_base_pct >= 0 else 0xFF0000
        )
        
        embed.add_field(name="💵 Current Price", value=f"**${current:,}**", inline=True)
        embed.add_field(name="📈 From Base", value=f"{from_base_pct:+.1f}%", inline=True)
        embed.add_field(name="📊 Volatility", value=vol_desc.get(stock["volatility"], "Unknown"), inline=False)
        embed.add_field(name="⬆️ 24h High", value=f"${high_24h:,}", inline=True)
        embed.add_field(name="⬇️ 24h Low", value=f"${low_24h:,}", inline=True)
        embed.add_field(name="🏠 Base Price", value=f"${base:,}", inline=True)
        
        await ctx.send(embed=embed)
    
    @commands.command(name="market_news", aliases=["news"])
    async def market_news(self, ctx):
        """View recent market news."""
        embed = await self.build_news_embed()
        await ctx.send(embed=embed)
    
    @commands.command(name="trade")
    async def trade(self, ctx):
        """Open the trading interface."""
        embed = await self.build_market_embed()
        view = TradeView(self, ctx)
        
        embed.set_footer(text="Click Buy or Sell to trade")
        await ctx.send(embed=embed, view=view)


async def setup(bot):
    await bot.add_cog(Stocks(bot))
