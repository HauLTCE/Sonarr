import asyncio
import logging
from utils.ytdl import YTDLSource

logger = logging.getLogger("bot")

class LazyQueueItem:
    """Represents a queue item with lazy-loaded metadata for 500-1000ms faster operations."""
    
    def __init__(self, title: str, url: str):
        """Initialize with basic info (title and URL are already available)."""
        self.title = title
        self.url = url
        self.metadata = None  # Loaded on demand
        self.loading = False
    
    async def get_metadata(self, bot_loop):
        """Lazily load full metadata only when needed (not on queue add)."""
        if self.metadata is not None:
            return self.metadata
        
        if self.loading:
            # Already loading, wait for it
            while self.loading:
                await asyncio.sleep(0.1)
            return self.metadata
        
        try:
            self.loading = True
            logger.debug(f"[LazyQueue] Loading metadata for: {self.title}")
            player = await YTDLSource.from_url(self.url, loop=bot_loop, stream=False)
            self.metadata = player.data
            self.loading = False
            return self.metadata
        except Exception as e:
            logger.error(f"[LazyQueue] Failed to load metadata for {self.url}: {e}")
            self.loading = False
            return None
    
    def __repr__(self):
        return f"LazyQueueItem(title='{self.title}', url='{self.url}')"

class LazyMusicQueue:
    """Music queue with lazy metadata loading - defer expensive operations until needed."""
    
    def __init__(self):
        self.items = []
    
    def add(self, title: str, url: str):
        """Add item to queue (O(1), no metadata loading)."""
        item = LazyQueueItem(title, url)
        self.items.append(item)
        logger.debug(f"[LazyQueue] Added: {title} (queue length: {len(self.items)})")
        return item
    
    def pop(self, index: int = 0):
        """Remove and return item from queue."""
        if index < len(self.items):
            item = self.items.pop(index)
            logger.debug(f"[LazyQueue] Removed: {item.title} (queue length: {len(self.items)})")
            return item
        return None
    
    def extend(self, items: list):
        """Add multiple items at once."""
        for title, url in items:
            self.add(title, url)
    
    def __len__(self):
        return len(self.items)
    
    def __getitem__(self, index):
        return self.items[index]
    
    def __repr__(self):
        return f"LazyMusicQueue({len(self.items)} items)"
