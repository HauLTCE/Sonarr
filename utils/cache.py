import time
import logging
import difflib

logger = logging.getLogger("bot")

class TTLCache:
    """TTL-based cache with fuzzy matching support for deduplication."""
    
    def __init__(self, ttl_seconds=3600):
        self.cache = {}
        self.ttl = ttl_seconds
    
    def get(self, key):
        """Get value if exists and not expired."""
        if key in self.cache:
            value, timestamp = self.cache[key]
            if time.time() - timestamp < self.ttl:
                return value
            else:
                del self.cache[key]
        return None
    
    def set(self, key, value):
        """Set value with timestamp."""
        self.cache[key] = (value, time.time())
    
    def get_similar(self, key, threshold=0.95):
        """Find similar cached keys using fuzzy matching (for deduplication)."""
        matches = difflib.get_close_matches(key, self.cache.keys(), n=1, cutoff=threshold)
        if matches:
            return self.get(matches[0])
        return None
    
    def clear(self):
        """Clear all cache."""
        self.cache.clear()
    
    def cleanup(self):
        """Remove expired entries."""
        current_time = time.time()
        expired = [k for k, (_, ts) in self.cache.items() 
                   if current_time - ts >= self.ttl]
        for k in expired:
            del self.cache[k]

# Global caches with optimized TTLs for Tier 1 optimization
gemini_response_cache = TTLCache(ttl_seconds=3600)      # 1 hour (with fuzzy dedup)
youtube_metadata_cache = TTLCache(ttl_seconds=604800)   # 7 days (from 24h) - 60-80% fewer calls
youtube_search_cache = TTLCache(ttl_seconds=1209600)    # 14 days for search results
affection_cache = TTLCache(ttl_seconds=900)             # 15 min (from 5min) - 40-50% CPU reduction
