import asyncio
import json
import logging
import os
import tempfile
from typing import Any
from urllib.parse import urlparse

import discord
import wavelink
from discord.ext import commands

from utils.checks import is_music_channel

logger = logging.getLogger("bot")

PLAYLIST_FILE = "playlists.json"
MAX_PLAYLIST_ADD = 100
MAX_QUEUE_PREVIEW = 20


class LavalinkPlayer(wavelink.Player):
    async def _dispatch_voice_update(self) -> None:  # pragma: no cover
        # Wavelink currently sends session/token/endpoint only. Lavalink v4.2.1
        # on this setup requires channelId as well.
        assert self.guild is not None

        data = self._voice_state["voice"]
        session_id = data.get("session_id")
        token = data.get("token")
        endpoint = data.get("endpoint")
        channel_id = str(self.channel.id) if self.channel else None

        if not session_id or not token or not endpoint or not channel_id:
            return

        request = {
            "voice": {
                "sessionId": session_id,
                "token": token,
                "endpoint": endpoint,
                "channelId": channel_id,
            }
        }

        try:
            await self.node._update_player(self.guild.id, data=request)
        except wavelink.LavalinkException:
            await self.disconnect()
        else:
            self._connection_event.set()


def _track_title(track: wavelink.Playable) -> str:
    title = getattr(track, "title", None)
    return title or "Unknown title"


def _track_uri(track: wavelink.Playable) -> str:
    uri = getattr(track, "uri", None)
    if uri:
        return uri

    identifier = getattr(track, "identifier", None)
    if identifier:
        return f"https://www.youtube.com/watch?v={identifier}"

    return _track_title(track)


def _format_duration(milliseconds: int | None) -> str:
    if not milliseconds:
        return "Live"

    total_seconds = int(milliseconds / 1000)
    minutes, seconds = divmod(total_seconds, 60)
    hours, minutes = divmod(minutes, 60)

    if hours > 0:
        return f"{hours:02d}:{minutes:02d}:{seconds:02d}"

    return f"{minutes:02d}:{seconds:02d}"


class Music(commands.Cog):
    def __init__(self, bot: commands.Bot) -> None:
        self.bot = bot
        self.history: list[tuple[str, str]] = []
        self.volume = 50
        self.saved_playlists = self._load_playlists()

    def _load_playlists(self) -> dict[str, list[dict[str, str]]]:
        if not os.path.exists(PLAYLIST_FILE):
            return {}

        try:
            with open(PLAYLIST_FILE, "r", encoding="utf-8") as file:
                raw = json.load(file)
        except (json.JSONDecodeError, OSError) as exc:
            logger.warning("Failed to load %s: %s", PLAYLIST_FILE, exc)
            return {}

        normalized: dict[str, list[dict[str, str]]] = {}
        for name, entries in raw.items():
            parsed: list[dict[str, str]] = []

            if not isinstance(entries, list):
                continue

            for entry in entries:
                if isinstance(entry, dict):
                    title = str(entry.get("title", "")).strip()
                    url = str(entry.get("url", "")).strip()
                elif isinstance(entry, (list, tuple)) and len(entry) >= 2:
                    title = str(entry[0]).strip()
                    url = str(entry[1]).strip()
                else:
                    continue

                if not title and not url:
                    continue

                parsed.append({"title": title or url, "url": url or title})

            normalized[name] = parsed

        return normalized

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

    async def update_status(self, track_title: str | None = None) -> None:
        if track_title:
            activity = discord.Activity(type=discord.ActivityType.listening, name=track_title)
        else:
            activity = discord.Activity(type=discord.ActivityType.watching, name="for !help")

        await self.bot.change_presence(activity=activity)

    def _search_sources_for_query(self, query: str) -> tuple[wavelink.TrackSource | None, ...]:
        parsed = urlparse(query.strip())
        if parsed.scheme in {"http", "https"} and parsed.netloc:
            return (None,)

        return (wavelink.TrackSource.YouTubeMusic, wavelink.TrackSource.YouTube)

    async def _search_query(self, query: str) -> wavelink.Search:
        last_exception: Exception | None = None

        for source in self._search_sources_for_query(query):
            try:
                results = await wavelink.Playable.search(query, source=source)
            except Exception as exc:
                logger.warning("Search failed for query '%s' with source %s: %s", query, source, exc)
                last_exception = exc
                continue

            if isinstance(results, wavelink.Playlist):
                if results.tracks:
                    return results
                continue

            if results:
                return results

        if last_exception is not None:
            raise last_exception

        return []

    async def _resolve_track(self, query: str) -> wavelink.Playable | None:
        results = await self._search_query(query)

        if isinstance(results, wavelink.Playlist):
            if not results.tracks:
                return None
            return results.tracks[0]

        if not results:
            return None

        return results[0]

    async def _resolve_tracks(self, query: str) -> list[wavelink.Playable]:
        results = await self._search_query(query)

        if isinstance(results, wavelink.Playlist):
            return list(results.tracks[:MAX_PLAYLIST_ADD])

        if not results:
            return []

        return [results[0]]

    async def _ensure_node_connected(self) -> bool:
        connected_nodes = [
            node for node in wavelink.Pool.nodes.values()
            if node.status is wavelink.NodeStatus.CONNECTED
        ]
        if connected_nodes:
            return True

        uri = os.getenv("LAVALINK_URI", "http://127.0.0.1:2333")
        password = os.getenv("LAVALINK_PASSWORD", "youshallnotpass")

        try:
            if wavelink.Pool.nodes:
                await wavelink.Pool.close()

            node = wavelink.Node(uri=uri, password=password, retries=2)
            await wavelink.Pool.connect(nodes=[node], client=self.bot, cache_capacity=100)
        except Exception as exc:
            logger.warning("Unable to (re)connect Lavalink node: %s", exc)
            return False

        for _ in range(20):
            if any(
                node.status is wavelink.NodeStatus.CONNECTED
                for node in wavelink.Pool.nodes.values()
            ):
                return True

            await asyncio.sleep(0.25)

        logger.warning("Lavalink node did not reach CONNECTED state after reconnect.")
        return False

    async def _ensure_player(self, ctx: commands.Context) -> wavelink.Player | None:
        if not ctx.author.voice or not ctx.author.voice.channel:
            await ctx.send("You need to join a voice channel first.")
            return None

        if ctx.voice_client and not isinstance(ctx.voice_client, wavelink.Player):
            await ctx.send("Current voice client is not a Lavalink player.")
            return None

        if not await self._ensure_node_connected():
            await ctx.send("Lavalink node is not connected yet. Please try again in a few seconds.")
            return None

        voice_channel = ctx.author.voice.channel

        try:
            if not ctx.voice_client:
                player = await voice_channel.connect(cls=LavalinkPlayer, self_deaf=True)
            else:
                player = ctx.voice_client
                if player.channel and player.channel.id != voice_channel.id:
                    await player.move_to(voice_channel)
        except Exception as exc:
            logger.warning("Failed to connect voice/Lavalink player: %s", exc)
            await ctx.send("Could not connect to Lavalink player. Check Lavalink URI/password and node status.")
            return None

        assert isinstance(player, wavelink.Player)

        player.autoplay = wavelink.AutoPlayMode.partial
        await player.set_volume(self.volume)
        setattr(player, "text_channel_id", ctx.channel.id)

        return player

    async def _start_if_idle(self, player: wavelink.Player) -> bool:
        if player.playing or player.paused:
            return False

        if not player.queue:
            return False

        try:
            next_track = player.queue.get()
        except wavelink.QueueEmpty:
            return False

        await player.play(next_track, volume=self.volume)
        return True

    async def _get_bound_channel(self, player: wavelink.Player) -> discord.TextChannel | None:
        channel_id = getattr(player, "text_channel_id", None)
        if not channel_id:
            return None

        channel = self.bot.get_channel(channel_id)
        if isinstance(channel, discord.TextChannel):
            return channel

        return None

    async def _send_now_playing(self, player: wavelink.Player, track: wavelink.Playable) -> None:
        channel = await self._get_bound_channel(player)
        if channel is None:
            return

        duration_text = _format_duration(getattr(track, "length", 0))
        embed = discord.Embed(
            title="Now Playing",
            description=f"[{_track_title(track)}]({_track_uri(track)})",
            color=0x00FF00,
        )
        embed.set_footer(text=f"Duration: {duration_text}")

        artwork = getattr(track, "artwork", None)
        if artwork:
            embed.set_thumbnail(url=artwork)

        try:
            await channel.send(embed=embed)
        except discord.HTTPException:
            pass

    async def _enqueue_query(self, player: wavelink.Player, query: str) -> tuple[int, str]:
        results = await self._search_query(query)

        if isinstance(results, wavelink.Playlist):
            tracks = list(results.tracks[:MAX_PLAYLIST_ADD])
            if not tracks:
                raise ValueError("Playlist has no playable tracks.")
            await player.queue.put_wait(tracks)
            return len(tracks), _track_title(tracks[0])

        if not results:
            raise ValueError("No tracks found.")

        track = results[0]
        await player.queue.put_wait(track)
        return 1, _track_title(track)

    @commands.Cog.listener()
    async def on_wavelink_track_start(self, payload: wavelink.TrackStartEventPayload) -> None:
        if payload.player is None or payload.track is None:
            return

        player = payload.player
        track = payload.track

        title = _track_title(track)
        uri = _track_uri(track)

        self.history.insert(0, (title, uri))
        if len(self.history) > 20:
            self.history = self.history[:20]

        await self.update_status(title)
        await self._send_now_playing(player, track)

    @commands.Cog.listener()
    async def on_wavelink_track_exception(self, payload: wavelink.TrackExceptionEventPayload) -> None:
        player = payload.player
        if player is None:
            logger.warning("Track exception without active player: %s", payload.exception)
            return

        exception = payload.exception
        logger.warning("Track exception in guild %s: %s", player.guild.id if player.guild else "?", exception)

        channel = await self._get_bound_channel(player)
        if channel is not None:
            try:
                await channel.send("Playback failed for this track. Try another link or run !play again.")
            except discord.HTTPException:
                pass

    @commands.Cog.listener()
    async def on_wavelink_inactive_player(self, player: wavelink.Player) -> None:
        channel = await self._get_bound_channel(player)

        try:
            await player.disconnect()
        except Exception:
            pass

        await self.update_status(None)

        if channel is not None:
            try:
                await channel.send("Disconnected due to inactivity.")
            except discord.HTTPException:
                pass

    @commands.command()
    @is_music_channel()
    async def join(self, ctx: commands.Context) -> None:
        player = await self._ensure_player(ctx)
        if player is None:
            return

        await ctx.send(f"Connected to {player.channel.mention}.")

    @commands.command()
    @is_music_channel()
    async def play(self, ctx: commands.Context, *, query: str) -> None:
        player = await self._ensure_player(ctx)
        if player is None:
            return

        async with ctx.typing():
            try:
                added, first_title = await self._enqueue_query(player, query)
            except Exception as exc:
                await ctx.send(f"Could not load track: {exc}")
                return

            started = await self._start_if_idle(player)

        if added == 1:
            if started:
                await ctx.send(f"Queued and started: **{first_title}**")
            else:
                await ctx.send(f"Added to queue: **{first_title}**")
            return

        if started:
            await ctx.send(f"Added {added} tracks from playlist. Playback started.")
        else:
            await ctx.send(f"Added {added} tracks from playlist to queue.")

    @commands.command()
    @is_music_channel()
    async def queue(self, ctx: commands.Context) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player):
            await ctx.send("Queue is empty.")
            return

        current = player.current
        upcoming = list(player.queue)

        if current is None and not upcoming:
            await ctx.send("Queue is empty.")
            return

        embed = discord.Embed(title="Music Queue", color=0x00FF00)
        lines: list[str] = []

        if current is not None:
            lines.append(f"Now: [{_track_title(current)}]({_track_uri(current)})")
            lines.append("")

        if upcoming:
            lines.append("Up next:")
            for index, track in enumerate(upcoming[:MAX_QUEUE_PREVIEW], start=1):
                lines.append(f"{index}. [{_track_title(track)}]({_track_uri(track)})")
            if len(upcoming) > MAX_QUEUE_PREVIEW:
                lines.append(f"...and {len(upcoming) - MAX_QUEUE_PREVIEW} more")

        embed.description = "\n".join(lines)
        await ctx.send(embed=embed)

    @commands.command()
    @is_music_channel()
    async def search(self, ctx: commands.Context, *, query: str) -> None:
        results = await self._search_query(query)
        tracks = list(results.tracks) if isinstance(results, wavelink.Playlist) else list(results)
        tracks = tracks[:5]

        if not tracks:
            await ctx.send("No results found.")
            return

        description = "\n".join(
            f"{index}. [{_track_title(track)}]({_track_uri(track)})"
            for index, track in enumerate(tracks, start=1)
        )
        embed = discord.Embed(
            title=f"Search results for: {query}",
            description=description,
            color=0x00FF00,
        )
        embed.set_footer(text="Reply with a number (1-5) or 'cancel' in 30 seconds.")
        await ctx.send(embed=embed)

        def _check(message: discord.Message) -> bool:
            return message.author == ctx.author and message.channel == ctx.channel

        try:
            message = await self.bot.wait_for("message", timeout=30, check=_check)
        except asyncio.TimeoutError:
            await ctx.send("Timed out.")
            return

        content = message.content.strip().lower()
        if content == "cancel":
            await ctx.send("Cancelled.")
            return

        if not content.isdigit():
            await ctx.send("Invalid choice.")
            return

        selected_index = int(content) - 1
        if selected_index < 0 or selected_index >= len(tracks):
            await ctx.send("Invalid selection.")
            return

        player = await self._ensure_player(ctx)
        if player is None:
            return

        selected_track = tracks[selected_index]
        await player.queue.put_wait(selected_track)
        started = await self._start_if_idle(player)

        if started:
            await ctx.send(f"Queued and started: **{_track_title(selected_track)}**")
        else:
            await ctx.send(f"Added to queue: **{_track_title(selected_track)}**")

    @commands.command()
    @is_music_channel()
    async def remove(self, ctx: commands.Context, index: int) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player) or not player.queue:
            await ctx.send("Queue is empty.")
            return

        if index < 1 or index > len(player.queue):
            await ctx.send("Invalid queue index.")
            return

        removed = player.queue[index - 1]
        player.queue.delete(index - 1)
        await ctx.send(f"Removed: **{_track_title(removed)}**")

    @commands.command()
    @is_music_channel()
    async def clear(self, ctx: commands.Context) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player):
            await ctx.send("Queue is already empty.")
            return

        player.queue.clear()
        player.auto_queue.clear()
        await ctx.send("Queue cleared.")

    @commands.command()
    @is_music_channel()
    async def shuffle(self, ctx: commands.Context) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player) or len(player.queue) < 2:
            await ctx.send("Need at least 2 tracks in queue to shuffle.")
            return

        player.queue.shuffle()
        await ctx.send("Queue shuffled.")

    @commands.command()
    @is_music_channel()
    async def skip(self, ctx: commands.Context) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player) or not player.playing:
            await ctx.send("Nothing is currently playing.")
            return

        await player.skip(force=True)
        await ctx.send("Skipped.")

    @commands.command()
    @is_music_channel()
    async def stop(self, ctx: commands.Context) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player):
            await ctx.send("Not connected.")
            return

        player.queue.clear()
        player.auto_queue.clear()
        player.queue.mode = wavelink.QueueMode.normal
        await player.disconnect()
        await self.update_status(None)
        await ctx.send("Stopped and disconnected.")

    @commands.command()
    @is_music_channel()
    async def volume(self, ctx: commands.Context, vol: int) -> None:
        if vol < 0 or vol > 100:
            await ctx.send("Volume must be between 0 and 100.")
            return

        self.volume = vol

        player = ctx.voice_client
        if isinstance(player, wavelink.Player):
            await player.set_volume(vol)

        await ctx.send(f"Volume set to {vol}%.")

    @commands.command()
    @is_music_channel()
    async def history(self, ctx: commands.Context) -> None:
        if not self.history:
            await ctx.send("No playback history yet.")
            return

        lines = [
            f"{index}. [{title}]({url})"
            for index, (title, url) in enumerate(self.history[:10], start=1)
        ]
        embed = discord.Embed(title="Playback History", description="\n".join(lines), color=0x00FF00)
        await ctx.send(embed=embed)

    @commands.command()
    @is_music_channel()
    async def previous(self, ctx: commands.Context) -> None:
        if len(self.history) < 2:
            await ctx.send("No previous song available.")
            return

        previous_url = self.history[1][1]
        player = await self._ensure_player(ctx)
        if player is None:
            return

        track = await self._resolve_track(previous_url)
        if track is None:
            await ctx.send("Could not load previous track.")
            return

        player.queue.put_at(0, track)

        if player.playing:
            await player.skip(force=True)
        else:
            await self._start_if_idle(player)

        await ctx.send(f"Replaying previous track: **{_track_title(track)}**")

    @commands.command()
    @is_music_channel()
    async def loop(self, ctx: commands.Context, mode: str | None = None) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player):
            await ctx.send("Connect and play a track first.")
            return

        if mode is None:
            mode = "song" if player.queue.mode is wavelink.QueueMode.normal else "off"

        mode = mode.lower().strip()
        if mode not in {"song", "playlist", "off"}:
            await ctx.send("Invalid mode. Use: song, playlist, off.")
            return

        if mode == "song":
            player.queue.mode = wavelink.QueueMode.loop
            await ctx.send("Loop mode set to: current song.")
        elif mode == "playlist":
            player.queue.mode = wavelink.QueueMode.loop_all
            await ctx.send("Loop mode set to: entire queue.")
        else:
            player.queue.mode = wavelink.QueueMode.normal
            await ctx.send("Loop mode disabled.")

    def _serialize_track(self, track: wavelink.Playable) -> dict[str, str]:
        return {"title": _track_title(track), "url": _track_uri(track)}

    @commands.command()
    @is_music_channel()
    async def playlist_save(self, ctx: commands.Context, name: str) -> None:
        player = ctx.voice_client
        if not isinstance(player, wavelink.Player):
            await ctx.send("Nothing to save. Not connected.")
            return

        tracks: list[wavelink.Playable] = []
        if player.current is not None:
            tracks.append(player.current)
        tracks.extend(list(player.queue))

        if not tracks:
            await ctx.send("Queue is empty, nothing to save.")
            return

        self.saved_playlists[name] = [self._serialize_track(track) for track in tracks]
        if self._save_playlists_atomic():
            await ctx.send(f"Playlist '{name}' saved ({len(tracks)} tracks).")
        else:
            await ctx.send("Failed to save playlist file.")

    @commands.command()
    @is_music_channel()
    async def playlist_load(self, ctx: commands.Context, name: str) -> None:
        if name not in self.saved_playlists:
            await ctx.send("Playlist not found.")
            return

        entries = self.saved_playlists[name]
        player = await self._ensure_player(ctx)
        if player is None:
            return

        added = 0
        failed = 0

        async with ctx.typing():
            for entry in entries[:MAX_PLAYLIST_ADD]:
                query = (entry.get("url") or "").strip() or (entry.get("title") or "").strip()
                if not query:
                    failed += 1
                    continue

                track = await self._resolve_track(query)
                if track is None:
                    failed += 1
                    continue

                await player.queue.put_wait(track)
                added += 1

            started = await self._start_if_idle(player)

        if added == 0:
            await ctx.send("No tracks could be loaded from this playlist.")
            return

        suffix = " Playback started." if started else ""
        if failed:
            await ctx.send(f"Loaded playlist '{name}': {added} added, {failed} failed.{suffix}")
        else:
            await ctx.send(f"Loaded playlist '{name}' with {added} tracks.{suffix}")

    @commands.command()
    @is_music_channel()
    async def playlist_list(self, ctx: commands.Context) -> None:
        if not self.saved_playlists:
            await ctx.send("No saved playlists.")
            return

        names = "\n".join(f"- {name}" for name in sorted(self.saved_playlists))
        await ctx.send(f"Saved playlists:\n{names}")

    @commands.command()
    @is_music_channel()
    async def playlist_add(self, ctx: commands.Context, name: str, *, query: str) -> None:
        if name not in self.saved_playlists:
            await ctx.send(f"Playlist '{name}' not found. Use !playlist_list.")
            return

        tracks = await self._resolve_tracks(query)
        if not tracks:
            await ctx.send("Could not find any tracks for this query.")
            return

        entries = self.saved_playlists[name]
        entries.extend(self._serialize_track(track) for track in tracks)

        if self._save_playlists_atomic():
            await ctx.send(f"Added {len(tracks)} track(s) to playlist '{name}'.")
        else:
            await ctx.send("Failed to save playlist file.")

    @commands.command()
    @is_music_channel()
    async def playlist_remove(self, ctx: commands.Context, name: str, index: int) -> None:
        if name not in self.saved_playlists:
            await ctx.send(f"Playlist '{name}' not found.")
            return

        playlist = self.saved_playlists[name]
        if index < 1 or index > len(playlist):
            await ctx.send("Invalid index.")
            return

        removed = playlist.pop(index - 1)
        if self._save_playlists_atomic():
            await ctx.send(f"Removed: **{removed.get('title', 'Unknown track')}**")
        else:
            await ctx.send("Failed to save playlist file.")

    @commands.command()
    @is_music_channel()
    async def playlist_view(self, ctx: commands.Context, name: str) -> None:
        if name not in self.saved_playlists:
            await ctx.send(f"Playlist '{name}' not found.")
            return

        playlist = self.saved_playlists[name]
        if not playlist:
            await ctx.send(f"Playlist '{name}' is empty.")
            return

        lines = []
        for index, entry in enumerate(playlist[:MAX_QUEUE_PREVIEW], start=1):
            title = entry.get("title", "Unknown title")
            url = entry.get("url", "")
            if url:
                lines.append(f"{index}. [{title}]({url})")
            else:
                lines.append(f"{index}. {title}")

        if len(playlist) > MAX_QUEUE_PREVIEW:
            lines.append(f"...and {len(playlist) - MAX_QUEUE_PREVIEW} more")

        embed = discord.Embed(
            title=f"Playlist: {name} ({len(playlist)} tracks)",
            description="\n".join(lines),
            color=0x00FF00,
        )
        await ctx.send(embed=embed)

    @commands.command()
    @is_music_channel()
    async def playlist_delete(self, ctx: commands.Context, name: str) -> None:
        if name not in self.saved_playlists:
            await ctx.send(f"Playlist '{name}' not found.")
            return

        del self.saved_playlists[name]
        if self._save_playlists_atomic():
            await ctx.send(f"Deleted playlist '{name}'.")
        else:
            await ctx.send("Failed to save playlist file.")

    @commands.Cog.listener()
    async def on_voice_state_update(
        self,
        member: discord.Member,
        before: discord.VoiceState,
        after: discord.VoiceState,
    ) -> None:
        if member.bot:
            return

        voice_client: Any = member.guild.voice_client
        if not isinstance(voice_client, wavelink.Player) or not voice_client.connected:
            return

        channel = voice_client.channel
        if channel is None:
            return

        human_members = [m for m in channel.members if not m.bot]
        if human_members:
            return

        async def delayed_disconnect() -> None:
            await asyncio.sleep(300)

            current_vc: Any = member.guild.voice_client
            if not isinstance(current_vc, wavelink.Player) or not current_vc.connected:
                return

            current_channel = current_vc.channel
            if current_channel is None:
                return

            still_humans = [m for m in current_channel.members if not m.bot]
            if still_humans:
                return

            current_vc.queue.clear()
            current_vc.auto_queue.clear()
            try:
                await current_vc.disconnect()
            except Exception:
                return

            text_channel = await self._get_bound_channel(current_vc)
            if text_channel is not None:
                try:
                    await text_channel.send("Disconnected because the bot was alone for 5 minutes.")
                except discord.HTTPException:
                    pass

            await self.update_status(None)

        asyncio.create_task(delayed_disconnect())

