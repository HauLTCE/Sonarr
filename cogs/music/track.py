"""
track.py — Lightweight dataclass for track metadata.
"""

from dataclasses import dataclass, field


@dataclass
class Track:
    """Represents a single audio track in the queue."""

    title: str = "Unknown"
    url: str = ""                # YouTube page URL (permanent)
    duration: int = 0            # Duration in seconds
    thumbnail: str = ""          # Thumbnail image URL
    author: str = "Unknown"      # Channel/uploader name
    query: str = ""              # Original query or URL used to find this track
    stream_url: str = ""         # Direct audio stream URL (expires ~6h, refresh before play)
    requested_by: str = "Unknown"  # Discord username who queued it

    @property
    def duration_str(self) -> str:
        """Format duration as MM:SS or HH:MM:SS."""
        if self.duration <= 0:
            return "Live"
        minutes, seconds = divmod(self.duration, 60)
        hours, minutes = divmod(minutes, 60)
        if hours > 0:
            return f"{hours:02d}:{minutes:02d}:{seconds:02d}"
        return f"{minutes:02d}:{seconds:02d}"

    @property
    def display_url(self) -> str:
        """Return the URL for display (YouTube page URL, not stream URL)."""
        return self.url or self.query

    def to_dict(self) -> dict:
        """Serialize for playlist saving (JSON-compatible)."""
        return {
            "title": self.title,
            "url": self.url,
            "duration": self.duration,
            "author": self.author,
        }

    @classmethod
    def from_dict(cls, data: dict, requested_by: str = "Playlist") -> "Track":
        """Deserialize from saved playlist data."""
        return cls(
            title=data.get("title", "Unknown"),
            url=data.get("url", ""),
            duration=data.get("duration", 0),
            author=data.get("author", "Unknown"),
            query=data.get("url", ""),
            requested_by=requested_by,
        )
