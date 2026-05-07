"""
ui.py — Interactive music player views (buttons + embeds).
"""

import math
import logging
import time
from typing import TYPE_CHECKING

import discord
import aiohttp

if TYPE_CHECKING:
    from .player import MusicPlayer
    from .track import Track
    from .queue import LoopMode

logger = logging.getLogger("bot")


def format_duration(seconds: int) -> str:
    """Format seconds as MM:SS or HH:MM:SS."""
    if seconds <= 0:
        return "Live"
    m, s = divmod(seconds, 60)
    h, m = divmod(m, 60)
    if h > 0:
        return f"{h:02d}:{m:02d}:{s:02d}"
    return f"{m:02d}:{s:02d}"


def make_progress_bar(elapsed: float, total: int, length: int = 20) -> str:
    """
    Create a text progress bar.

    Example: ▬▬▬▬▬🔘▬▬▬▬▬▬▬▬▬▬▬▬▬▬ 01:23 / 03:42
    """
    if total <= 0:
        return "🔴 Live Stream"

    progress = min(elapsed / total, 1.0)
    filled = int(length * progress)

    bar = "▬" * filled + "🔘" + "▬" * (length - filled - 1)
    elapsed_str = format_duration(int(elapsed))
    total_str = format_duration(total)

    return f"{bar} {elapsed_str} / {total_str}"


def build_now_playing_embed(track: "Track", player: "MusicPlayer") -> discord.Embed:
    """Build the Now Playing embed."""
    from .queue import LoopMode

    embed = discord.Embed(
        title="🎵 Now Playing",
        color=0x00FF00,  # Green accent — matches your screenshot
    )

    # Track info
    title_display = track.title or "Unknown"
    if track.url:
        embed.description = f"**[{title_display}]({track.url})**"
    else:
        embed.description = f"**{title_display}**"

    if track.author:
        embed.description += f"\nby {track.author}"

    # Thumbnail
    if track.thumbnail:
        embed.set_thumbnail(url=track.thumbnail)

    # Progress bar
    elapsed = player.elapsed
    progress_bar = make_progress_bar(elapsed, track.duration)
    embed.add_field(name="", value=progress_bar, inline=False)

    # Footer info
    queue_len = len(player.queue)
    loop_text = ""
    if player.queue.loop_mode == LoopMode.SONG:
        loop_text = " | 🔂 Looping Song"
    elif player.queue.loop_mode == LoopMode.QUEUE:
        loop_text = " | 🔁 Looping Queue"

    vol_pct = int(player.volume * 100)
    footer = f"Duration: {track.duration_str} | Queue: {queue_len} tracks | Vol: {vol_pct}%{loop_text}"
    embed.set_footer(text=footer)

    # Requested by
    if track.requested_by and track.requested_by != "Unknown":
        embed.add_field(name="Requested by", value=track.requested_by, inline=True)

    return embed


class PlayerControlView(discord.ui.View):
    """
    Interactive button controls for the Now Playing message.
    """

    def __init__(self, cog, player: "MusicPlayer", track: "Track"):
        super().__init__(timeout=None)
        self.cog = cog
        self.player = player
        self.track = track

    # ── Row 0: Playback Controls ─────────────────────────────────

    @discord.ui.button(
        emoji="⏸️", style=discord.ButtonStyle.primary,
        custom_id="music_play_pause", row=0
    )
    async def play_pause(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.player.is_connected:
            return await interaction.response.send_message("Player disconnected.", ephemeral=True)

        if self.player.is_paused:
            await self.player.resume()
            button.emoji = "⏸️"
        else:
            await self.player.pause()
            button.emoji = "▶️"

        # Refresh the embed with updated progress
        embed = build_now_playing_embed(self.player.current or self.track, self.player)
        await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(
        emoji="⏭️", style=discord.ButtonStyle.secondary,
        custom_id="music_skip", row=0
    )
    async def skip(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.player.is_connected:
            return await interaction.response.send_message("Player disconnected.", ephemeral=True)

        skipped = await self.player.skip()
        await interaction.response.send_message(
            f"Skipped: **{skipped.title}**" if skipped else "Skipped.",
            ephemeral=True, delete_after=5
        )

    @discord.ui.button(
        emoji="🔁", style=discord.ButtonStyle.secondary,
        custom_id="music_loop", row=0
    )
    async def loop(self, interaction: discord.Interaction, button: discord.ui.Button):
        from .queue import LoopMode

        if not self.player.is_connected:
            return await interaction.response.send_message("Player disconnected.", ephemeral=True)

        queue = self.player.queue
        if queue.loop_mode == LoopMode.OFF:
            queue.loop_mode = LoopMode.SONG
            button.emoji = "🔂"
            button.style = discord.ButtonStyle.primary
            msg = "Looping current song."
        elif queue.loop_mode == LoopMode.SONG:
            queue.loop_mode = LoopMode.QUEUE
            button.emoji = "🔁"
            button.style = discord.ButtonStyle.primary
            msg = "Looping entire queue."
        else:
            queue.loop_mode = LoopMode.OFF
            button.emoji = "🔁"
            button.style = discord.ButtonStyle.secondary
            msg = "Looping disabled."

        embed = build_now_playing_embed(self.player.current or self.track, self.player)
        await interaction.response.edit_message(embed=embed, view=self)
        await interaction.followup.send(msg, ephemeral=True)

    @discord.ui.button(
        emoji="📜", style=discord.ButtonStyle.secondary,
        custom_id="music_queue", row=0
    )
    async def view_queue(self, interaction: discord.Interaction, button: discord.ui.Button):
        upcoming = self.player.queue.upcoming
        if not upcoming:
            return await interaction.response.send_message("Queue is empty.", ephemeral=True)

        view = QueuePaginationView(upcoming, self.player.current)
        await interaction.response.send_message(embed=view.get_embed(), view=view, ephemeral=True)

    # ── Row 1: Extra Controls ────────────────────────────────────

    @discord.ui.button(
        emoji="🔀", style=discord.ButtonStyle.secondary,
        custom_id="music_shuffle", row=1
    )
    async def shuffle(self, interaction: discord.Interaction, button: discord.ui.Button):
        self.player.queue.shuffle()
        await interaction.response.send_message("🔀 Queue shuffled.", ephemeral=True, delete_after=5)

    @discord.ui.button(
        emoji="⏹️", style=discord.ButtonStyle.danger,
        custom_id="music_stop", row=1
    )
    async def stop(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.player.is_connected:
            await self.player.disconnect()
            self.cog._destroy_player(interaction.guild_id)
        # Disable all buttons
        for child in self.children:
            child.disabled = True
        embed = build_now_playing_embed(self.track, self.player)
        embed.title = "⏹️ Player Stopped"
        embed.color = 0xFF0000
        await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(
        emoji="❤️", label="Save", style=discord.ButtonStyle.success,
        custom_id="music_save", row=1
    )
    async def save_song(self, interaction: discord.Interaction, button: discord.ui.Button):
        playlists = list(self.cog.saved_playlists.keys())
        if not playlists:
            return await interaction.response.send_message(
                "No saved playlists! Use `!playlist create <name>` first.",
                ephemeral=True
            )
        view = SaveToPlaylistView(self.cog, self.track)
        await interaction.response.send_message(
            "Select a playlist:", view=view, ephemeral=True
        )

    @discord.ui.button(
        emoji="🎤", label="Lyrics", style=discord.ButtonStyle.secondary,
        custom_id="music_lyrics", row=1
    )
    async def show_lyrics(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.defer(ephemeral=True)
        title = self.track.title or "Unknown"
        author = self.track.author or ""
        query = f"{title} {author}".strip()

        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(
                    f"https://some-random-api.com/lyrics?title={query}"
                ) as resp:
                    if resp.status == 200:
                        data = await resp.json()
                        lyrics = data.get("lyrics", "No lyrics found.")
                        embed = discord.Embed(
                            title=f"Lyrics: {title}",
                            description=lyrics[:4096],
                            color=0x00FF00
                        )
                        return await interaction.followup.send(embed=embed, ephemeral=True)
                    else:
                        return await interaction.followup.send(
                            f"Could not find lyrics for {title}.", ephemeral=True
                        )
        except Exception:
            return await interaction.followup.send(
                f"Error fetching lyrics.", ephemeral=True
            )


# ── Save to Playlist View ───────────────────────────────────────

class SaveToPlaylistView(discord.ui.View):
    def __init__(self, cog, track: "Track"):
        super().__init__(timeout=60)
        self.cog = cog
        self.track = track

        options = [
            discord.SelectOption(label=name, value=name)
            for name in list(cog.saved_playlists.keys())[:25]
        ]

        self.select = discord.ui.Select(
            placeholder="Choose a playlist...",
            min_values=1, max_values=1,
            options=options
        )
        self.select.callback = self.select_callback
        self.add_item(self.select)

    async def select_callback(self, interaction: discord.Interaction):
        name = self.select.values[0]
        playlist = self.cog.saved_playlists[name]
        playlist.append(self.track.to_dict())
        self.cog._save_playlists_atomic()
        await interaction.response.edit_message(
            content=f"✅ Saved **{self.track.title}** to `{name}`!",
            view=None
        )


# ── Queue Pagination View ───────────────────────────────────────

class QueuePaginationView(discord.ui.View):
    def __init__(self, queue: list, current=None):
        super().__init__(timeout=180)
        self.queue = queue
        self.current = current
        self.page = 0
        self.per_page = 10
        self.max_page = max(0, math.ceil(len(self.queue) / self.per_page) - 1)

    def get_embed(self) -> discord.Embed:
        embed = discord.Embed(title="🎵 Music Queue", color=0x00FF00)
        lines = []

        if self.page == 0 and self.current:
            title = self.current.title if hasattr(self.current, 'title') else str(self.current)
            lines.append(f"**Now Playing:** {title}\n")

        start = self.page * self.per_page
        end = start + self.per_page

        if self.queue:
            lines.append("**Up Next:**")
            for i, track in enumerate(self.queue[start:end], start=start + 1):
                title = track.title if hasattr(track, 'title') else str(track)
                dur = track.duration_str if hasattr(track, 'duration_str') else "??"
                lines.append(f"`{i}.` {title} | `{dur}`")
        else:
            lines.append("Queue is empty.")

        embed.description = "\n".join(lines)
        embed.set_footer(
            text=f"Page {self.page + 1}/{self.max_page + 1} | {len(self.queue)} tracks"
        )
        return embed

    @discord.ui.button(emoji="◀", style=discord.ButtonStyle.secondary)
    async def prev_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        self.page = max(0, self.page - 1)
        await interaction.response.edit_message(embed=self.get_embed(), view=self)

    @discord.ui.button(emoji="▶", style=discord.ButtonStyle.secondary)
    async def next_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        self.page = min(self.max_page, self.page + 1)
        await interaction.response.edit_message(embed=self.get_embed(), view=self)
