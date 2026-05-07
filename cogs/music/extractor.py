"""
extractor.py — Async yt-dlp wrapper for audio extraction.

All yt-dlp calls run in a thread executor to avoid blocking the Discord event loop.
"""

import asyncio
import logging
import functools
from dataclasses import dataclass

import yt_dlp

from .track import Track

logger = logging.getLogger("bot")

# ── yt-dlp config ────────────────────────────────────────────────
YTDL_OPTIONS = {
    'format': 'bestaudio/best',
    'noplaylist': True,
    'nocheckcertificate': True,
    'ignoreerrors': False,
    'logtostderr': False,
    'quiet': True,
    'no_warnings': True,
    'default_search': 'ytsearch',   # "ytsearch" means plain text → YouTube search
    'source_address': '0.0.0.0',    # Bind to IPv4 (IPv6 causes issues on some hosts)
    'extract_flat': False,
    'geo_bypass': True,
}

YTDL_PLAYLIST_OPTIONS = {
    **YTDL_OPTIONS,
    'noplaylist': False,
    'extract_flat': True,           # Don't resolve every track in a playlist upfront
    'playlistend': 100,             # Cap at 100 tracks
}

YTDL_SEARCH_OPTIONS = {
    **YTDL_OPTIONS,
    'default_search': 'ytsearch5',  # Return top 5 results
}

FFMPEG_OPTIONS = {
    'before_options': '-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5',
    'options': '-vn',               # -vn = no video, audio only
}


# ── Helper: determine if a string is a URL ───────────────────────
def _is_url(query: str) -> bool:
    return query.startswith(('http://', 'https://', 'www.'))


def _is_playlist_url(query: str) -> bool:
    return 'playlist' in query or 'list=' in query


# ── Core extraction functions (sync — run in executor) ───────────
def _extract_info(query: str, options: dict) -> dict | None:
    """Synchronous yt-dlp extraction. Called via run_in_executor."""
    try:
        with yt_dlp.YoutubeDL(options) as ydl:
            info = ydl.extract_info(query, download=False)
            return info
    except Exception as e:
        logger.error(f"[Extractor] yt-dlp error for '{query[:60]}': {e}")
        return None


def _info_to_track(info: dict, requested_by: str = "Unknown") -> Track:
    """Convert a yt-dlp info dict to a Track dataclass."""
    return Track(
        title=info.get('title', 'Unknown Title'),
        url=info.get('webpage_url') or info.get('url', ''),
        duration=info.get('duration', 0),
        thumbnail=info.get('thumbnail', ''),
        author=info.get('uploader') or info.get('channel', 'Unknown'),
        query=info.get('webpage_url') or info.get('original_url', ''),
        stream_url=info.get('url', ''),      # Direct audio stream URL
        requested_by=requested_by,
    )


# ── Public async API ─────────────────────────────────────────────
class YTDLExtractor:
    """Async wrapper around yt-dlp for the music player."""

    async def search(self, query: str, requested_by: str = "Unknown") -> Track | None:
        """
        Search for a single track. Accepts:
        - YouTube URL → extracts directly
        - Plain text → searches YouTube ("ytsearch:<query>")

        Returns a Track or None if nothing found.
        """
        loop = asyncio.get_event_loop()
        info = await loop.run_in_executor(
            None,
            functools.partial(_extract_info, query, YTDL_OPTIONS)
        )

        if not info:
            return None

        # If search returned multiple entries, take the first
        if 'entries' in info:
            entries = list(info['entries'])
            if not entries:
                return None
            info = entries[0]

        return _info_to_track(info, requested_by)

    async def search_multiple(self, query: str, count: int = 5, requested_by: str = "Unknown") -> list[Track]:
        """
        Search YouTube and return up to `count` results.
        Used by the !search command to let the user pick.
        """
        search_query = f"ytsearch{count}:{query}" if not _is_url(query) else query

        loop = asyncio.get_event_loop()
        info = await loop.run_in_executor(
            None,
            functools.partial(_extract_info, search_query, YTDL_OPTIONS)
        )

        if not info:
            return []

        entries = info.get('entries', [info])
        return [_info_to_track(e, requested_by) for e in entries if e]

    async def extract_playlist(self, url: str, requested_by: str = "Unknown") -> list[Track]:
        """
        Extract all tracks from a YouTube playlist URL.
        Uses extract_flat=True so it's fast (doesn't resolve each track).
        Each track only has title + URL — stream_url is fetched at play time.
        """
        loop = asyncio.get_event_loop()
        info = await loop.run_in_executor(
            None,
            functools.partial(_extract_info, url, YTDL_PLAYLIST_OPTIONS)
        )

        if not info or 'entries' not in info:
            return []

        tracks = []
        for entry in info['entries']:
            if not entry:
                continue
            tracks.append(Track(
                title=entry.get('title', 'Unknown'),
                url=entry.get('url') or entry.get('webpage_url', ''),
                duration=entry.get('duration', 0),
                thumbnail=entry.get('thumbnail', ''),
                author=entry.get('uploader', 'Unknown'),
                query=entry.get('url') or entry.get('webpage_url', ''),
                stream_url='',   # Will be extracted at play time
                requested_by=requested_by,
            ))

        return tracks[:100]  # Hard cap

    async def refresh_stream_url(self, track: Track) -> str | None:
        """
        Re-extract the direct audio stream URL for a track.
        Called RIGHT BEFORE playback because YouTube URLs expire (~6 hours).

        Returns the stream URL string, or None on failure.
        """
        query = track.url or track.query
        if not query:
            return None

        loop = asyncio.get_event_loop()
        info = await loop.run_in_executor(
            None,
            functools.partial(_extract_info, query, YTDL_OPTIONS)
        )

        if not info:
            return None

        if 'entries' in info:
            entries = list(info['entries'])
            if not entries:
                return None
            info = entries[0]

        stream_url = info.get('url', '')
        track.stream_url = stream_url

        # Also update metadata if it was a flat-extracted playlist entry
        if not track.title or track.title == 'Unknown':
            track.title = info.get('title', 'Unknown')
        if not track.duration:
            track.duration = info.get('duration', 0)
        if not track.thumbnail:
            track.thumbnail = info.get('thumbnail', '')
        if not track.author or track.author == 'Unknown':
            track.author = info.get('uploader') or info.get('channel', 'Unknown')

        return stream_url
