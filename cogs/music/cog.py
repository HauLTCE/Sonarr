"""
cog.py — Main Music cog.
"""

import asyncio
import json
import logging
import os
import tempfile

import discord
from discord.ext import commands

from utils.checks import is_music_channel
from .player import MusicPlayer
from .track import Track
from .extractor import YTDLExtractor
from .queue import LoopMode
from .ui import PlayerControlView, QueuePaginationView, build_now_playing_embed

logger = logging.getLogger("bot")

PLAYLIST_FILE = "playlists.json"


class Music(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.players: dict[int, MusicPlayer] = {}  # guild_id -> MusicPlayer
        self.extractor = YTDLExtractor()
        self.saved_playlists = self._load_playlists()

    def _load_playlists(self) -> dict:
        if not os.path.exists(PLAYLIST_FILE):
            return {}

        try:
            with open(PLAYLIST_FILE, "r", encoding="utf-8") as file:
                raw = json.load(file)
        except (json.JSONDecodeError, OSError) as exc:
            logger.warning("Failed to load %s: %s", PLAYLIST_FILE, exc)
            return {}

        return raw

    def _save_playlists_atomic(self) -> bool:
        temp_path = ""
        try:
            fd, temp_path = tempfile.mkstemp(suffix=".json", dir=".")
            with os.fdopen(fd, "w", encoding="utf-8") as temp_file:
                json.dump(self.saved_playlists, temp_file, ensure_ascii=True, indent=2)
            os.replace(temp_path, PLAYLIST_FILE)
            return True
        except OSError as exc:
            logger.error("Failed to save playlists: %s", exc)
            if temp_path and os.path.exists(temp_path):
                try:
                    os.remove(temp_path)
                except OSError:
                    pass
            return False

    async def _get_or_create_player(self, ctx) -> MusicPlayer:
        guild_id = ctx.guild.id
        if guild_id not in self.players:
            self.players[guild_id] = MusicPlayer(
                self.bot, ctx.guild, ctx.channel.id
            )
        player = self.players[guild_id]
        player.text_channel_id = ctx.channel.id  # Update bound text channel
        return player

    def _get_player(self, guild_id: int) -> MusicPlayer | None:
        return self.players.get(guild_id)

    def _destroy_player(self, guild_id: int) -> None:
        if guild_id in self.players:
            del self.players[guild_id]

    async def _send_now_playing(self, player: MusicPlayer, track: Track, channel):
        """Send or update the interactive Now Playing embed."""
        embed = build_now_playing_embed(track, player)
        view = PlayerControlView(self, player, track)

        # Delete the old Now Playing message
        if player.np_message:
            try:
                await player.np_message.delete()
            except Exception:
                pass

        player.np_message = await channel.send(embed=embed, view=view)

    @commands.Cog.listener()
    async def on_voice_state_update(self, member, before, after):
        # Ignore bot's own state changes
        if member.id == self.bot.user.id:
            if after.channel is None:  # Bot was disconnected
                self._destroy_player(member.guild.id)
            return

        # Check if bot is now alone in VC
        player = self._get_player(member.guild.id)
        if not player or not player.is_connected:
            return

        voice_channel = player.voice_client.channel
        # Count non-bot members
        humans = [m for m in voice_channel.members if not m.bot]
        if len(humans) == 0:
            # Wait 30 seconds, then disconnect if still alone
            await asyncio.sleep(30)
            if not player.voice_client or not player.voice_client.channel:
                return
            humans = [m for m in player.voice_client.channel.members if not m.bot]
            if len(humans) == 0:
                text = await player.get_text_channel()
                await player.disconnect()
                self._destroy_player(member.guild.id)
                if text:
                    await text.send("Disconnected — everyone left the voice channel.")

    @commands.command()
    @is_music_channel()
    async def join(self, ctx):
        if not ctx.author.voice:
            return await ctx.send("Join a voice channel first.")

        player = await self._get_or_create_player(ctx)
        await player.connect(ctx.author.voice.channel)
        await ctx.send(f"Connected to {ctx.author.voice.channel.mention}.")

    @commands.command()
    @is_music_channel()
    async def play(self, ctx, *, query: str | None = None):
        if not ctx.author.voice:
            return await ctx.send("Join a voice channel first.")
            
        player = await self._get_or_create_player(ctx)
        await player.connect(ctx.author.voice.channel)

        # No query: resume if paused
        if not query:
            if player.is_paused:
                await player.resume()
                return await ctx.send("Resumed playback.")
            return await ctx.send("Provide a search query or URL.")

        async with ctx.typing():
            # Check for saved playlist
            if query in self.saved_playlists:
                entries = self.saved_playlists[query]
                tracks = []
                for entry in entries:
                    tracks.append(Track.from_dict(entry, requested_by=ctx.author.display_name))
                
                if not tracks:
                    return await ctx.send("Playlist is empty.")
                player.queue.add_many(tracks)
                await ctx.send(f"Added **{len(tracks)}** tracks from saved playlist `{query}`.")

            # Check for YouTube playlist
            elif 'list=' in query or 'playlist' in query:
                tracks = await self.extractor.extract_playlist(
                    query, requested_by=ctx.author.display_name
                )
                if not tracks:
                    return await ctx.send("Could not load playlist.")
                player.queue.add_many(tracks)
                await ctx.send(f"Added **{len(tracks)}** tracks from playlist.")
            else:
                # Single track
                track = await self.extractor.search(
                    query, requested_by=ctx.author.display_name
                )
                if not track:
                    return await ctx.send("No results found.")
                player.queue.add(track)
                if player.is_playing or player.is_paused:
                    await ctx.send(f"Added to queue: **{track.title}**")

        # Start playback if idle
        if not player.is_playing and not player.is_paused:
            next_track = player.queue.get_next(None)
            if next_track:
                success = await player.play_track(next_track)
                if success:
                    await self._send_now_playing(player, next_track, ctx.channel)

    @commands.command()
    @is_music_channel()
    async def pause(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or not player.is_playing:
            return await ctx.send("Nothing is playing.")
        await player.pause()
        await ctx.send("Paused playback.")

    @commands.command()
    @is_music_channel()
    async def resume(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or not player.is_paused:
            return await ctx.send("Player is not paused.")
        await player.resume()
        await ctx.send("Resumed playback.")

    @commands.command()
    @is_music_channel()
    async def skip(self, ctx, count: int = 1):
        player = self._get_player(ctx.guild.id)
        if not player or not player.is_playing:
            return await ctx.send("Nothing is playing.")

        if count > 1:
            for _ in range(count - 1):
                player.queue.get_next(None)  # discard

        skipped = await player.skip()
        await ctx.send(f"Skipped: **{skipped.title}**" if skipped else "Skipped.")

    @commands.command()
    @is_music_channel()
    async def stop(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or not player.is_connected:
            return await ctx.send("Not connected.")
        
        await player.disconnect()
        self._destroy_player(ctx.guild.id)
        await ctx.send("Stopped playback and disconnected.")

    @commands.command()
    @is_music_channel()
    async def queue(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or (player.queue.is_empty and not player.current):
            return await ctx.send("Queue is empty.")
            
        view = QueuePaginationView(player.queue.upcoming, player.current)
        await ctx.send(embed=view.get_embed(), view=view)

    @commands.command(aliases=["np"])
    @is_music_channel()
    async def nowplaying(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or not player.is_playing:
            return await ctx.send("Nothing is currently playing.")
        await self._send_now_playing(player, player.current, ctx.channel)

    @commands.command()
    @is_music_channel()
    async def volume(self, ctx, vol: int):
        if not 0 <= vol <= 100:
            return await ctx.send("Volume must be between 0 and 100.")
            
        player = await self._get_or_create_player(ctx)
        await player.set_volume(vol)
        await ctx.send(f"Volume set to {vol}%.")

    @commands.command()
    @is_music_channel()
    async def loop(self, ctx, mode: str = None):
        player = self._get_player(ctx.guild.id)
        if not player:
            return await ctx.send("Not connected.")
            
        if mode is None:
            if player.queue.loop_mode == LoopMode.OFF:
                player.queue.loop_mode = LoopMode.SONG
                await ctx.send("Loop mode: **Song**")
            elif player.queue.loop_mode == LoopMode.SONG:
                player.queue.loop_mode = LoopMode.QUEUE
                await ctx.send("Loop mode: **Queue**")
            else:
                player.queue.loop_mode = LoopMode.OFF
                await ctx.send("Loop mode: **Off**")
        else:
            mode = mode.lower()
            if mode in ["song", "track", "current"]:
                player.queue.loop_mode = LoopMode.SONG
                await ctx.send("Loop mode: **Song**")
            elif mode in ["queue", "all", "playlist"]:
                player.queue.loop_mode = LoopMode.QUEUE
                await ctx.send("Loop mode: **Queue**")
            elif mode in ["off", "none", "disable"]:
                player.queue.loop_mode = LoopMode.OFF
                await ctx.send("Loop mode: **Off**")
            else:
                await ctx.send("Invalid loop mode. Use `song`, `queue`, or `off`.")

    @commands.command()
    @is_music_channel()
    async def shuffle(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or player.queue.is_empty:
            return await ctx.send("Queue is empty.")
            
        player.queue.shuffle()
        await ctx.send("Queue shuffled.")

    @commands.command()
    @is_music_channel()
    async def previous(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player:
            return await ctx.send("Not connected.")
            
        prev_track = player.queue.get_previous()
        if not prev_track:
            return await ctx.send("No previous track in history.")
            
        # Put current track back in front of queue so we can return to it later
        if player.current:
            player.queue.add_next(player.current)
            
        player.queue.add_next(prev_track)
        await player.skip()

    @commands.command()
    @is_music_channel()
    async def remove(self, ctx, index: str):
        player = self._get_player(ctx.guild.id)
        if not player or player.queue.is_empty:
            return await ctx.send("Queue is empty.")
            
        if "-" in index:
            try:
                start, end = map(int, index.split("-"))
                removed = player.queue.remove_range(start, end)
                await ctx.send(f"Removed {len(removed)} tracks.")
            except ValueError:
                await ctx.send("Invalid range format. Use `start-end`.")
        else:
            try:
                idx = int(index)
                removed = player.queue.remove(idx)
                if removed:
                    await ctx.send(f"Removed: **{removed.title}**")
                else:
                    await ctx.send("Invalid index.")
            except ValueError:
                await ctx.send("Invalid index format.")

    @commands.command()
    @is_music_channel()
    async def playnext(self, ctx, *, query: str):
        player = await self._get_or_create_player(ctx)
        
        async with ctx.typing():
            track = await self.extractor.search(query, requested_by=ctx.author.display_name)
            if not track:
                return await ctx.send("No results found.")
                
            player.queue.add_next(track)
            await ctx.send(f"Added to front of queue: **{track.title}**")
            
            if not player.is_playing and not player.is_paused:
                next_track = player.queue.get_next(None)
                if next_track:
                    success = await player.play_track(next_track)
                    if success:
                        await self._send_now_playing(player, next_track, ctx.channel)

    @commands.command()
    @is_music_channel()
    async def search(self, ctx, *, query: str):
        player = await self._get_or_create_player(ctx)
        
        async with ctx.typing():
            tracks = await self.extractor.search_multiple(query, count=5, requested_by=ctx.author.display_name)
            
            if not tracks:
                return await ctx.send("No results found.")
                
            desc = "\n".join(f"{i}. [{t.title}]({t.url})" for i, t in enumerate(tracks, 1))
            embed = discord.Embed(title=f"Search results for: {query}", description=desc, color=0x00FF00)
            embed.set_footer(text="Reply with a number (1-5) or 'cancel' in 30 seconds.")
            
            await ctx.send(embed=embed)
            
            def check(m):
                return m.author == ctx.author and m.channel == ctx.channel
                
            try:
                msg = await self.bot.wait_for("message", timeout=30.0, check=check)
                if msg.content.lower() == "cancel":
                    return await ctx.send("Search cancelled.")
                    
                idx = int(msg.content) - 1
                if 0 <= idx < len(tracks):
                    track = tracks[idx]
                    player.queue.add(track)
                    await ctx.send(f"Added to queue: **{track.title}**")
                    
                    if not player.is_playing and not player.is_paused:
                        next_track = player.queue.get_next(None)
                        if next_track:
                            success = await player.play_track(next_track)
                            if success:
                                await self._send_now_playing(player, next_track, ctx.channel)
                else:
                    await ctx.send("Invalid number selection.")
            except asyncio.TimeoutError:
                await ctx.send("Search timed out.")
            except ValueError:
                await ctx.send("Invalid input. Please provide a number.")

    @commands.command()
    @is_music_channel()
    async def clear(self, ctx):
        player = self._get_player(ctx.guild.id)
        if player:
            player.queue.clear()
            await ctx.send("Queue cleared.")
        else:
            await ctx.send("Not connected.")

    @commands.command()
    @is_music_channel()
    async def history(self, ctx):
        player = self._get_player(ctx.guild.id)
        if not player or not player.queue.history:
            return await ctx.send("No play history.")
            
        history = player.queue.history
        desc = "\n".join(f"{i}. [{t.title}]({t.url})" for i, t in enumerate(history[:10], 1))
        embed = discord.Embed(title="Play History", description=desc, color=0x00FF00)
        await ctx.send(embed=embed)

    # Playlist commands
    @commands.group(invoke_without_command=True)
    @is_music_channel()
    async def playlist(self, ctx):
        await ctx.send("Available playlist commands: `!playlist create <name>`, `!playlist list`, `!playlist add <name> [url]`, `!playlist view <name>`, `!playlist delete <name>`, `!playlist savequeue <name>`")

    @playlist.command(name="create")
    @is_music_channel()
    async def playlist_create(self, ctx, name: str):
        if name in self.saved_playlists:
            return await ctx.send(f"Playlist `{name}` already exists.")
        self.saved_playlists[name] = []
        if self._save_playlists_atomic():
            await ctx.send(f"Created empty playlist `{name}`.")
        else:
            await ctx.send("Failed to save.")

    @playlist.command(name="savequeue")
    @is_music_channel()
    async def playlist_savequeue(self, ctx, name: str):
        player = self._get_player(ctx.guild.id)
        if not player:
            return await ctx.send("Nothing to save. Not connected.")

        tracks = []
        if player.current: 
            tracks.append(player.current.to_dict())
        for track in player.queue:
            tracks.append(track.to_dict())

        if not tracks:
            return await ctx.send("Queue is empty, nothing to save.")

        self.saved_playlists[name] = tracks
        if self._save_playlists_atomic():
            await ctx.send(f"Playlist `{name}` saved ({len(tracks)} tracks).")
        else:
            await ctx.send("Failed to save.")

    @playlist.command(name="list")
    @is_music_channel()
    async def playlist_list(self, ctx):
        if not self.saved_playlists:
            return await ctx.send("No saved playlists.")
        names = "\n".join(f"- {name} ({len(t)} tracks)" for name, t in self.saved_playlists.items())
        embed = discord.Embed(title="Saved Playlists", description=names, color=0x00FF00)
        await ctx.send(embed=embed)

    @playlist.command(name="add")
    @is_music_channel()
    async def playlist_add(self, ctx, name: str, *, query: str = None):
        if name not in self.saved_playlists:
            return await ctx.send(f"Playlist `{name}` not found. Use `!playlist create` first.")

        if not query:
            player = self._get_player(ctx.guild.id)
            if player and player.current:
                track = player.current
                self.saved_playlists[name].append(track.to_dict())
                if self._save_playlists_atomic():
                    return await ctx.send(f"Added **{track.title}** to `{name}`.")
            return await ctx.send("Please provide a song link, or play a song first to add it.")
        
        async with ctx.typing():
            track = await self.extractor.search(query, requested_by=ctx.author.display_name)
            if not track:
                return await ctx.send("Could not find any tracks.")

            self.saved_playlists[name].append(track.to_dict())
            if self._save_playlists_atomic():
                await ctx.send(f"Added **{track.title}** to playlist `{name}`.")
            else:
                await ctx.send("Failed to save.")

    @playlist.command(name="remove")
    @is_music_channel()
    async def playlist_remove(self, ctx, name: str, index: int):
        if name not in self.saved_playlists:
            return await ctx.send(f"Playlist `{name}` not found.")
        playlist = self.saved_playlists[name]
        if index < 1 or index > len(playlist):
            return await ctx.send("Invalid index.")

        removed = playlist.pop(index - 1)
        if self._save_playlists_atomic():
            await ctx.send(f"Removed: **{removed.get('title')}** from `{name}`.")

    @playlist.command(name="view")
    @is_music_channel()
    async def playlist_view(self, ctx, name: str):
        if name not in self.saved_playlists:
            return await ctx.send(f"Playlist `{name}` not found.")
        playlist = self.saved_playlists[name]
        if not playlist:
            return await ctx.send(f"Playlist `{name}` is empty.")

        MAX_QUEUE_PREVIEW = 20
        lines = [f"`{i}.` [{e.get('title', 'Unknown')}]({e.get('url', '')})" for i, e in enumerate(playlist[:MAX_QUEUE_PREVIEW], 1)]
        if len(playlist) > MAX_QUEUE_PREVIEW:
            lines.append(f"...and {len(playlist) - MAX_QUEUE_PREVIEW} more")

        embed = discord.Embed(title=f"Playlist: {name} ({len(playlist)} tracks)", description="\n".join(lines), color=0x00FF00)
        await ctx.send(embed=embed)

    @playlist.command(name="delete")
    @is_music_channel()
    async def playlist_delete(self, ctx, name: str):
        if name not in self.saved_playlists:
            return await ctx.send(f"Playlist `{name}` not found.")
        del self.saved_playlists[name]
        if self._save_playlists_atomic():
            await ctx.send(f"Deleted playlist `{name}`.")

async def setup(bot):
    await bot.add_cog(Music(bot))
