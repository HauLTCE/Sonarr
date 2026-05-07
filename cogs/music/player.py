"""
player.py — Per-guild music player engine.

Manages voice connection, playback, volume, and the play-next loop.
"""

import asyncio
import logging
import time

import discord

from .track import Track
from .queue import SongQueue, LoopMode
from .extractor import YTDLExtractor, FFMPEG_OPTIONS

logger = logging.getLogger("bot")


class MusicPlayer:
    """
    Per-guild music player.

    Lifecycle:
        1. Created when a user runs !play in a guild for the first time
        2. Connects to voice channel
        3. Plays tracks from the queue
        4. Auto-disconnects after inactivity or when alone in VC
        5. Destroyed when disconnected
    """

    def __init__(self, bot, guild: discord.Guild, text_channel_id: int):
        self.bot = bot
        self.guild = guild
        self.text_channel_id = text_channel_id

        self.queue = SongQueue()
        self.extractor = YTDLExtractor()

        self.current: Track | None = None
        self.voice_client: discord.VoiceClient | None = None
        self.volume: float = 0.5            # 0.0 to 1.0 (50% default)
        self.start_time: float = 0.0        # When current track started (for progress)
        self.paused_at: float | None = None # When paused (for progress calc)

        self._play_next_lock = asyncio.Lock()
        self._destroyed = False

        # Now Playing message reference (for updating/deleting)
        self.np_message: discord.Message | None = None

    # ── Properties ───────────────────────────────────────────────

    @property
    def is_playing(self) -> bool:
        return (
            self.voice_client is not None
            and self.voice_client.is_playing()
        )

    @property
    def is_paused(self) -> bool:
        return (
            self.voice_client is not None
            and self.voice_client.is_paused()
        )

    @property
    def is_connected(self) -> bool:
        return (
            self.voice_client is not None
            and self.voice_client.is_connected()
        )

    @property
    def elapsed(self) -> float:
        """Seconds elapsed in current track."""
        if self.start_time == 0:
            return 0.0
        if self.paused_at is not None:
            return self.paused_at - self.start_time
        return time.time() - self.start_time

    # ── Connection ───────────────────────────────────────────────

    async def connect(self, channel: discord.VoiceChannel) -> None:
        """Connect to a voice channel."""
        if self.voice_client and self.voice_client.is_connected():
            if self.voice_client.channel.id != channel.id:
                await self.voice_client.move_to(channel)
            return

        self.voice_client = await channel.connect(self_deaf=True)

    async def disconnect(self) -> None:
        """Disconnect from voice and clean up."""
        self._destroyed = True
        self.current = None
        self.queue.clear()

        if self.voice_client:
            try:
                if self.voice_client.is_playing():
                    self.voice_client.stop()
                await self.voice_client.disconnect()
            except Exception:
                pass
            self.voice_client = None

    # ── Playback ─────────────────────────────────────────────────

    async def play_track(self, track: Track) -> bool:
        """
        Play a single track. Refreshes stream URL, creates FFmpeg source,
        starts playback.

        Returns True if playback started, False on error.
        """
        if not self.is_connected:
            return False

        # Refresh the stream URL (YouTube URLs expire)
        stream_url = await self.extractor.refresh_stream_url(track)
        if not stream_url:
            logger.warning(f"[Player] Failed to get stream URL for: {track.title}")
            return False

        self.current = track
        self.start_time = time.time()
        self.paused_at = None

        # Create FFmpeg audio source with volume control
        try:
            source = discord.FFmpegPCMAudio(stream_url, **FFMPEG_OPTIONS)
            source = discord.PCMVolumeTransformer(source, volume=self.volume)
        except Exception as e:
            logger.error(f"[Player] FFmpeg source creation failed: {e}")
            return False

        # Play with after= callback to chain next track
        def after_callback(error):
            if error:
                logger.error(f"[Player] Playback error: {error}")
            if not self._destroyed:
                asyncio.run_coroutine_threadsafe(
                    self.play_next(), self.bot.loop
                )

        try:
            self.voice_client.play(source, after=after_callback)
        except Exception as e:
            logger.error(f"[Player] voice_client.play failed: {e}")
            return False

        logger.info(f"[Player] Now playing: {track.title} in guild {self.guild.id}")
        return True

    async def play_next(self) -> None:
        """
        Called after a track finishes. Gets next from queue and plays it.
        If queue is empty, clean up the Now Playing message.
        """
        async with self._play_next_lock:
            if self._destroyed:
                return

            next_track = self.queue.get_next(self.current)

            if next_track is None:
                # Queue exhausted
                self.current = None
                self.start_time = 0.0
                # Update bot status
                await self._update_presence(None)
                # Don't disconnect — wait for inactivity timeout
                return

            success = await self.play_track(next_track)
            if not success:
                # Failed to play — try the next one
                logger.warning(f"[Player] Skipping failed track: {next_track.title}")
                await self.play_next()

    async def skip(self) -> Track | None:
        """Skip the current track. Returns the skipped track."""
        skipped = self.current
        if self.voice_client and self.voice_client.is_playing():
            self.voice_client.stop()  # Triggers after_callback → play_next
        return skipped

    async def pause(self) -> None:
        """Pause playback."""
        if self.voice_client and self.voice_client.is_playing():
            self.voice_client.pause()
            self.paused_at = time.time()

    async def resume(self) -> None:
        """Resume playback."""
        if self.voice_client and self.voice_client.is_paused():
            # Adjust start_time to account for pause duration
            if self.paused_at:
                pause_duration = time.time() - self.paused_at
                self.start_time += pause_duration
                self.paused_at = None
            self.voice_client.resume()

    async def set_volume(self, vol: int) -> None:
        """Set volume (0-100). Updates live if currently playing."""
        self.volume = vol / 100.0
        if self.voice_client and self.voice_client.source:
            # PCMVolumeTransformer exposes .volume
            if isinstance(self.voice_client.source, discord.PCMVolumeTransformer):
                self.voice_client.source.volume = self.volume

    # ── Helpers ──────────────────────────────────────────────────

    async def _update_presence(self, track: Track | None) -> None:
        """Update bot's Discord presence."""
        if track:
            activity = discord.Activity(
                type=discord.ActivityType.listening,
                name=track.title[:128]
            )
        else:
            activity = discord.Activity(
                type=discord.ActivityType.watching,
                name="for !help"
            )
        try:
            await self.bot.change_presence(activity=activity)
        except Exception:
            pass

    async def get_text_channel(self) -> discord.TextChannel | None:
        """Get the bound text channel for sending Now Playing messages."""
        channel = self.bot.get_channel(self.text_channel_id)
        if isinstance(channel, discord.TextChannel):
            return channel
        return None
