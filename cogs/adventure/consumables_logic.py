"""Logic for `!use <consumable>`. Kept out of the cog so the command body stays
thin. Combat items share the per-battle cap enforced in views.CombatView."""
import discord
import asyncio
from utils.consumables import CONSUMABLES, get_consumables, use_consumable
from cogs.adventure.engine import get_character, update_character, award_dungeon_xp
from cogs.adventure.items import get_equipped_bonuses
from cogs.adventure.views import MAX_ITEMS_PER_BATTLE, hp_bar

# Combat-only items: require an active battle and count toward the per-battle cap.
COMBAT_ONLY = {
    "strength_tonic", "iron_skin", "speed_elixir", "antidote",
    "smoke_bomb", "damage_scroll", "shield_scroll", "lucky_charm",
}
# Items that count toward the cap when used during combat (potions + combat-only).
CAP_ITEMS = COMBAT_ONLY | {"health_potion", "greater_potion"}


def find_consumable(name: str):
    """Resolve a user-typed name to a consumable id, or None."""
    name = name.lower().strip()
    for cid, cdata in CONSUMABLES.items():
        if cdata['name'].lower() == name:
            return cid
    return None


async def handle_use(cog, ctx, item_name: str):
    """Dispatch a `!use` request. Sends its own messages."""
    cid = find_consumable(item_name)
    if not cid:
        await ctx.send("Unknown item. Check `!inventory` for your consumables.", delete_after=10)
        return
    if cid == "revival_token":
        await ctx.send("💀 Revival Tokens activate automatically on death. No need to use them manually.")
        return
    if get_consumables(ctx.author.id).get(cid, 0) <= 0:
        await ctx.send(f"You don't have any **{CONSUMABLES[cid]['name']}**. Buy one at `!shop consumables`.")
        return

    combat = cog.active_combats.get(ctx.author.id)

    # Enforce the shared per-battle cap for any capped item used mid-combat.
    if combat and cid in CAP_ITEMS and combat.items_used >= MAX_ITEMS_PER_BATTLE:
        await ctx.send(f"🧪 Item limit reached! ({MAX_ITEMS_PER_BATTLE}/{MAX_ITEMS_PER_BATTLE} used this battle)", delete_after=5)
        return
    if cid in COMBAT_ONLY and not combat:
        await ctx.send(f"⚔️ **{CONSUMABLES[cid]['name']}** can only be used during combat!", delete_after=5)
        return

    if cid in ("health_potion", "greater_potion"):
        await _use_potion(cog, ctx, cid, combat)
    elif cid == "lucky_charm":
        await _use_lucky_charm(ctx, combat)
    elif cid in COMBAT_ONLY:
        await _use_combat_effect(cog, ctx, cid, combat)
    else:
        await _use_utility(cog, ctx, cid)


async def _use_potion(cog, ctx, cid, combat):
    name = CONSUMABLES[cid]['name']
    if combat:
        char, bonuses = combat.char, combat.bonuses
    else:
        char = get_character(ctx.author.id)
        bonuses = get_equipped_bonuses(ctx.author.id)
    eff_hp = char['max_hp'] + bonuses.get('max_hp', 0)
    if char['hp'] >= eff_hp:
        await ctx.send("You're already at full HP!", delete_after=5)
        return
    use_consumable(ctx.author.id, cid)
    old = char['hp']
    char['hp'] = eff_hp if cid == "greater_potion" else min(eff_hp, char['hp'] + 50)
    update_character(ctx.author.id, hp=char['hp'])
    healed = char['hp'] - old
    extra = ""
    if combat:
        combat.items_used += 1
        combat.combat_log.append(f"🧪 **{name}** — +{healed} HP ({combat.items_used}/{MAX_ITEMS_PER_BATTLE})")
        extra = f" ({combat.items_used}/{MAX_ITEMS_PER_BATTLE} items used)"
    embed = discord.Embed(title=f"🧪 {name}", color=0x2ECC71)
    embed.description = f"{ctx.author.mention}\n{hp_bar(char['hp'], eff_hp)} {char['hp']}/{eff_hp}\nHealed **{healed} HP**{extra}"
    await ctx.send(embed=embed)


async def _use_lucky_charm(ctx, combat):
    """+10 LCK for the current fight. (Old version claimed '5 combats' but the
    bonus was never actually read — now it applies to the live battle.)"""
    use_consumable(ctx.author.id, "lucky_charm")
    combat.bonuses['luck'] = combat.bonuses.get('luck', 0) + 10
    combat.items_used += 1
    combat.combat_log.append(f"🍀 **Lucky Charm** — +10 LCK this fight! ({combat.items_used}/{MAX_ITEMS_PER_BATTLE})")
    await ctx.send("🍀 **+10 LCK** for this battle! Higher crit chance.")


async def _use_combat_effect(cog, ctx, cid, combat):
    """Combat-only items that modify the live battle. All count toward the cap."""
    use_consumable(ctx.author.id, cid)
    combat.items_used += 1
    tag = f"({combat.items_used}/{MAX_ITEMS_PER_BATTLE})"

    if cid == "strength_tonic":
        combat.bonuses['attack'] += 5
        combat.combat_log.append(f"💪 **Strength Tonic** — +5 ATK {tag}")
        await ctx.send("💪 **+5 ATK** for this combat!")
    elif cid == "iron_skin":
        combat.bonuses['defense'] += 5
        combat.combat_log.append(f"🛡️ **Iron Skin** — +5 DEF {tag}")
        await ctx.send("🛡️ **+5 DEF** for this combat!")
    elif cid == "speed_elixir":
        combat.bonuses['speed'] += 5
        combat.combat_log.append(f"💨 **Speed Elixir** — +5 SPD {tag}")
        await ctx.send("💨 **+5 SPD** for this combat!")
    elif cid == "antidote":
        # Bleed lives on enemies, not the player — treat as a small cleanse heal.
        eff_hp = combat.char['max_hp'] + combat.bonuses.get('max_hp', 0)
        old = combat.char['hp']
        combat.char['hp'] = min(eff_hp, combat.char['hp'] + 20)
        update_character(ctx.author.id, hp=combat.char['hp'])
        combat.combat_log.append(f"🩹 **Antidote** — healed {combat.char['hp'] - old} HP {tag}")
        await ctx.send(f"🩹 Cleansed! Healed **{combat.char['hp'] - old} HP**.")
    elif cid == "damage_scroll":
        combat.enemy['hp'] -= 50
        combat.combat_log.append(f"📜 **Damage Scroll** — 50 damage to enemy! {tag}")
        await ctx.send("📜 The scroll erupts! **50 damage** to enemy!")
    elif cid == "shield_scroll":
        combat.shield_active = True
        combat.combat_log.append(f"🛡️ **Shield Scroll** — next attack blocked! {tag}")
        await ctx.send("🛡️ A magic shield surrounds you! Next enemy attack blocked.")
    elif cid == "smoke_bomb":
        embed = discord.Embed(title="💣 Smoke Bomb!", color=0x95A5A6)
        embed.description = f"{ctx.author.mention} vanished in a cloud of smoke!"
        for c in combat.children: c.disabled = True
        combat._left = True
        try:
            if combat._message:
                await combat._message.edit(embed=embed, view=combat)
        except Exception:
            pass
        await asyncio.sleep(1)
        cog.active_combats.pop(ctx.author.id, None)
        combat.stop()
        await cog.show_main_menu(ctx.channel, ctx, combat.char)


async def _use_utility(cog, ctx, cid):
    """Non-combat utility items. Blocked while a fight is active where relevant."""
    if cid == "xp_tome":
        use_consumable(ctx.author.id, cid)
        char = get_character(ctx.author.id)
        level_msg = award_dungeon_xp(ctx.author.id, char, 50)
        embed = discord.Embed(title="📖 XP Tome", color=0x9B59B6)
        desc = f"{ctx.author.mention}\n**+50 Dungeon XP!**"
        if level_msg:
            desc += f"\n{level_msg}"
        embed.description = desc
        await ctx.send(embed=embed)
    elif cid == "floor_skip":
        if ctx.author.id in cog.active_combats:
            await ctx.send("Can't skip floors during combat!", delete_after=5)
            return
        use_consumable(ctx.author.id, cid)
        char = get_character(ctx.author.id)
        char['current_floor'] += 1
        if char['current_floor'] > char['deepest_floor']:
            char['deepest_floor'] = char['current_floor']
        update_character(ctx.author.id, current_floor=char['current_floor'], deepest_floor=char['deepest_floor'])
        await ctx.send(f"⏭️ Floor skipped! Now on **Floor {char['current_floor']}**.")
    elif cid == "warp_crystal":
        if ctx.author.id in cog.active_combats:
            await ctx.send("Can't warp during combat!", delete_after=5)
            return
        char = get_character(ctx.author.id)
        if char['current_floor'] >= char['deepest_floor']:
            await ctx.send("You're already at your deepest floor!", delete_after=5)
            return
        use_consumable(ctx.author.id, cid)
        char['current_floor'] = char['deepest_floor']
        update_character(ctx.author.id, current_floor=char['current_floor'])
        await ctx.send(f"🔮 Warped to **Floor {char['current_floor']}**!")
