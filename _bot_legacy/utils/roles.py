import discord
import logging
from utils.database import db

logger = logging.getLogger("bot")

# Color palette for title categories
ROLE_COLORS = {
    "economy": 0xF1C40F,    # Gold
    "gem": 0x9B59B6,        # Purple
    "dungeon": 0xE74C3C,    # Red
    "gambling": 0x2ECC71,   # Green
    "prestige": 0xFFFFFF,   # White
    "milestone": 0x3498DB,  # Blue
}


async def ensure_role(guild: discord.Guild, role_name: str, category: str = "economy") -> discord.Role:
    """Find or create a cosmetic role in the guild. Caches role_id in DB."""
    guild_id = str(guild.id)
    color = ROLE_COLORS.get(category, 0x95A5A6)

    # Check DB cache first
    db.cursor.execute(
        "SELECT role_id FROM title_roles WHERE guild_id = ? AND role_name = ?",
        (guild_id, role_name)
    )
    row = db.cursor.fetchone()

    if row:
        role = guild.get_role(int(row[0]))
        if role:
            return role
        # Role was deleted from Discord, remove stale DB entry
        db.cursor.execute(
            "DELETE FROM title_roles WHERE guild_id = ? AND role_name = ?",
            (guild_id, role_name)
        )
        db.connection.commit()

    # Search by name in guild
    existing = discord.utils.get(guild.roles, name=role_name)
    if existing:
        # Cache it
        db.cursor.execute(
            "INSERT OR REPLACE INTO title_roles (guild_id, role_name, role_id, category, color) VALUES (?, ?, ?, ?, ?)",
            (guild_id, role_name, str(existing.id), category, color)
        )
        db.connection.commit()
        return existing

    # Create the role
    try:
        role = await guild.create_role(
            name=role_name,
            color=discord.Color(color),
            hoist=False,
            mentionable=False,
            reason=f"Sonarr title system: {category}"
        )
        # Cache it
        db.cursor.execute(
            "INSERT OR REPLACE INTO title_roles (guild_id, role_name, role_id, category, color) VALUES (?, ?, ?, ?, ?)",
            (guild_id, role_name, str(role.id), category, color)
        )
        db.connection.commit()
        logger.info(f"[Roles] Created role '{role_name}' in {guild.name}")
        return role
    except discord.Forbidden:
        logger.warning(f"[Roles] Missing permissions to create role '{role_name}' in {guild.name}")
        return None
    except Exception as e:
        logger.error(f"[Roles] Failed to create role '{role_name}': {e}")
        return None


def get_all_title_role_ids(guild_id: str) -> set:
    """Get all title role IDs for a guild."""
    db.cursor.execute("SELECT role_id FROM title_roles WHERE guild_id = ?", (guild_id,))
    return {int(row[0]) for row in db.cursor.fetchall()}


async def assign_title_role(member: discord.Member, role_name: str, category: str = "economy") -> bool:
    """Remove all previous title roles, then assign the new one."""
    guild = member.guild
    guild_id = str(guild.id)

    # Get the new role (create if needed)
    new_role = await ensure_role(guild, role_name, category)
    if not new_role:
        return False

    # Get all known title role IDs
    title_role_ids = get_all_title_role_ids(guild_id)

    # Remove existing title roles from the member
    roles_to_remove = [r for r in member.roles if r.id in title_role_ids and r.id != new_role.id]
    try:
        if roles_to_remove:
            await member.remove_roles(*roles_to_remove, reason="Sonarr title switch")
        if new_role not in member.roles:
            await member.add_roles(new_role, reason="Sonarr title assignment")
        return True
    except discord.Forbidden:
        logger.warning(f"[Roles] Missing permissions to assign roles in {guild.name}")
        return False
    except Exception as e:
        logger.error(f"[Roles] Failed to assign role: {e}")
        return False


async def remove_title_roles(member: discord.Member):
    """Remove all title roles from a member."""
    guild_id = str(member.guild.id)
    title_role_ids = get_all_title_role_ids(guild_id)
    roles_to_remove = [r for r in member.roles if r.id in title_role_ids]
    try:
        if roles_to_remove:
            await member.remove_roles(*roles_to_remove, reason="Sonarr title removal")
    except Exception:
        pass


# All available titles and their costs/requirements
COIN_TITLES = {
    "Coin Collector": {"cost": 500, "currency": "coins"},
    "High Roller": {"cost": 2000, "currency": "coins"},
    "Risk Taker": {"cost": 5000, "currency": "coins"},
    "Whale": {"cost": 25000, "currency": "coins"},
    "The One Percent": {"cost": 100000, "currency": "coins"},
}

GEM_TITLES = {
    "Lucky": {"cost": 5, "currency": "gems"},
    "Untouchable": {"cost": 8, "currency": "gems"},
    "Queen's Favorite": {"cost": 10, "currency": "gems"},
    "Dungeon Master": {"cost": 15, "currency": "gems"},
}

# Auto-granted titles (not purchasable)
MILESTONE_TITLES = {
    "Floor 10 Survivor": {"category": "dungeon", "requirement": "floor_10"},
    "Floor 25 Veteran": {"category": "dungeon", "requirement": "floor_25"},
    "Floor 50 Conqueror": {"category": "dungeon", "requirement": "floor_50"},
    "Degenerate Gambler": {"category": "gambling", "requirement": "games_100"},
    "House Nemesis": {"category": "gambling", "requirement": "games_1000"},
    "Prestige I": {"category": "prestige", "requirement": "prestige_1"},
    "Prestige II": {"category": "prestige", "requirement": "prestige_2"},
    "Prestige III": {"category": "prestige", "requirement": "prestige_3"},
    "Prestige IV": {"category": "prestige", "requirement": "prestige_4"},
    "Prestige V": {"category": "prestige", "requirement": "prestige_5"},
}


def get_owned_titles(user_id: int) -> list:
    """Get list of title names the user has purchased."""
    uid = str(user_id)
    db.cursor.execute("SELECT achievement_id FROM achievements WHERE user_id = ? AND achievement_id LIKE 'title_%'", (uid,))
    return [row[0].replace("title_", "") for row in db.cursor.fetchall()]
