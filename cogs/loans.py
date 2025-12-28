import discord
from discord.ext import commands, tasks
import logging
import random
from datetime import datetime, timezone, timedelta

from utils.economy import EconomyManager
from utils.database import db

logger = logging.getLogger("bot")

# ========== CONFIGURATION ==========
LOAN_TERM_DAYS = 14  # Days until loan is due
GRACE_PERIOD_DAYS = 7  # Days before enforcement starts
INTEREST_COMPOUND_HOURS = 24  # How often interest compounds

# Credit Score Tiers (higher = better rates, higher limits)
CREDIT_TIERS = {
    "excellent": {"min_score": 200, "rate": 0.03, "max_multiplier": 5000},  # 3% daily
    "good": {"min_score": 100, "rate": 0.05, "max_multiplier": 3000},  # 5% daily
    "fair": {"min_score": 50, "rate": 0.10, "max_multiplier": 1500},  # 10% daily
    "poor": {"min_score": 0, "rate": 0.15, "max_multiplier": 1000},  # 15% daily
}

SHAME_ROLE_NAME = "Financial Ruin"
SHAME_DURATION_DAYS = 3
BORROW_COOLDOWN_DAYS = 7
BUYBACK_MULTIPLIER = 1.25  # 125% of repo cost to reclaim

# Bot personality responses
LOAN_APPROVAL_RESPONSES = [
    "Fine, I'll lend you **${amount}**. But if you're late, I *will* come collecting. You have **{days} days**.",
    "You want money? Here's **${amount}**. Don't make me regret this. Due in **{days} days**.",
    "I'm feeling generous today. **${amount}** at {rate}% daily. Pay it back in **{days} days** or else.",
    "Against my better judgment... **${amount}** is yours. {days} days. Don't disappoint me.",
    "Oh, you need MY help? How delicious. **${amount}**, {rate}% daily, **{days} days**. Clock's ticking.",
]

LOAN_DENIAL_RESPONSES = [
    "Your credit score is **{score}**. That's cute. Come back when you've earned some respect.",
    "Ha! You think I'd lend to someone with a **{score}** credit score? Please.",
    "I don't do charity, sweetheart. Your score of **{score}** doesn't inspire confidence.",
    "A credit score of **{score}**? I've seen better numbers in a dumpster.",
    "Sorry, but **{score}** screams 'will never pay me back.' Pass.",
]

BANKRUPTCY_RESPONSES = [
    "Oh, look who couldn't handle money. I've wiped your debt, but you're earning half wage until you learn to count. **3 days of shame** for you.",
    "Pathetic. I'm clearing **${debt}** from your record, but everyone will know you failed. **Financial Ruin** tag for 3 days.",
    "You've declared bankruptcy. How *embarrassing*. Debt gone, but so is your dignity. **3 days** of reduced earnings.",
    "I suppose even losers deserve a second chance. **${debt}** forgiven. But you'll wear your shame for **3 days**.",
    "Bankruptcy? In THIS economy? Fine. Debt cleared. Pride destroyed. **3 days** of financial humiliation.",
]

LATE_PAYMENT_RESPONSES = [
    "ROB:*wallet//10*:Where's my money? I'm taking this as a down payment.",
    "RENAME:Debtor:Pay your bills.",
    "ROB:*wallet//5*:You thought I forgot? I never forget.",
    "TIMEOUT:30m:Sit there and think about my money.",
    "RENAME:Broke:Maybe this will remind you.",
    "ROB:*wallet//8*:Interest payment. You're welcome.",
]

COLLATERAL_SEIZED_RESPONSES = [
    "Time's up. I'm taking your **{pokemon}** as collateral. Pay **${buyback}** to get it back.",
    "Tick tock. Your **{pokemon}** is mine now. Want it back? **${buyback}**.",
    "Should've paid on time. **{pokemon}** goes to the repo locker. Buyback price: **${buyback}**.",
    "I warned you. **{pokemon}** is in my vault now. **${buyback}** to reclaim.",
]


class LoanView(discord.ui.View):
    """View for loan request confirmation."""
    
    def __init__(self, cog, ctx, amount: int, rate: float, max_loan: int, credit_score: float, collateral_pokemon=None):
        super().__init__(timeout=60)
        self.cog = cog
        self.ctx = ctx
        self.amount = amount
        self.rate = rate
        self.max_loan = max_loan
        self.credit_score = credit_score
        self.collateral_pokemon = collateral_pokemon
        self.message = None
    
    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        return interaction.user.id == self.ctx.author.id
    
    @discord.ui.button(label="Accept Loan", style=discord.ButtonStyle.green, emoji="💰")
    async def accept(self, interaction: discord.Interaction, button: discord.ui.Button):
        # Process the loan
        success = await self.cog.process_loan(
            interaction.user, 
            self.amount, 
            self.rate, 
            self.collateral_pokemon
        )
        
        if success:
            response = random.choice(LOAN_APPROVAL_RESPONSES).format(
                amount=f"{self.amount:,}",
                days=LOAN_TERM_DAYS,
                rate=int(self.rate * 100)
            )
            await interaction.response.edit_message(content=response, embed=None, view=None)
        else:
            await interaction.response.edit_message(content="❌ Loan processing failed.", embed=None, view=None)
        self.stop()
    
    @discord.ui.button(label="Decline", style=discord.ButtonStyle.red, emoji="❌")
    async def decline(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.edit_message(content="Loan request cancelled. Smart move... or cowardice?", embed=None, view=None)
        self.stop()


class BankruptcyConfirmView(discord.ui.View):
    """Confirmation view for declaring bankruptcy."""
    
    def __init__(self, cog, ctx, current_debt: int):
        super().__init__(timeout=30)
        self.cog = cog
        self.ctx = ctx
        self.current_debt = current_debt
    
    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        return interaction.user.id == self.ctx.author.id
    
    @discord.ui.button(label="Yes, Declare Bankruptcy", style=discord.ButtonStyle.danger, emoji="💸")
    async def confirm(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self.cog.process_bankruptcy(interaction, self.current_debt)
        self.stop()
    
    @discord.ui.button(label="Cancel", style=discord.ButtonStyle.secondary, emoji="↩️")
    async def cancel(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.edit_message(content="Bankruptcy cancelled. Good luck paying that debt!", embed=None, view=None)
        self.stop()


class Loans(commands.Cog):
    """
    Loan Shark System - Borrow money from the bot with predatory interest rates.
    Features collateral system, bankruptcy protection, and AI-driven debt collection.
    """
    
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
        self.interest_task.start()
        self.enforcement_task.start()
    
    def cog_unload(self):
        self.interest_task.cancel()
        self.enforcement_task.cancel()
    
    def calculate_credit_score(self, user_id: str) -> float:
        """
        Calculate credit score based on:
        - Account age (0.2 per day, max 100)
        - Bank balance (1 per 50,000)
        - Level (3 per level) - requires levels.json
        - Previous bankruptcy (-50)
        """
        score = 0
        
        # Bank balance component
        economy = db.get_user_economy(user_id)
        bank_balance = economy.get("bank", 0)
        score += bank_balance / 50000
        
        # Check for previous bankruptcy
        bankruptcy = db.get_bankruptcy(user_id)
        if bankruptcy:
            score -= 50  # Penalty for past bankruptcy
        
        # Minimum score of 10
        return max(10, score)
    
    def get_credit_tier(self, score: float) -> dict:
        """Get the credit tier for a given score."""
        for tier_name, tier_data in CREDIT_TIERS.items():
            if score >= tier_data["min_score"]:
                return {"name": tier_name, **tier_data}
        return {"name": "poor", **CREDIT_TIERS["poor"]}
    
    def get_max_loan(self, score: float) -> int:
        """Calculate maximum loan amount based on credit score."""
        tier = self.get_credit_tier(score)
        return int(score * tier["max_multiplier"])
    
    async def process_loan(self, user: discord.Member, amount: int, rate: float, collateral_pokemon=None) -> bool:
        """Process and create a loan."""
        try:
            now = datetime.now(timezone.utc).timestamp()
            deadline = now + (LOAN_TERM_DAYS * 86400)
            
            # Create the loan
            db.create_loan(
                str(user.id),
                amount,
                rate,
                deadline,
                collateral_pokemon
            )
            
            # Give money to user
            self.economy_manager.update_balance(user.id, amount, "wallet")
            
            # If there's collateral, mark that pokemon as locked
            if collateral_pokemon:
                # Mark pokemon as collateral (can't be used until loan paid)
                pass  # We track this via the loan record
            
            logger.info(f"[Loans] Created loan for {user.id}: ${amount} at {rate*100}% daily, due in {LOAN_TERM_DAYS} days")
            return True
        except Exception as e:
            logger.error(f"[Loans] Failed to create loan: {e}")
            return False
    
    async def announce_bankruptcy(self, guild: discord.Guild, user: discord.User, debt: int):
        """Announce bankruptcy to the market channel."""
        if not guild:
            return
        
        guild_id = str(guild.id)
        channel_id = db.get_market_channel(guild_id)
        if not channel_id:
            return
        
        channel = self.bot.get_channel(channel_id)
        if not channel:
            return
        
        try:
            embed = discord.Embed(
                title="📉 BANKRUPTCY ALERT",
                description=f"{user.mention} has declared bankruptcy!",
                color=discord.Color.dark_red(),
                timestamp=datetime.now(timezone.utc)
            )
            embed.add_field(name="Debt Forgiven", value=f"${debt:,}", inline=True)
            embed.add_field(name="Penalty", value=f"{SHAME_DURATION_DAYS}-day shame period\n50% earnings debuff", inline=True)
            embed.set_thumbnail(url=user.display_avatar.url)
            
            await channel.send(embed=embed)
        except Exception as e:
            logger.error(f"[Loans] Failed to announce bankruptcy to guild {guild_id}: {e}")
    
    async def process_bankruptcy(self, interaction: discord.Interaction, debt: int):
        """Process bankruptcy declaration."""
        user_id = str(interaction.user.id)
        
        # Clear all debt
        db.clear_loan(user_id)
        
        # Wipe all money
        db.set_user_economy(user_id, 0, 0, {}, None, 0)
        
        # Record bankruptcy
        db.record_bankruptcy(user_id, debt, SHAME_DURATION_DAYS, BORROW_COOLDOWN_DAYS)
        
        # Apply shame debuff (50% earnings reduction)
        shame_duration_seconds = SHAME_DURATION_DAYS * 86400
        db.set_active_buff(user_id, "bankruptcy_shame", 0.5, shame_duration_seconds)
        
        # Try to add shame role
        try:
            guild = interaction.guild
            if guild:
                shame_role = discord.utils.get(guild.roles, name=SHAME_ROLE_NAME)
                if not shame_role:
                    # Create the role if it doesn't exist
                    shame_role = await guild.create_role(
                        name=SHAME_ROLE_NAME,
                        color=discord.Color.dark_red(),
                        reason="Bankruptcy shame role"
                    )
                await interaction.user.add_roles(shame_role, reason="Declared bankruptcy")
        except Exception as e:
            logger.warning(f"[Loans] Could not add shame role: {e}")
        
        # Send response
        response = random.choice(BANKRUPTCY_RESPONSES).format(debt=f"{debt:,}")
        await interaction.response.edit_message(content=response, embed=None, view=None)
        logger.info(f"[Loans] {interaction.user.id} declared bankruptcy, ${debt} forgiven")
        
        # Announce to market channel
        await self.announce_bankruptcy(interaction.guild, interaction.user, debt)
    
    @tasks.loop(hours=INTEREST_COMPOUND_HOURS)
    async def interest_task(self):
        """Apply daily interest to all active loans."""
        try:
            loans = db.get_all_active_loans()
            now = datetime.now(timezone.utc).timestamp()
            
            for loan in loans:
                # Check if enough time has passed since last interest
                last_applied = loan.get("last_interest_applied", loan["created_timestamp"])
                hours_since = (now - last_applied) / 3600
                
                if hours_since >= INTEREST_COMPOUND_HOURS:
                    # Apply interest
                    interest = int(loan["amount_owed"] * loan["interest_rate"])
                    new_amount = loan["amount_owed"] + interest
                    db.update_loan_amount(loan["user_id"], new_amount, now)
                    logger.info(f"[Loans] Applied ${interest} interest to user {loan['user_id']}, new total: ${new_amount}")
        except Exception as e:
            logger.error(f"[Loans] Interest task error: {e}")
    
    @interest_task.before_loop
    async def before_interest_task(self):
        await self.bot.wait_until_ready()
    
    @tasks.loop(hours=6)
    async def enforcement_task(self):
        """Check for overdue loans and enforce penalties."""
        try:
            loans = db.get_all_active_loans()
            now = datetime.now(timezone.utc).timestamp()
            
            for loan in loans:
                days_overdue = (now - loan["deadline_timestamp"]) / 86400
                
                if days_overdue > 0:
                    # Loan is overdue
                    if days_overdue >= GRACE_PERIOD_DAYS and loan["collateral_pokemon_id"]:
                        # Seize collateral after grace period
                        await self.seize_collateral(loan)
                    else:
                        # Mark for late payment enforcement via personality
                        if loan["late_notice_count"] < 5:
                            db.increment_late_notice(loan["user_id"])
        except Exception as e:
            logger.error(f"[Loans] Enforcement task error: {e}")
    
    @enforcement_task.before_loop
    async def before_enforcement_task(self):
        await self.bot.wait_until_ready()
    
    async def seize_collateral(self, loan: dict):
        """Seize collateral Pokemon and move to locker."""
        user_id = loan["user_id"]
        pokemon_id = loan["collateral_pokemon_id"]
        
        if not pokemon_id:
            return
        
        # Calculate buyback cost
        buyback_cost = int(loan["amount_owed"] * BUYBACK_MULTIPLIER)
        
        # Add to locker
        db.add_to_locker(user_id, pokemon_id, buyback_cost, loan["principal"])
        
        # Unequip and mark pokemon as locked
        db.cursor.execute(
            'UPDATE pokemon_owned SET is_equipped = 0 WHERE pokemon_id = ?',
            (pokemon_id,)
        )
        db.connection.commit()
        
        # Clear the loan (debt "paid" via collateral)
        db.clear_loan(user_id)
        
        logger.info(f"[Loans] Seized Pokemon {pokemon_id} from user {user_id}, buyback: ${buyback_cost}")
    
    def has_active_loan(self, user_id: str) -> bool:
        """Check if user has an active loan."""
        loan = db.get_loan(user_id)
        return loan is not None and loan.get("status") == "active"
    
    def get_late_payment_trigger(self, user_id: str) -> str | None:
        """Get a late payment trigger response if user has overdue loan."""
        loan = db.get_loan(user_id)
        if not loan or loan["status"] != "active":
            return None
        
        now = datetime.now(timezone.utc).timestamp()
        if now > loan["deadline_timestamp"]:
            # Loan is overdue
            return random.choice(LATE_PAYMENT_RESPONSES)
        return None
    
    # ========== COMMANDS ==========
    
    @commands.group(invoke_without_command=True)
    async def loan(self, ctx):
        """Loan commands. Use !loan request, !loan pay, !loan status."""
        embed = discord.Embed(
            title="🦈 Loan Shark Services",
            description="Need money? I can help... for a price.",
            color=0xFF0000
        )
        embed.add_field(name="📝 Commands", value=(
            "`!loan request <amount>` - Request a loan\n"
            "`!loan pay <amount>` - Pay back your debt\n"
            "`!loan status` - Check your current debt\n"
            "`!loan locker` - View seized collateral\n"
            "`!loan unlock <pokemon_id>` - Buy back collateral\n"
            "`!declare_bankruptcy` - Nuclear option"
        ), inline=False)
        embed.set_footer(text="Interest compounds daily. Don't be late.")
        await ctx.send(embed=embed)
    
    @loan.command(name="request")
    async def loan_request(self, ctx, amount: int):
        """Request a loan from the bot."""
        user_id = str(ctx.author.id)
        
        # Check for existing loan
        if self.has_active_loan(user_id):
            return await ctx.send("❌ You already have an active loan. Pay it off first!")
        
        # Check bankruptcy cooldown
        if not db.can_borrow(user_id):
            bankruptcy = db.get_bankruptcy(user_id)
            cooldown_end = datetime.fromtimestamp(bankruptcy["can_borrow_after"], tz=timezone.utc)
            return await ctx.send(f"❌ You declared bankruptcy recently. You can borrow again <t:{int(bankruptcy['can_borrow_after'])}:R>.")
        
        # Calculate credit score
        credit_score = self.calculate_credit_score(user_id)
        tier = self.get_credit_tier(credit_score)
        max_loan = self.get_max_loan(credit_score)
        
        # Check if amount is valid
        if amount <= 0:
            return await ctx.send("❌ Amount must be positive.")
        
        if amount < 1000:
            return await ctx.send("❌ Minimum loan is $1,000. I don't do small change.")
        
        if amount > max_loan:
            response = random.choice(LOAN_DENIAL_RESPONSES).format(score=int(credit_score))
            return await ctx.send(f"{response}\n\n📊 Your max loan: **${max_loan:,}**")
        
        # Get user's best Pokemon for collateral
        owned_pokemon = db.get_owned_pokemon(user_id)
        collateral_pokemon = None
        collateral_info = "None required"
        
        if owned_pokemon:
            # Find highest level non-fainted pokemon
            best = max(owned_pokemon, key=lambda p: p.get("level", 1))
            collateral_pokemon = best["pokemon_id"]
            pokemon_name = best.get("nickname") or best.get("species_id", "Pokemon")
            collateral_info = f"**{pokemon_name}** (Lv.{best.get('level', 1)})"
        
        # Create loan offer embed
        rate = tier["rate"]
        daily_interest = int(amount * rate)
        total_at_deadline = int(amount * (1 + rate) ** LOAN_TERM_DAYS)
        
        embed = discord.Embed(
            title="💰 Loan Offer",
            description=f"I'll lend you **${amount:,}**. Here are the terms:",
            color=0xFFD700
        )
        embed.add_field(name="📊 Credit Score", value=f"{int(credit_score)} ({tier['name'].upper()})", inline=True)
        embed.add_field(name="📈 Interest Rate", value=f"{int(rate*100)}% daily", inline=True)
        embed.add_field(name="⏰ Due Date", value=f"{LOAN_TERM_DAYS} days", inline=True)
        embed.add_field(name="💵 Daily Interest", value=f"~${daily_interest:,}/day", inline=True)
        embed.add_field(name="💸 Est. Total Due", value=f"~${total_at_deadline:,}", inline=True)
        embed.add_field(name="🔒 Collateral", value=collateral_info, inline=True)
        embed.set_footer(text="⚠️ Miss payments and I WILL collect. Accept?")
        
        view = LoanView(self, ctx, amount, rate, max_loan, credit_score, collateral_pokemon)
        view.message = await ctx.send(embed=embed, view=view)
    
    @loan.command(name="pay")
    async def loan_pay(self, ctx, amount: str):
        """Pay back your loan."""
        user_id = str(ctx.author.id)
        loan = db.get_loan(user_id)
        
        if not loan or loan["status"] != "active":
            return await ctx.send("✅ You don't have any active loans. Good for you.")
        
        # Handle 'all' amount
        wallet = self.economy_manager.get_balance(ctx.author.id, "wallet")
        
        if amount.lower() == "all":
            pay_amount = min(wallet, loan["amount_owed"])
        else:
            try:
                pay_amount = int(amount)
            except ValueError:
                return await ctx.send("❌ Invalid amount. Use a number or 'all'.")
        
        if pay_amount <= 0:
            return await ctx.send("❌ Amount must be positive.")
        
        if pay_amount > wallet:
            return await ctx.send(f"❌ You only have **${wallet:,}** in your wallet.")
        
        # Process payment
        self.economy_manager.update_balance(ctx.author.id, -pay_amount, "wallet")
        remaining = db.pay_loan(user_id, pay_amount)
        
        if remaining == 0:
            # Loan fully paid!
            embed = discord.Embed(
                title="✅ Loan Paid Off!",
                description=f"You paid **${pay_amount:,}** and cleared your debt!",
                color=0x00FF00
            )
            embed.add_field(name="Status", value="Debt Free! 🎉", inline=False)
            
            # Release collateral if any
            if loan.get("collateral_pokemon_id"):
                embed.add_field(name="🔓 Collateral Released", value="Your Pokemon is no longer at risk.", inline=False)
        else:
            embed = discord.Embed(
                title="💵 Payment Received",
                description=f"You paid **${pay_amount:,}** toward your debt.",
                color=0xFFD700
            )
            embed.add_field(name="Remaining Debt", value=f"**${remaining:,}**", inline=True)
            embed.add_field(name="Keep paying!", value="Interest still applies.", inline=True)
        
        await ctx.send(embed=embed)
    
    @loan.command(name="status")
    async def loan_status(self, ctx):
        """Check your current loan status."""
        user_id = str(ctx.author.id)
        loan = db.get_loan(user_id)
        
        if not loan:
            credit_score = self.calculate_credit_score(user_id)
            tier = self.get_credit_tier(credit_score)
            max_loan = self.get_max_loan(credit_score)
            
            embed = discord.Embed(
                title="📊 Loan Status",
                description="You have no active loans.",
                color=0x00FF00
            )
            embed.add_field(name="Credit Score", value=f"{int(credit_score)} ({tier['name'].upper()})", inline=True)
            embed.add_field(name="Max Loan", value=f"${max_loan:,}", inline=True)
            embed.add_field(name="Interest Rate", value=f"{int(tier['rate']*100)}% daily", inline=True)
            return await ctx.send(embed=embed)
        
        now = datetime.now(timezone.utc).timestamp()
        deadline = loan["deadline_timestamp"]
        days_remaining = (deadline - now) / 86400
        
        if days_remaining < 0:
            status_text = f"⚠️ **OVERDUE** by {abs(int(days_remaining))} days!"
            color = 0xFF0000
        elif days_remaining < 3:
            status_text = f"⏰ Due in **{days_remaining:.1f}** days"
            color = 0xFFA500
        else:
            status_text = f"✅ Due in **{days_remaining:.1f}** days"
            color = 0x00FF00
        
        embed = discord.Embed(
            title="🦈 Your Loan Status",
            color=color
        )
        embed.add_field(name="💵 Original Loan", value=f"${loan['principal']:,}", inline=True)
        embed.add_field(name="💸 Current Debt", value=f"${loan['amount_owed']:,}", inline=True)
        embed.add_field(name="📈 Interest Rate", value=f"{int(loan['interest_rate']*100)}% daily", inline=True)
        embed.add_field(name="⏰ Status", value=status_text, inline=False)
        
        if loan.get("collateral_pokemon_id"):
            embed.add_field(name="🔒 Collateral", value=f"Pokemon ID: `{loan['collateral_pokemon_id']}`", inline=False)
        
        embed.set_footer(text="Use !loan pay <amount> to reduce debt")
        await ctx.send(embed=embed)
    
    @loan.command(name="locker")
    async def loan_locker(self, ctx):
        """View your seized collateral."""
        user_id = str(ctx.author.id)
        items = db.get_locker_items(user_id)
        
        if not items:
            return await ctx.send("✅ Your collateral locker is empty. Nothing has been seized.")
        
        embed = discord.Embed(
            title="🔒 Collateral Locker",
            description="These items were seized due to missed loan payments.",
            color=0xFF0000
        )
        
        for item in items:
            # Try to get Pokemon info
            pokemon_info = db.get_pokemon_by_id(item["pokemon_id"]) if hasattr(db, 'get_pokemon_by_id') else None
            if pokemon_info:
                name = pokemon_info.get("nickname") or pokemon_info.get("species_id", "Pokemon")
            else:
                name = f"Pokemon `{item['pokemon_id'][:8]}...`"
            
            embed.add_field(
                name=name,
                value=f"Buyback: **${item['buyback_cost']:,}**\nOriginal Loan: ${item['original_loan_amount']:,}",
                inline=True
            )
        
        embed.set_footer(text="Use !loan unlock <pokemon_id> to buy back")
        await ctx.send(embed=embed)
    
    @loan.command(name="unlock")
    async def loan_unlock(self, ctx, pokemon_id: str):
        """Buy back seized collateral from the locker."""
        user_id = str(ctx.author.id)
        items = db.get_locker_items(user_id)
        
        # Find the item
        target_item = None
        for item in items:
            if item["pokemon_id"] == pokemon_id or item["pokemon_id"].startswith(pokemon_id):
                target_item = item
                break
        
        if not target_item:
            return await ctx.send("❌ That Pokemon isn't in your locker.")
        
        buyback_cost = target_item["buyback_cost"]
        wallet = self.economy_manager.get_balance(ctx.author.id, "wallet")
        
        if wallet < buyback_cost:
            return await ctx.send(f"❌ Buyback costs **${buyback_cost:,}**. You have **${wallet:,}**.")
        
        # Process buyback
        self.economy_manager.update_balance(ctx.author.id, -buyback_cost, "wallet")
        db.remove_from_locker(user_id, target_item["pokemon_id"])
        
        await ctx.send(f"✅ Paid **${buyback_cost:,}** and reclaimed your Pokemon!")
    
    @commands.command(name="declare_bankruptcy")
    async def declare_bankruptcy(self, ctx):
        """Declare bankruptcy to wipe all debt (and all money)."""
        user_id = str(ctx.author.id)
        loan = db.get_loan(user_id)
        
        if not loan or loan["status"] != "active":
            return await ctx.send("You don't have any debt to declare bankruptcy on. Lucky you.")
        
        current_debt = loan["amount_owed"]
        economy = db.get_user_economy(user_id)
        total_assets = economy["wallet"] + economy["bank"]
        
        embed = discord.Embed(
            title="⚠️ DECLARE BANKRUPTCY?",
            description="This is a **nuclear option**. Are you sure?",
            color=0xFF0000
        )
        embed.add_field(name="Current Debt", value=f"${current_debt:,}", inline=True)
        embed.add_field(name="Your Assets", value=f"${total_assets:,}", inline=True)
        embed.add_field(name="❌ What You LOSE", value=(
            "• All money (wallet + bank)\n"
            "• Your dignity\n"
            "• Ability to borrow for 7 days"
        ), inline=False)
        embed.add_field(name="✅ What You GET", value=(
            "• Debt cleared to $0\n"
            f"• '{SHAME_ROLE_NAME}' role for {SHAME_DURATION_DAYS} days\n"
            "• 50% reduced earnings during shame period"
        ), inline=False)
        embed.set_footer(text="Are you absolutely sure?")
        
        view = BankruptcyConfirmView(self, ctx, current_debt)
        await ctx.send(embed=embed, view=view)
    
    @commands.command(name="credit_score")
    async def credit_score(self, ctx, member: discord.Member = None):
        """Check credit score."""
        member = member or ctx.author
        user_id = str(member.id)
        
        score = self.calculate_credit_score(user_id)
        tier = self.get_credit_tier(score)
        max_loan = self.get_max_loan(score)
        
        # Color based on tier
        colors = {"excellent": 0x00FF00, "good": 0x90EE90, "fair": 0xFFA500, "poor": 0xFF0000}
        
        embed = discord.Embed(
            title=f"📊 Credit Score: {member.display_name}",
            color=colors.get(tier["name"], 0xFFFFFF)
        )
        embed.add_field(name="Score", value=f"**{int(score)}**", inline=True)
        embed.add_field(name="Rating", value=tier["name"].upper(), inline=True)
        embed.add_field(name="Max Loan", value=f"${max_loan:,}", inline=True)
        embed.add_field(name="Interest Rate", value=f"{int(tier['rate']*100)}% daily", inline=True)
        
        # Bankruptcy history
        bankruptcy = db.get_bankruptcy(user_id)
        if bankruptcy:
            embed.add_field(name="⚠️ Bankruptcy", value="Previous bankruptcy on record (-50 penalty)", inline=False)
        
        await ctx.send(embed=embed)


async def setup(bot):
    await bot.add_cog(Loans(bot))
