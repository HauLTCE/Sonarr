import logging
from utils.database import db

logger = logging.getLogger("bot")

class EconomyManager:
    """Handles user economy using SQLite backend for 50-70% faster operations."""
    
    def __init__(self):
        """Initialize with SQLite backend (no need to load from file)."""
        self.db = db
        logger.info("[EconomyManager] Initialized with SQLite backend")

    def get_balance(self, user_id, location="wallet"):
        """Gets balance. Location can be 'wallet', 'bank', or 'total'."""
        uid = str(user_id)
        data = self.db.get_user_economy(uid)
        
        if location == "total":
            return data["wallet"] + data["bank"]
        return data.get(location, 0)

    def update_balance(self, user_id, amount, location="wallet"):
        """Updates balance instantly (SQLite write-through for critical operations)."""
        uid = str(user_id)
        
        if location == "wallet":
            self.db.update_balance(uid, wallet_delta=amount)
        elif location == "bank":
            self.db.update_balance(uid, bank_delta=amount)

    def check_account(self, user_id):
        """Ensure a user account exists (SQLite handles this automatically)."""
        uid = str(user_id)
        if not self.db.user_economy_exists(uid):
            self.db.set_user_economy(uid, 0, 0, {}, None, 0)

    def force_save(self):
        """No-op for SQLite (data is persisted immediately)."""
        logger.debug("[EconomyManager] Force save (no-op for SQLite)")

    def set_daily_status(self, user_id, last_daily, daily_streak):
        """Update daily tracking fields without changing balances."""
        uid = str(user_id)
        data = self.db.get_user_economy(uid)
        self.db.set_user_economy(
            uid,
            data["wallet"],
            data["bank"],
            data.get("donations", {}),
            last_daily,
            daily_streak
        )

    def get_donations(self, bot_id):
        """Get donations dictionary for a user."""
        uid = str(bot_id)
        data = self.db.get_user_economy(uid)
        return data.get("donations", {})

    def record_donation(self, user_id, amount, bot_id):
        """Record a donation from a user to the bot."""
        uid = str(user_id)
        bot_uid = str(bot_id)
        
        bot_data = self.db.get_user_economy(bot_uid)
        donations = bot_data.get("donations", {})
        donations[uid] = donations.get(uid, 0) + amount
        
        self.db.set_user_economy(
            bot_uid,
            bot_data["wallet"],
            bot_data["bank"],
            donations,
            bot_data["last_daily"],
            bot_data.get("daily_streak", 0)
        )
        
        # Log to affection sources so it counts toward affection
        try:
            from utils.knowledge_base import kb
            kb.add_affection_source(uid, "donation", amount)
        except Exception as e:
            logger.debug(f"Error logging donation to affection: {e}")
        
        # Invalidate affection cache since donations changed
        try:
            from utils.affection_cache import affection_cache
            affection_cache.invalidate(uid)
        except:
            pass

    def reduce_donation(self, user_id, bot_id):
        """Reduce a user's donation score (used as punishment)."""
        uid = str(user_id)
        bot_uid = str(bot_id)
        
        bot_data = self.db.get_user_economy(bot_uid)
        donations = bot_data.get("donations", {})
        donations[uid] = donations.get(uid, 0) // 2
        
        self.db.set_user_economy(
            bot_uid,
            bot_data["wallet"],
            bot_data["bank"],
            donations,
            bot_data["last_daily"],
            bot_data.get("daily_streak", 0)
        )
        
        try:
            from utils.affection_cache import affection_cache
            affection_cache.invalidate(uid)
        except:
            pass

    # Add compatibility properties for legacy code
    @property
    def economy(self):
        """Fallback for legacy code that accesses .economy dict."""
        return self.db.get_all_users_economy()
