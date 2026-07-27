"""The `!dungeon_guide` help command. Static content, kept out of the cog body."""
import discord


def build_guide_embeds():
    e1 = discord.Embed(title="🗡️ Dungeon Guide — Getting Started", color=0x3498DB)
    e1.description = (
        "The Dungeon is a text-based roguelike RPG. "
        "Explore floors, fight enemies, collect loot, and try not to die.\n\n"
        "**How to Start**\nType `!adventure` (or `!adv`) to enter the dungeon.\n\n"
        "**The Main Menu**\n"
        "⚔️ **Explore** — Move to the next room\n"
        "💊 **Rest** — Heal 30% of max HP for 50 🪙\n"
        "📊 **Stats** — View your full stats including gear bonuses\n\n"
        "**Room Types**\n```\n"
        "⚔️ Combat     (50%)  — Fight an enemy\n"
        "💰 Treasure   (20%)  — Free coins + possible loot\n"
        "⚠️ Trap       (15%)  — Take damage, might die\n"
        "🛕 Shrine     (10%)  — Free heal (15% HP)\n"
        "🕯️ Empty       (5%)  — Safe passage\n"
        "👑 Boss       (100%) — Every 10th floor\n```\n\n"
        "**Dungeon Leveling**\n"
        "Kill enemies to earn ⭐ XP. Level up for +5 Max HP and +1 random stat.\n"
        "Boss kills grant 3× XP!"
    )
    e1.set_footer(text="Page 1/4 — Getting Started")

    e2 = discord.Embed(title="⚔️ Dungeon Guide — Combat", color=0xE74C3C)
    e2.description = (
        "**Your Stats**\n```\n"
        "ATK — Determines damage dealt\n"
        "DEF — Reduces incoming damage\n"
        "SPD — Affects flee success rate\n"
        "LCK — Increases critical hit chance\n"
        "HP  — Don't let this reach 0\n```\n\n"
        "**Item Limit**\nYou can use at most **3 items per battle** "
        "(potions, scrolls, tonics combined). Spend them wisely.\n\n"
        "**Death Penalty**\n"
        "💀 Lose **5 floors** (min floor 1)\n💸 15% wallet tax\n"
        "❤️ HP fully restored\n💀 **Revival Token** can prevent floor loss!\n\n"
        "**Bosses**\nEvery 10th floor is a guaranteed boss with 2.5× HP. "
        "Bosses are the **only source of Legendary drops**."
    )
    e2.set_footer(text="Page 2/4 — Combat")
    return e1, e2


def build_guide_embeds_2():
    e3 = discord.Embed(title="🎒 Dungeon Guide — Items & Gear", color=0x9B59B6)
    e3.description = (
        "Items drop from combat wins and treasure rooms.\n"
        "Use `!inventory` to view and `!equip <name>` to equip.\n"
        "One item per slot (weapon / helmet / armor / ring / accessory). "
        "Sell unwanted gear at `!shop`.\n\n"
        "**Item Tiers**\n```\n"
        "⬜ Common     Floors 1-15\n🟩 Uncommon   Floors 5-30\n"
        "🟦 Rare       Floors 10-50\n🟪 Epic       Floors 20-80\n"
        "🟧 Legendary  Floor 35+   (boss only)\n```\n\n"
        "**★ Sample Legendaries ★**\n"
        "🗡️ **Blackened Sword** — +1 stat on kill (always vs demons)\n"
        "⏳ **Hourglass of the Archiver** — 60% dodge\n"
        "🔱 **Spear of the Red Dragon** — +60% dmg, costs 5% HP/turn\n"
        "🗡️ **Dagger and Pike** — bleed + 20% lifesteal\n"
        "👑 **Crown of the Endless** — tank accessory (+50 HP)"
    )
    e3.set_footer(text="Page 3/4 — Items & Gear")

    e4 = discord.Embed(title="🧠 Dungeon Guide — Strategy & Enemies", color=0x2ECC71)
    e4.description = (
        "**Enemy Types**\n```\n"
        "🐾 Beast   💀 Undead   👤 Humanoid\n"
        "🧪 Slime   😈 Demon    🔥 Elemental   🐉 Dragon\n```\n"
        "Demons appear Floor 21+. Elementals 51+. Dragons 81+.\n\n"
        "**Named Bosses**\n"
        "`F10` The Warden  •  `F20` Crimson Butcher\n"
        "`F50` The Nameless King  •  `F100` **The World Ender**\n\n"
        "**Tips**\n"
        "• Rest before bosses — don't enter with low HP\n"
        "• Bank your coins — death tax only hits your wallet\n"
        "• You only get 3 items per fight, so save potions for emergencies"
    )
    e4.set_footer(text="Page 4/4 — Strategy & Enemies  •  Good luck in there.")
    return e3, e4
