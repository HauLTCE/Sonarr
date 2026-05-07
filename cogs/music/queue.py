"""
queue.py — Per-guild song queue with loop modes and history.
"""

import random
from enum import Enum
from typing import Iterator

from .track import Track


class LoopMode(Enum):
    OFF = "off"
    SONG = "song"      # Repeat current track
    QUEUE = "queue"     # Repeat entire queue


class SongQueue:
    """
    Manages the track queue for a single guild.

    Features:
    - Standard FIFO queue
    - Loop modes: off, song, queue
    - History tracking (last 20 tracks)
    - Insert-at-front (playnext)
    - Range removal
    - Shuffle
    """

    def __init__(self, max_history: int = 20):
        self._queue: list[Track] = []
        self._history: list[Track] = []
        self._max_history = max_history
        self.loop_mode: LoopMode = LoopMode.OFF

    # ── Queue Operations ─────────────────────────────────────────

    def add(self, track: Track) -> int:
        """Add a track to the end of the queue. Returns the position (1-indexed)."""
        self._queue.append(track)
        return len(self._queue)

    def add_next(self, track: Track) -> None:
        """Insert a track at the front of the queue (plays next)."""
        self._queue.insert(0, track)

    def add_many(self, tracks: list[Track]) -> int:
        """Add multiple tracks. Returns count added."""
        self._queue.extend(tracks)
        return len(tracks)

    def get_next(self, current: Track | None = None) -> Track | None:
        """
        Get the next track to play, respecting loop mode.

        Args:
            current: The track that just finished playing.

        Returns:
            The next Track, or None if the queue is exhausted.
        """
        # Record the finished track in history
        if current:
            self._history.insert(0, current)
            if len(self._history) > self._max_history:
                self._history = self._history[:self._max_history]

        # Loop song: return the same track again
        if self.loop_mode == LoopMode.SONG and current:
            return current

        # Loop queue: put the finished track at the end
        if self.loop_mode == LoopMode.QUEUE and current:
            self._queue.append(current)

        # Pop the next track
        if self._queue:
            return self._queue.pop(0)

        return None

    def get_previous(self) -> Track | None:
        """Get the most recent track from history (for !previous)."""
        if len(self._history) >= 1:
            return self._history[0]
        return None

    def remove(self, index: int) -> Track | None:
        """
        Remove a track by 1-indexed position.
        Returns the removed Track or None if index is invalid.
        """
        if index < 1 or index > len(self._queue):
            return None
        return self._queue.pop(index - 1)

    def remove_range(self, start: int, end: int) -> list[Track]:
        """
        Remove tracks in a 1-indexed inclusive range [start, end].
        Returns list of removed tracks.
        """
        start_idx = max(0, start - 1)
        end_idx = min(len(self._queue), end)
        removed = self._queue[start_idx:end_idx]
        del self._queue[start_idx:end_idx]
        return removed

    def clear(self) -> None:
        """Clear the entire queue (does not affect history)."""
        self._queue.clear()

    def shuffle(self) -> None:
        """Shuffle the queue in-place."""
        random.shuffle(self._queue)

    # ── Query ────────────────────────────────────────────────────

    @property
    def upcoming(self) -> list[Track]:
        """Return a copy of the upcoming tracks."""
        return list(self._queue)

    @property
    def history(self) -> list[Track]:
        """Return a copy of the play history."""
        return list(self._history)

    @property
    def is_empty(self) -> bool:
        return len(self._queue) == 0

    def __len__(self) -> int:
        return len(self._queue)

    def __iter__(self) -> Iterator[Track]:
        return iter(self._queue)

    def __bool__(self) -> bool:
        return bool(self._queue)
