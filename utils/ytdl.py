import discord
import yt_dlp
import asyncio
import logging
from .cache import youtube_metadata_cache, youtube_search_cache

logger = logging.getLogger("bot")

yt_dlp.utils.bug_reports_message = lambda *args, **kwargs: ''

ytdl_format_options = {
    'format': 'bestaudio/best',
    'outtmpl': '%(extractor)s-%(id)s-%(title)s.%(ext)s',
    'restrictfilenames': True,
    'noplaylist': False,
    'nocheckcertificate': True,
    'ignoreerrors': False,
    'logtostderr': False,
    'quiet': True,
    'no_warnings': True,
    'default_search': 'auto',
    'source_address': '0.0.0.0',
    'socket_timeout': 15,
    'retries': 10
}

ffmpeg_options = {
    'options': '-vn',
    'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5'
}

ytdl = yt_dlp.YoutubeDL(ytdl_format_options)

class YTDLSource(discord.PCMVolumeTransformer):
    def __init__(self, source, *, data, volume=0.5):
        super().__init__(source, volume)
        self.data = data
        self.title = data.get('title')
        self.url = data.get('url')

    @classmethod
    async def get_info(cls, url, *, loop=None):
        """
        Extracts video/playlist metadata without downloading.
        Returns either a single dict {'url','title'} or a list of such dicts when a playlist or search returns multiple entries.
        Tier 1 Optimization: Uses 7-day cache for metadata (60-80% fewer calls).
        """
        cached_result = youtube_metadata_cache.get(url)
        if cached_result:
            return cached_result
        
        loop = loop or asyncio.get_event_loop()
        
        try:
            data = await asyncio.wait_for(
                loop.run_in_executor(None, lambda: ytdl.extract_info(url, download=False)),
                timeout=30.0
            )

            if isinstance(data, dict) and 'entries' in data and isinstance(data['entries'], list):
                entries = []
                for entry in data['entries']:
                    if not entry:
                        continue
                    entries.append({'url': entry.get('webpage_url', entry.get('url')), 'title': entry.get('title')})
                youtube_metadata_cache.set(url, entries)
                return entries

            if isinstance(data, dict) and data.get('title'):
                result = {'url': data.get('webpage_url', data.get('url')), 'title': data.get('title')}
                youtube_metadata_cache.set(url, result)
                return result

            raise RuntimeError("No usable metadata found")

        except asyncio.TimeoutError:
            logger.error(f"Metadata fetch timeout (30s) for: {url}")
            raise RuntimeError(f"YouTube metadata fetch timed out. Please try again.")
        except Exception as e:
            raise e

    @classmethod
    async def search(cls, query, limit=5, *, loop=None):
        """
        Performs a YouTube search and returns up to `limit` matches as list of {'url','title'}.
        Tier 1 Optimization: Uses 14-day cache for search results (very stable, reduced API load).
        """

        cache_key = f"search:{query}:{limit}"
        
        cached_result = youtube_search_cache.get(cache_key)
        if cached_result:
            return cached_result
        
        loop = loop or asyncio.get_event_loop()
        search_query = f"ytsearch{limit}:{query}"
        try:
            data = await asyncio.wait_for(
                loop.run_in_executor(None, lambda: ytdl.extract_info(search_query, download=False)),
                timeout=30.0
            )
            entries = data.get('entries', []) if isinstance(data, dict) else []
            results = []
            for entry in entries:
                if not entry:
                    continue
                results.append({'url': entry.get('webpage_url', entry.get('url')), 'title': entry.get('title')})
            youtube_search_cache.set(cache_key, results)
            return results
        except asyncio.TimeoutError:
            logger.error(f"YouTube search timeout (30s) for: {query}")
            raise RuntimeError(f"YouTube search timed out. Please try again.")
        except Exception as e:
            raise e

    @classmethod
    async def from_url(cls, url, *, loop=None, stream=False):
        """
        Generates the actual Audio Player stream. Expects a single video URL or file path.
        """
        logger.debug(f"Generating audio stream for: {url}")
        loop = loop or asyncio.get_event_loop()
        
        try:
            data = await loop.run_in_executor(None, lambda: ytdl.extract_info(url, download=not stream))

            if isinstance(data, dict) and 'entries' in data and isinstance(data['entries'], list):
                entry = next((e for e in data['entries'] if e), None)
                if entry:
                    data = entry

            filename = data['url'] if stream else ytdl.prepare_filename(data)
            
            return cls(discord.FFmpegPCMAudio(filename, **ffmpeg_options), data=data)
            
        except Exception as e:
            raise e