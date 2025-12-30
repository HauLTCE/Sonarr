"""
Classification Logger - Saves all AI classification decisions to a file for debugging.

Writes in JSON Lines format (one JSON object per line) which is:
- Corruption resistant (each line is independent)
- Easy to parse and analyze
- Appendable without loading entire file
"""

import json
import os
import threading
from datetime import datetime, timezone
from pathlib import Path
from queue import Queue
from typing import Optional
import atexit

# Configuration
LOG_DIR = Path("logs/classification")
LOG_FILE_PREFIX = "classification"
MAX_FILE_SIZE_MB = 50  # Rotate after 50MB
FLUSH_INTERVAL = 10  # Flush every 10 entries


class ClassificationLogger:
    """Thread-safe logger for classification events."""
    
    _instance = None
    _lock = threading.Lock()
    
    def __new__(cls):
        if cls._instance is None:
            with cls._lock:
                if cls._instance is None:
                    cls._instance = super().__new__(cls)
                    cls._instance._initialized = False
        return cls._instance
    
    def __init__(self):
        if self._initialized:
            return
        
        self._initialized = True
        self._write_queue = Queue()
        self._buffer = []
        self._buffer_lock = threading.Lock()
        self._file_handle = None
        self._current_file_path = None
        self._entry_count = 0
        
        # Ensure log directory exists
        LOG_DIR.mkdir(parents=True, exist_ok=True)
        
        # Open initial log file
        self._rotate_if_needed()
        
        # Register cleanup on exit
        atexit.register(self._flush_and_close)
    
    def _get_log_filename(self) -> Path:
        """Generate log filename with date."""
        date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        return LOG_DIR / f"{LOG_FILE_PREFIX}_{date_str}.jsonl"
    
    def _rotate_if_needed(self):
        """Check file size and rotate if needed."""
        target_path = self._get_log_filename()
        
        # Check if we need a new file (date changed or size exceeded)
        if self._current_file_path != target_path:
            self._close_file()
            self._current_file_path = target_path
            self._open_file()
        elif self._current_file_path and self._current_file_path.exists():
            size_mb = self._current_file_path.stat().st_size / (1024 * 1024)
            if size_mb >= MAX_FILE_SIZE_MB:
                # Add sequence number for rotation
                base = self._current_file_path.stem
                ext = self._current_file_path.suffix
                seq = 1
                while True:
                    new_path = LOG_DIR / f"{base}_{seq}{ext}"
                    if not new_path.exists():
                        self._close_file()
                        self._current_file_path = new_path
                        self._open_file()
                        break
                    seq += 1
    
    def _open_file(self):
        """Open log file for appending."""
        try:
            self._file_handle = open(self._current_file_path, 'a', encoding='utf-8', buffering=1)  # Line buffered
        except Exception as e:
            print(f"[ClassificationLogger] Failed to open log file: {e}")
            self._file_handle = None
    
    def _close_file(self):
        """Safely close current file."""
        if self._file_handle:
            try:
                self._flush_buffer()
                self._file_handle.close()
            except Exception:
                pass
            self._file_handle = None
    
    def _flush_buffer(self):
        """Write buffered entries to file."""
        with self._buffer_lock:
            if not self._buffer or not self._file_handle:
                return
            
            try:
                for entry in self._buffer:
                    line = json.dumps(entry, ensure_ascii=False, separators=(',', ':'))
                    self._file_handle.write(line + '\n')
                self._file_handle.flush()
                os.fsync(self._file_handle.fileno())  # Force write to disk
                self._buffer.clear()
            except Exception as e:
                print(f"[ClassificationLogger] Flush error: {e}")
    
    def _flush_and_close(self):
        """Cleanup on exit."""
        self._flush_buffer()
        self._close_file()
    
    def log(
        self,
        event_type: str,
        message: str,
        user_id: Optional[str] = None,
        guild_id: Optional[str] = None,
        category: Optional[str] = None,
        confidence: Optional[int] = None,
        source: Optional[str] = None,  # "pattern", "keyword", "cache", "ai", "empty", etc.
        response: Optional[str] = None,
        extra: Optional[dict] = None
    ):
        """
        Log a classification event.
        
        Args:
            event_type: Type of event (classify, misgender, pattern, ai_call, trigger, response)
            message: The message content being classified
            user_id: Discord user ID
            guild_id: Discord guild/server ID
            category: The classification category result
            confidence: Confidence score if available
            source: Source of classification (pattern, keyword, cache, ai)
            response: The response sent to the user
            extra: Any additional data to log
        """
        entry = {
            "ts": datetime.now(timezone.utc).isoformat(),
            "event": event_type,
            "msg": message[:500] if message else None,  # Truncate very long messages
        }
        
        # Only add non-None fields
        if user_id:
            entry["user"] = user_id
        if guild_id:
            entry["guild"] = str(guild_id)
        if category:
            entry["cat"] = category
        if confidence is not None:
            entry["conf"] = confidence
        if source:
            entry["src"] = source
        if response:
            entry["resp"] = response[:200] if len(response) > 200 else response  # Truncate response
        if extra:
            entry["extra"] = extra
        
        with self._buffer_lock:
            self._buffer.append(entry)
            self._entry_count += 1
        
        # Flush periodically
        if self._entry_count % FLUSH_INTERVAL == 0:
            self._rotate_if_needed()
            self._flush_buffer()
    
    def log_classify(
        self,
        message: str,
        category: str,
        source: str,
        confidence: int = None,
        user_id: str = None,
        guild_id: str = None,
        word_count: int = None,
        extra: dict = None
    ):
        """Convenience method for classification events."""
        _extra = {"words": word_count} if word_count else {}
        if extra:
            _extra.update(extra)
        self.log(
            event_type="classify",
            message=message,
            category=category,
            source=source,
            confidence=confidence,
            user_id=user_id,
            guild_id=guild_id,
            extra=_extra if _extra else None
        )
    
    def log_misgender(
        self,
        message: str,
        term: str,
        user_id: str = None,
        guild_id: str = None,
        third_party: bool = False
    ):
        """Log misgendering detection."""
        self.log(
            event_type="misgender",
            message=message,
            category="misgendered",
            source="pattern",
            user_id=user_id,
            guild_id=guild_id,
            extra={"term": term, "third_party": third_party}
        )
    
    def log_ai_call(
        self,
        message: str,
        category: str,
        user_id: str = None,
        guild_id: str = None,
        model: str = None,
        cached: bool = False,
        fallback: bool = False
    ):
        """Log AI API call."""
        self.log(
            event_type="ai_call",
            message=message,
            category=category,
            source="cache" if cached else "ai",
            user_id=user_id,
            guild_id=guild_id,
            extra={"model": model, "fallback": fallback} if model or fallback else None
        )
    
    def log_trigger(
        self,
        message: str,
        user_name: str,
        user_id: str,
        guild_id: str = None,
        channel_id: str = None
    ):
        """Log message trigger (when bot decides to respond)."""
        self.log(
            event_type="trigger",
            message=message,
            user_id=user_id,
            guild_id=guild_id,
            extra={"user_name": user_name, "channel": channel_id}
        )
    
    def log_response(
        self,
        message: str,
        response: str,
        category: str = None,
        user_id: str = None,
        guild_id: str = None
    ):
        """Log bot response."""
        self.log(
            event_type="response",
            message=message,
            response=response,
            category=category,
            user_id=user_id,
            guild_id=guild_id
        )
    
    def log_pattern(
        self,
        message: str,
        pattern_type: str,
        result: str,
        modifiers: dict = None,
        user_id: str = None,
        guild_id: str = None
    ):
        """Log pattern matching result."""
        self.log(
            event_type="pattern",
            message=message,
            category=result,
            source="pattern",
            user_id=user_id,
            guild_id=guild_id,
            extra={"type": pattern_type, "mods": modifiers} if modifiers else {"type": pattern_type}
        )
    
    def log_error(
        self,
        message: str,
        error: str,
        context: str = None,
        user_id: str = None,
        guild_id: str = None
    ):
        """Log an error during classification."""
        self.log(
            event_type="error",
            message=message,
            user_id=user_id,
            guild_id=guild_id,
            extra={"error": error, "context": context}
        )
    
    def flush(self):
        """Manually flush the buffer."""
        self._flush_buffer()


# Global instance
classification_logger = ClassificationLogger()


# Convenience functions
def log_classify(*args, **kwargs):
    classification_logger.log_classify(*args, **kwargs)

def log_misgender(*args, **kwargs):
    classification_logger.log_misgender(*args, **kwargs)

def log_ai_call(*args, **kwargs):
    classification_logger.log_ai_call(*args, **kwargs)

def log_trigger(*args, **kwargs):
    classification_logger.log_trigger(*args, **kwargs)

def log_response(*args, **kwargs):
    classification_logger.log_response(*args, **kwargs)

def log_pattern(*args, **kwargs):
    classification_logger.log_pattern(*args, **kwargs)

def log_error(*args, **kwargs):
    classification_logger.log_error(*args, **kwargs)

def flush_logs():
    classification_logger.flush()
