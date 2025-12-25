import sqlite3
import logging
from pathlib import Path
from datetime import datetime, timezone

logger = logging.getLogger("bot")

# Add to existing database.py functionality
def add_knowledge_base_tables(connection, cursor):
    """Add Q&A knowledge base and user rating tables to database."""
    
    # Q&A Knowledge Base: question -> answer with metadata
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS qa_knowledge_base (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            question TEXT UNIQUE NOT NULL,
            question_lower TEXT NOT NULL,
            answer TEXT NOT NULL,
            created_by TEXT,
            created_at REAL NOT NULL,
            approved INTEGER DEFAULT 0,
            usage_count INTEGER DEFAULT 0,
            last_used REAL
        )
    ''')
    
    # User Ratings/Grades: Store AI's assessment of users
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS user_ratings (
            user_id TEXT PRIMARY KEY,
            grade TEXT DEFAULT 'F',
            interaction_score INTEGER DEFAULT 0,
            chat_count INTEGER DEFAULT 0,
            qa_contributions INTEGER DEFAULT 0,
            last_interaction REAL,
            notes TEXT DEFAULT ''
        )
    ''')
    
    # Affection Breakdown: Track affection sources
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS affection_sources (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id TEXT NOT NULL,
            source TEXT NOT NULL,
            value INTEGER NOT NULL,
            timestamp REAL NOT NULL
        )
    ''')
    
    # Create indexes
    cursor.execute('CREATE INDEX IF NOT EXISTS idx_qa_lower ON qa_knowledge_base(question_lower)')
    cursor.execute('CREATE INDEX IF NOT EXISTS idx_affection_user ON affection_sources(user_id)')
    
    connection.commit()
    logger.info("[Database] Knowledge base tables created")

class KnowledgeBase:
    """Manage Q&A knowledge base and user ratings."""
    
    def __init__(self, db_path="bot_data.db"):
        self.connection = sqlite3.connect(str(db_path), check_same_thread=False)
        self.cursor = self.connection.cursor()
        add_knowledge_base_tables(self.connection, self.cursor)
    
    # ============== Q&A METHODS ==============
    
    def add_qa(self, question: str, answer: str, created_by: str = None, approved: bool = False):
        """Add Q&A entry."""
        try:
            self.cursor.execute('''
                INSERT OR REPLACE INTO qa_knowledge_base 
                (question, question_lower, answer, created_by, created_at, approved)
                VALUES (?, ?, ?, ?, ?, ?)
            ''', (question, question.lower(), answer, created_by, datetime.now(timezone.utc).timestamp(), int(approved)))
            self.connection.commit()
            logger.debug(f"[KB] Added QA: {question}")
            return True
        except Exception as e:
            logger.error(f"[KB] Error adding QA: {e}")
            return False
    
    def find_qa(self, question: str):
        """Find Q&A by question (fuzzy match)."""
        self.cursor.execute('''
            SELECT answer, usage_count FROM qa_knowledge_base
            WHERE question_lower LIKE ? AND approved = 1
            LIMIT 1
        ''', (f"%{question.lower()}%",))
        
        row = self.cursor.fetchone()
        if row:
            # Update usage
            self.cursor.execute('UPDATE qa_knowledge_base SET usage_count = usage_count + 1, last_used = ? WHERE question_lower LIKE ?',
                              (datetime.now(timezone.utc).timestamp(), f"%{question.lower()}%"))
            self.connection.commit()
        return row
    
    def get_pending_qa(self, limit: int = 5):
        """Get unapproved Q&A entries."""
        self.cursor.execute('''
            SELECT id, question, answer, created_by FROM qa_knowledge_base
            WHERE approved = 0
            ORDER BY created_at ASC
            LIMIT ?
        ''', (limit,))
        return self.cursor.fetchall()
    
    def approve_qa(self, qa_id: int):
        """Approve a Q&A entry."""
        self.cursor.execute('UPDATE qa_knowledge_base SET approved = 1 WHERE id = ?', (qa_id,))
        self.connection.commit()
    
    # ============== USER RATING METHODS ==============
    
    def get_user_rating(self, user_id: str):
        """Get user rating data."""
        self.cursor.execute('''
            SELECT grade, interaction_score, chat_count, qa_contributions, notes
            FROM user_ratings WHERE user_id = ?
        ''', (user_id,))
        
        row = self.cursor.fetchone()
        if not row:
            return {"grade": "F", "score": 0, "chats": 0, "qa_contrib": 0, "notes": ""}
        
        return {
            "grade": row[0],
            "score": row[1],
            "chats": row[2],
            "qa_contrib": row[3],
            "notes": row[4]
        }
    
    def update_user_rating(self, user_id: str, grade: str = None, score_delta: int = 0, 
                          chat_delta: int = 0, qa_delta: int = 0, notes: str = None):
        """Update user rating."""
        current = self.get_user_rating(user_id)
        
        new_score = current["score"] + score_delta
        new_chats = current["chats"] + chat_delta
        new_qa = current["qa_contrib"] + qa_delta
        new_grade = grade or current["grade"]
        new_notes = notes or current["notes"]
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO user_ratings
            (user_id, grade, interaction_score, chat_count, qa_contributions, last_interaction, notes)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        ''', (user_id, new_grade, new_score, new_chats, new_qa, datetime.now(timezone.utc).timestamp(), new_notes))
        
        self.connection.commit()
    
    # ============== AFFECTION TRACKING ==============
    
    def add_affection_source(self, user_id: str, source: str, value: int):
        """Log affection source (donation, chat, qa_contrib, etc)."""
        self.cursor.execute('''
            INSERT INTO affection_sources (user_id, source, value, timestamp)
            VALUES (?, ?, ?, ?)
        ''', (user_id, source, value, datetime.now(timezone.utc).timestamp()))
        self.connection.commit()
    
    def get_affection_breakdown(self, user_id: str):
        """Get breakdown of affection sources."""
        self.cursor.execute('''
            SELECT source, SUM(value) as total FROM affection_sources
            WHERE user_id = ?
            GROUP BY source
        ''', (user_id,))
        
        return {row[0]: row[1] for row in self.cursor.fetchall()}

kb = KnowledgeBase()
