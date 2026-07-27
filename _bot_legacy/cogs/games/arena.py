import discord
import random
import asyncio
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

class ArenaMatch:
    def __init__(self, cog, p1, p2, bet):
        self.cog = cog
        self.p1 = p1
        self.p2 = p2
        self.bet = bet
        self.hp = {p1.id: 100, p2.id: 100}
        self.moves = {p1.id: None, p2.id: None}
        self.round = 1
        
    def resolve_round(self):
        m1 = self.moves[self.p1.id]
        m2 = self.moves[self.p2.id]
        
        # Calculate base values
        dmg1 = 0
        dmg2 = 0
        log = []
        
        def roll_attack(): return random.randint(15, 25)
        def roll_counter(): return random.randint(8, 12)
        def roll_special():
            if random.random() < 0.40:
                return random.randint(35, 45)
            return 0
            
        d1_raw = roll_attack() if m1 == 'attack' else (roll_special() if m1 == 'special' else 0)
        d2_raw = roll_attack() if m2 == 'attack' else (roll_special() if m2 == 'special' else 0)
        
        if m1 == 'special':
            if d1_raw > 0: log.append(f"{self.p1.display_name}'s Special hits!")
            else: log.append(f"{self.p1.display_name}'s Special missed!")
        if m2 == 'special':
            if d2_raw > 0: log.append(f"{self.p2.display_name}'s Special hits!")
            else: log.append(f"{self.p2.display_name}'s Special missed!")
            
        # Modifiers
        d1_final = d1_raw
        d2_final = d2_raw
        
        if m1 == 'defend':
            d2_final = int(d2_raw * 0.4) if m2 != 'special' else d2_raw
            if m2 == 'attack':
                dmg2 += roll_counter()
                log.append(f"{self.p1.display_name} blocked and countered!")
                
        if m2 == 'defend':
            d1_final = int(d1_raw * 0.4) if m1 != 'special' else d1_raw
            if m1 == 'attack':
                dmg1 += roll_counter()
                log.append(f"{self.p2.display_name} blocked and countered!")
                
        # Interruption logic
        if m1 == 'special' and d1_raw == 0 and m2 == 'attack':
            log.append(f"{self.p2.display_name} interrupted {self.p1.display_name}'s special!")
        if m2 == 'special' and d2_raw == 0 and m1 == 'attack':
            log.append(f"{self.p1.display_name} interrupted {self.p2.display_name}'s special!")
            
        dmg1 += d2_final
        dmg2 += d1_final
        
        self.hp[self.p1.id] -= dmg1
        self.hp[self.p2.id] -= dmg2
        
        if dmg1 > 0: log.append(f"{self.p1.display_name} takes {dmg1} damage.")
        if dmg2 > 0: log.append(f"{self.p2.display_name} takes {dmg2} damage.")
        
        self.moves = {self.p1.id: None, self.p2.id: None}
        self.round += 1
        return "\n".join(log)

class ArenaView(discord.ui.View):
    def __init__(self, match):
        super().__init__(timeout=30)
        self.match = match
        
    @discord.ui.button(label="Attack", style=discord.ButtonStyle.danger, emoji="⚔️")
    async def attack(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self.handle_move(interaction, 'attack')
        
    @discord.ui.button(label="Defend", style=discord.ButtonStyle.primary, emoji="🛡️")
    async def defend(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self.handle_move(interaction, 'defend')
        
    @discord.ui.button(label="Special", style=discord.ButtonStyle.success, emoji="🎯")
    async def special(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self.handle_move(interaction, 'special')
        
    async def handle_move(self, interaction, move):
        uid = interaction.user.id
        if uid not in [self.match.p1.id, self.match.p2.id]:
            await interaction.response.send_message("Not your match.", ephemeral=True)
            return
            
        if self.match.moves[uid] is not None:
            await interaction.response.send_message("You already locked in your move.", ephemeral=True)
            return
            
        self.match.moves[uid] = move
        await interaction.response.send_message(f"Locked in: {move}", ephemeral=True)
        
        if self.match.moves[self.match.p1.id] is not None and self.match.moves[self.match.p2.id] is not None:
            self.stop()

def get_hp_bar(hp):
    hp = max(0, min(100, hp))
    filled = int((hp / 100) * 16)
    return "█" * filled + "░" * (16 - filled)

async def run_arena_match(cog, ctx, p1, p2, bet):
    cog.active_games.add(p1.id)
    cog.active_games.add(p2.id)

    # Guards the broad refund below: once the outcome is being settled, a later
    # failure (e.g. the final message edit) must not refund on top of a payout.
    settled = False
    try:
        # M1: atomic deductions. A player's balance can drop while they wait in
        # queue, so fail (and refund the other) instead of clamping to 0.
        if not spend_wallet(p1.id, bet):
            await ctx.send(f"{p1.display_name} no longer has enough coins. Match cancelled.")
            return
        if not spend_wallet(p2.id, bet):
            update_wallet(p1.id, bet)
            await ctx.send(f"{p2.display_name} no longer has enough coins. Match cancelled.")
            return
        
        match = ArenaMatch(cog, p1, p2, bet)
        
        embed = discord.Embed(title="⚔️ Arena Battle", color=0xE74C3C)
        embed.description = (
            f"Pot: {bet*2:,} 🪙\n\n"
            f"{p1.mention}: {get_hp_bar(100)} 100 HP\n"
            f"{p2.mention}: {get_hp_bar(100)} 100 HP\n\n"
            f"Round 1 — Choose your move!"
        )
        
        msg = await ctx.send(embed=embed)
        
        while match.hp[p1.id] > 0 and match.hp[p2.id] > 0:
            view = ArenaView(match)
            await msg.edit(embed=embed, view=view)
            
            await view.wait()
            
            # handle timeouts
            if match.moves[p1.id] is None: match.moves[p1.id] = 'defend'
            if match.moves[p2.id] is None: match.moves[p2.id] = 'defend'
            
            # Save moves before resolve clears them
            p1_move = match.moves[p1.id]
            p2_move = match.moves[p2.id]
            
            log = match.resolve_round()
            
            embed.description = (
                f"**Round {match.round - 1} Results:**\n"
                f"{p1.display_name} used {p1_move}!\n"
                f"{p2.display_name} used {p2_move}!\n\n"
                f"{log}\n\n"
                f"{p1.mention}: {get_hp_bar(match.hp[p1.id])} {max(0, match.hp[p1.id])} HP\n"
                f"{p2.mention}: {get_hp_bar(match.hp[p2.id])} {max(0, match.hp[p2.id])} HP\n\n"
            )
            
            if match.hp[p1.id] > 0 and match.hp[p2.id] > 0:
                embed.description += f"Round {match.round} — Choose your move!"
                
        # Game over — outcome decided, we're about to settle the pot.
        settled = True
        embed.title = "🏆 ARENA VICTORY"
        
        if match.hp[p1.id] <= 0 and match.hp[p2.id] <= 0:
            embed.description += "\n\nIt's a draw! Both players died."
            update_wallet(p1.id, bet)
            update_wallet(p2.id, bet)
        else:
            winner = p1 if match.hp[p1.id] > 0 else p2
            loser = p2 if winner == p1 else p1
            
            pot = bet * 2
            tax = int(pot * 0.10)
            payout = pot - tax
            
            update_wallet(winner.id, payout)
            record_gamble(winner.id, bet, payout - bet)
            record_gamble(loser.id, bet, -bet)
            
            embed.description += f"\n\n🏆 **{winner.display_name} WINS!**\nPayout: {payout:,} 🪙 (10% tax)"
            embed.color = 0x2ECC71
            
        await msg.edit(embed=embed, view=None)
        
    except Exception as e:
        # Only refund if we hadn't started settling the pot yet.
        if not settled:
            update_wallet(p1.id, bet)
            update_wallet(p2.id, bet)
            await ctx.send("An error occurred. Bets refunded.")
        raise e
    finally:
        cog.active_games.discard(p1.id)
        cog.active_games.discard(p2.id)

arena_queues = {}

async def start_arena(cog, ctx, bet: int):
    if bet not in arena_queues:
        arena_queues[bet] = []
        
    # check if someone is waiting
    for p in arena_queues[bet]:
        if p.id != ctx.author.id:
            arena_queues[bet].remove(p)
            await run_arena_match(cog, ctx, p, ctx.author, bet)
            return
            
    # Join queue. Mark the player busy so they can't start another game while
    # waiting (run_arena_match adds both players too; a set makes that idempotent).
    arena_queues[bet].append(ctx.author)
    cog.active_games.add(ctx.author.id)
    await ctx.send(f"{ctx.author.mention} joined the Arena queue for {bet:,} 🪙. Waiting for an opponent... (Timeout in 60s)")

    await asyncio.sleep(60)

    if ctx.author in arena_queues[bet]:
        arena_queues[bet].remove(ctx.author)
        cog.active_games.discard(ctx.author.id)
        # We could implement AI bot here, but for now just cancel
        await ctx.send(f"{ctx.author.mention}, no opponents found for {bet:,} 🪙 Arena. Queue cancelled.")
