import discord
import random
import asyncio
import time
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

HEIST_STAGES = [
    {
        "name": "🔓 Infiltration",
        "description": "Breaking past the security systems...",
        "base_success": 0.70,
        "per_player_bonus": 0.03,
        "success_messages": [
            "{player} hacked the security cameras. Clean entry.",
            "{player} picked the lock in 3 seconds flat.",
            "{player} smooth-talked the guard. They're in.",
        ],
        "failure_messages": [
            "{player} tripped the alarm. They've been caught!",
            "{player} forgot the lockpick. Rookie mistake.",
            "{player} sneezed during the silent approach. Eliminated.",
        ]
    },
    {
        "name": "🗄️ Vault Breach",
        "description": "Cracking the vault door...",
        "base_success": 0.55,
        "per_player_bonus": 0.04,
        "success_messages": [
            "{player} cracked the combination. The vault swings open.",
            "{player} used thermite on the hinges. Brutal but effective.",
            "{player} found the key under the mat. Really?",
        ],
        "failure_messages": [
            "{player} entered the wrong code 3 times. Lockout!",
            "{player}'s drill broke. They're stuck.",
            "{player} opened the wrong vault. It was full of old receipts.",
        ]
    },
    {
        "name": "🏃 Escape",
        "description": "Getting out with the loot...",
        "base_success": 0.60,
        "per_player_bonus": 0.03,
        "success_messages": [
            "{player} sprinted to the getaway car. Safe!",
            "{player} vanished into the shadows. Ghost.",
            "{player} bribed the cops on the way out. Smart money.",
        ],
        "failure_messages": [
            "{player} tripped and dropped the loot. The cops are laughing.",
            "{player} ran into a dead end. Cornered!",
            "{player}'s getaway car had no gas. Ironic.",
        ]
    }
]

def calculate_vault_loot(num_players: int, total_ante: int) -> int:
    base_loot = total_ante * 1.8
    group_bonus = total_ante * (num_players * 0.15)
    variance = random.uniform(0.8, 1.3)
    return int((base_loot + group_bonus) * variance)

class HeistRecruitmentView(discord.ui.View):
    def __init__(self, cog, ctx, ante):
        super().__init__(timeout=60)
        self.cog = cog
        self.ctx = ctx
        self.ante = ante
        self.crew = [ctx.author]
        self.force_start = False
        
    def update_embed(self, embed):
        crew_list = "\n".join([f"✅ {p.display_name}" + (" (Organizer)" if p == self.ctx.author else "") for p in self.crew])
        embed.description = (
            f"Organized by {self.ctx.author.mention}\n\n"
            f"Ante: {self.ante:,} 🪙 per person\n\n"
            f"Crew ({len(self.crew)}/6):\n"
            f"{crew_list}\n\n"
            f"⏰ Starting soon..."
        )

    @discord.ui.button(label="Join Heist", style=discord.ButtonStyle.primary, emoji="🤝")
    async def join(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user in self.crew:
            await interaction.response.send_message("You're already in the crew.", ephemeral=True)
            return
            
        if len(self.crew) >= 6:
            await interaction.response.send_message("The crew is full.", ephemeral=True)
            return

        if interaction.user.id in self.cog.active_games:
            await interaction.response.send_message("You're already in a game. Finish that first.", ephemeral=True)
            return
            
        from utils.economy_helpers import get_balance
        bal = get_balance(interaction.user.id)
        if bal['wallet'] < self.ante:
            await interaction.response.send_message("You don't have enough coins in your wallet.", ephemeral=True)
            return
            
        self.crew.append(interaction.user)
        embed = interaction.message.embeds[0]
        self.update_embed(embed)
        
        if len(self.crew) == 6:
            button.disabled = True
            await interaction.response.edit_message(embed=embed, view=self)
            self.stop()
        else:
            await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Force Start", style=discord.ButtonStyle.secondary)
    async def force_start_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Only the organizer can force start.", ephemeral=True)
            return
            
        if len(self.crew) < 2:
            await interaction.response.send_message("You need at least 2 people to start a heist.", ephemeral=True)
            return
            
        self.force_start = True
        self.stop()

# Cooldowns are per-guild now (were module-global, so a heist in one guild
# blocked heists in every other guild).
server_cooldowns: dict[int, float] = {}          # guild_id -> unix time allowed again
player_cooldowns: dict[tuple[int, int], float] = {}  # (guild_id, user_id) -> unix time

async def start_heist(cog, ctx, ante: int):
    guild_id = ctx.guild.id if ctx.guild else 0
    now = time.time()
    
    if now < server_cooldowns.get(guild_id, 0):
        rem = int((server_cooldowns[guild_id] - now) // 60)
        await ctx.send(f"The cops are on high alert. Wait {rem} minutes before starting another server heist.")
        return
        
    if now < player_cooldowns.get((guild_id, ctx.author.id), 0):
        rem = int((player_cooldowns[(guild_id, ctx.author.id)] - now) // 60)
        await ctx.send(f"You're lying low. Wait {rem} minutes.")
        return
        
    embed = discord.Embed(title="🏦 HEIST — Recruitment Phase", color=0x3498DB)
    view = HeistRecruitmentView(cog, ctx, ante)
    view.update_embed(embed)
    
    msg = await ctx.send(embed=embed, view=view)
    await view.wait()
    
    if len(view.crew) < 2:
        embed.description += "\n\n❌ Not enough people joined. Heist cancelled."
        await msg.edit(embed=embed, view=None)
        return

    # Lock in the crew: charge each ante atomically (M1) and drop anyone whose
    # wallet dropped below the ante since they joined.
    crew = []
    for p in view.crew:
        if spend_wallet(p.id, ante):
            crew.append(p)
    if len(crew) < 2:
        for p in crew:  # refund anyone we already charged
            update_wallet(p.id, ante)
        embed.description += "\n\n❌ The crew couldn't cover the ante. Heist cancelled."
        await msg.edit(embed=embed, view=None)
        return

    server_cooldowns[guild_id] = time.time() + 1800  # 30 mins
    for p in crew:
        player_cooldowns[(guild_id, p.id)] = time.time() + 900  # 15 mins
        cog.active_games.add(p.id)  # mark crew busy for the duration of the run

    try:
        survivors = list(crew)
        dead_players = []
        
        total_ante = ante * len(crew)
        
        for i, stage in enumerate(HEIST_STAGES):
            embed = discord.Embed(title=f"🏦 HEIST — Stage {i+1}: {stage['name']}", color=0xF1C40F)
            results = []
            
            success_rate = stage["base_success"] + (len(crew) * stage["per_player_bonus"])
            
            next_survivors = []
            
            for p in survivors:
                if random.random() < success_rate:
                    next_survivors.append(p)
                    results.append(f"✅ {random.choice(stage['success_messages']).format(player=p.display_name)}")
                else:
                    dead_players.append((p, i+1))
                    results.append(f"❌ {random.choice(stage['failure_messages']).format(player=p.display_name)}")
                    record_gamble(p.id, ante, -ante)
                    
            survivors = next_survivors
            
            embed.description = "\n".join(results) + f"\n\nSurvivors: {len(survivors)}/{len(crew)}"
            
            if i < len(HEIST_STAGES) - 1 and len(survivors) > 0:
                embed.description += f" — Moving to Stage {i+2}..."
                
            await msg.edit(embed=embed, view=None)
            
            if len(survivors) == 0:
                await asyncio.sleep(2)
                fail_embed = discord.Embed(title="🏦 HEIST — RESULTS", color=0xE74C3C)
                fail_embed.description = "Everyone was caught. The heist failed."
                await msg.edit(embed=fail_embed)
                return
                
            await asyncio.sleep(4)
            
        vault_loot = calculate_vault_loot(len(crew), total_ante)
        split = vault_loot // len(survivors)
        
        res_embed = discord.Embed(title="🏦 HEIST — RESULTS", color=0x2ECC71)
        
        desc = f"Vault Loot: {vault_loot:,} 🪙\nSurvivors: {', '.join([p.display_name for p in survivors])}\n\n"
        
        for p, stage_died in dead_players:
            desc += f"💀 {p.display_name} — Caught in Stage {stage_died} (-{ante:,} 🪙)\n"
            
        for p in survivors:
            net = split - ante
            update_wallet(p.id, split)
            record_gamble(p.id, ante, net)
            desc += f"🏆 {p.display_name} — {split:,} 🪙 (+{net:,} net)\n"
            
        res_embed.description = desc
        await msg.edit(embed=res_embed)
    finally:
        # Always release the busy flags, even if a stage edit raised.
        for p in crew:
            cog.active_games.discard(p.id)
