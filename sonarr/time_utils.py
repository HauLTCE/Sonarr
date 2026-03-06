"""
Time management utilities for the Sonarr bot.

Handles sleep time, grace periods, lunch breaks, and time-based restrictions.
"""

import random
from datetime import datetime, timezone, timedelta

from sonarr.premade_answers import (
    EVENING_GRACE_RESPONSES,
    MORNING_GRACE_RESPONSES,
    LUNCH_BREAK_RESPONSES,
)


class TimeManager:
    """Manages time-based bot behavior including sleep, lunch, and grace periods."""
    
    # UTC+7 timezone (Vietnam/Bangkok)
    TIMEZONE = timezone(timedelta(hours=7))
    
    # Time boundaries
    SLEEP_START = 22  # 10 PM
    SLEEP_END = 6     # 6 AM
    LUNCH_HOUR = 12   # 12 PM
    EVENING_GRACE = 21  # 9 PM
    MORNING_GRACE = 6   # 6 AM
    
    @classmethod
    def get_local_time(cls) -> datetime:
        """Get current time in UTC+7."""
        return datetime.now(cls.TIMEZONE)
    
    @classmethod
    def get_hour(cls) -> int:
        """Get current hour in UTC+7 (0-23)."""
        return cls.get_local_time().hour
    
    @classmethod
    def get_minute(cls) -> int:
        """Get current minute (0-59)."""
        return cls.get_local_time().minute
    
    @classmethod
    def is_sleep_time(cls) -> bool:
        """Check if bot is in sleep mode (10PM - 6AM in UTC+7)."""
        hour = cls.get_hour()
        return hour >= cls.SLEEP_START or hour < cls.SLEEP_END
    
    @classmethod
    def is_lunch_break(cls) -> bool:
        """Check if bot is on lunch break (12PM - 1PM in UTC+7)."""
        return cls.get_hour() == cls.LUNCH_HOUR
    
    @classmethod
    def is_evening_grace(cls) -> bool:
        """Check if bot is in evening grace period (9:30PM - 10PM in UTC+7)."""
        hour = cls.get_hour()
        minute = cls.get_minute()
        # Evening grace: 9:30 PM to 10:00 PM
        return hour == cls.EVENING_GRACE and minute >= 30
    
    @classmethod
    def is_morning_grace(cls) -> bool:
        """Check if bot is in morning grace period (6AM - 7AM in UTC+7)."""
        return cls.get_hour() == cls.MORNING_GRACE
    
    @classmethod
    def get_grace_chance(cls) -> float:
        """
        Get the current grace trigger chance based on time.
        
        Morning (6AM - 7AM):
        - 6:00-6:30: 70% chance
        - 6:30-6:45: Gradually decrease from 70% to 10%
        - 6:45-7:00: 10% chance
        
        Evening (9:30PM - 10PM):
        - 9:30-10:00: Gradually increase from 10% to 60%
        
        Returns:
            Float between 0.0 and 1.0 representing the chance
        """
        hour = cls.get_hour()
        minute = cls.get_minute()
        
        # Morning grace period (6AM - 7AM)
        if hour == cls.MORNING_GRACE:
            if minute < 30:
                # 6:00 - 6:30: constant 70%
                return 0.70
            elif minute < 45:
                # 6:30 - 6:45: gradually decrease from 70% to 10%
                # 15 minutes to go from 0.70 to 0.10 (decrease of 0.60)
                progress = (minute - 30) / 15  # 0.0 to 1.0
                return 0.70 - (0.60 * progress)  # 0.70 -> 0.10
            else:
                # 6:45 - 7:00: constant 10%
                return 0.10
        
        # Evening grace period (9:30PM - 10PM)
        if hour == cls.EVENING_GRACE and minute >= 30:
            # 9:30 - 10:00: gradually increase from 10% to 60%
            # 30 minutes to go from 0.10 to 0.60 (increase of 0.50)
            progress = (minute - 30) / 30  # 0.0 to 1.0
            return 0.10 + (0.50 * progress)  # 0.10 -> 0.60
        
        # Not in grace period
        return 0.0
    
    @classmethod
    def should_trigger_grace(cls) -> bool:
        """
        Roll the dice to see if grace should trigger based on current time.
        
        Returns:
            True if grace should trigger, False otherwise
        """
        chance = cls.get_grace_chance()
        if chance <= 0:
            return False
        return random.random() < chance
    
    @classmethod
    def is_restricted_time(cls) -> bool:
        """Check if bot should restrict economy commands (sleep or lunch)."""
        return cls.is_sleep_time() or cls.is_lunch_break()
    
    @classmethod
    def get_minutes_until_next_period(cls) -> int:
        """Get minutes until the next time period (sleep, wake, or end of break)."""
        return 60 - cls.get_minute()
    
    @classmethod
    def get_grace_response(cls) -> str | None:
        """
        Get appropriate grace period response with dynamic time if applicable.
        
        Returns:
            A response string with {minutes_left} formatted, or None if not in grace period.
        """
        minutes_left = cls.get_minutes_until_next_period()
        
        if cls.is_evening_grace():
            response = random.choice(EVENING_GRACE_RESPONSES)
            return response.format(minutes_left=minutes_left)
        elif cls.is_morning_grace():
            return random.choice(MORNING_GRACE_RESPONSES)
        elif cls.is_lunch_break():
            response = random.choice(LUNCH_BREAK_RESPONSES)
            return response.format(minutes_left=minutes_left)
        
        return None
    
    @classmethod
    def get_time_status(cls) -> dict:
        """Get current time status for debugging/display."""
        return {
            "local_time": cls.get_local_time().strftime("%Y-%m-%d %H:%M:%S"),
            "hour": cls.get_hour(),
            "is_sleep": cls.is_sleep_time(),
            "is_lunch": cls.is_lunch_break(),
            "is_evening_grace": cls.is_evening_grace(),
            "is_morning_grace": cls.is_morning_grace(),
            "is_restricted": cls.is_restricted_time(),
        }
