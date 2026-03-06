"""
core.database — Database access layer.

Re-exports the `db` singleton from utils.database for backwards compatibility.
The actual implementation lives in utils/database.py until further refactoring.
"""

from utils.database import db

__all__ = ["db"]
