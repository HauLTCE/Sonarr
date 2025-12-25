import discord
from discord.ext import commands
import asyncio
import time
import json
import os
import random
import logging
import tempfile
import shutil
from utils.ytdl import YTDLSource
from utils.checks import is_music_channel
from utils.music_queue import LazyMusicQueue

logger = logging.getLogger("bot")

PLAYLIST_FILE = "playlists.json"

class QueueView(discord.ui.View):
    def __init__(self, ctx, queue, page=0):
        super().__init__(timeout=None)
        self.ctx = ctx
        self.queue = queue
        self.page = page
        self.items_per_page = 10
        self.total_pages = (len(queue) - 1) // self.items_per_page + 1

    async def build_embed(self, page=0):
        start = page * self.items_per_page
        end = start + self.items_per_page
        current_items = self.queue[start:end]

        desc = ""
        for i, item in enumerate(current_items, start=start + 1):
            # Handle both lazy items and old tuple format
            title = item.title if hasattr(item, 'title') else item[0]
            url = item.url if hasattr(item, 'url') else item[1]
            desc += f"`{i}.` [{title}]({url})\n"

        embed = discord.Embed(title=f"🎵 Music Queue (Page {page + 1}/{self.total_pages})", description=desc, color=0x00ff00)
        return embed

class Music(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.music_queue = LazyMusicQueue()  # Tier 2: Lazy-loaded queue for 500-1000ms faster operations
        self.history = []
        self.loop_mode = 'off'
        self.loop_playlist_backup = []
        self.current_track = None
        self.volume = 0.5
        
        if os.path.exists(PLAYLIST_FILE):
            try:
                with open(PLAYLIST_FILE, "r") as f:
                    self.saved_playlists = json.load(f)
            except (json.JSONDecodeError, IOError) as e:
                logger.warning(f"Failed to load playlists.json: {e}. Initializing empty playlists.")
                self.saved_playlists = {}
                # Reset the file
                try:
                    with open(PLAYLIST_FILE, "w") as f:
                        json.dump({}, f)
                except Exception as reset_error:
                    logger.error(f"Failed to reset playlists.json: {reset_error}")
        else:
            self.saved_playlists = {}

    async def update_status(self):
        if self.current_track:
            await self.bot.change_presence(activity=discord.Activity(type=discord.ActivityType.listening, name=self.current_track[0]))
        else:
            await self.bot.change_presence(activity=discord.Activity(type=discord.ActivityType.watching, name="for !help"))
    def _save_playlists_atomic(self):
        """Save playlists with atomic writes to prevent corruption."""
        import tempfile
        try:
            # Write to temp file first
            temp_fd, temp_path = tempfile.mkstemp(suffix='.json', dir='.')
            with open(temp_fd, 'w') as f:
                json.dump(self.saved_playlists, f, indent=2)
            
            # Atomic rename (replaces old file)
            import shutil
            shutil.move(temp_path, PLAYLIST_FILE)
            logger.debug(f"[Music] Playlists saved atomically")
            return True
        except Exception as e:
            logger.error(f"[Music] Failed to save playlists: {e}")
            try:
                import os
                if os.path.exists(temp_path):
                    os.remove(temp_path)
            except:
                pass
            return False
    async def start_playing_async(self, ctx):
        if not ctx.voice_client or not ctx.voice_client.is_connected():
            return

        if self.loop_mode == 'song' and self.current_track:
            item = self.current_track if not hasattr(self.current_track, 'title') else self.current_track
            title = item[0] if isinstance(item, tuple) else item.title
            url = item[1] if isinstance(item, tuple) else item.url
        elif len(self.music_queue) > 0:
            item = self.music_queue.pop(0)
            if item is None:
                return
            title = item.title if hasattr(item, 'title') else item[0]
            url = item.url if hasattr(item, 'url') else item[1]
            self.current_track = (title, url)
        else:
            return

        self.history.insert(0, self.current_track)
        if len(self.history) > 20: self.history.pop()
        
        await self.update_status()

        try:
            player = await YTDLSource.from_url(url, loop=self.bot.loop, stream=True)
            player.volume = self.volume
            
            ctx.voice_client.play(player, after=lambda e: self.play_next(ctx))
            
            embed = discord.Embed(title="Now Playing 🎵", description=f"[{player.title}]({url})", color=0x00ff00)
            if 'thumbnail' in player.data:
                embed.set_thumbnail(url=player.data['thumbnail'])
            if 'duration' in player.data:
                m, s = divmod(player.data['duration'], 60)
                embed.set_footer(text=f"Duration: {int(m)}m {int(s)}s")
            
            await ctx.send(embed=embed)

        except Exception as e:
            logger.exception(f"Async Play failed: {e}")
            await ctx.send(f"⚠️ Error playing song: {e}")
            await self.start_playing_async(ctx)

    def play_next(self, ctx):
        if not ctx.voice_client or not ctx.voice_client.is_connected():
            return
            
        asyncio.run_coroutine_threadsafe(self.process_next_song(ctx), self.bot.loop)

    async def process_next_song(self, ctx):
        should_announce = True

        if self.loop_mode == 'song' and self.current_track:
            title, url = self.current_track
            should_announce = False
        elif len(self.music_queue) > 0:
            item = self.music_queue.pop(0)
            if item is None:
                await self.process_next_song(ctx)
                return
            title = item.title if hasattr(item, 'title') else item[0]
            url = item.url if hasattr(item, 'url') else item[1]
            self.current_track = (title, url)
            
            self.history.insert(0, self.current_track)
            if len(self.history) > 20: self.history.pop()
        else:
            if self.loop_mode == 'playlist' and self.loop_playlist_backup:
                # Reinitialize queue with backup items
                self.music_queue = LazyMusicQueue()
                for title, url in self.loop_playlist_backup:
                    self.music_queue.add(title, url)
                
                item = self.music_queue.pop(0)
                title = item.title
                url = item.url
                self.current_track = (title, url)
                self.history.insert(0, self.current_track)
                if len(self.history) > 20: self.history.pop()
            else:
                self.current_track = None
                await self.update_status()
                self.bot.loop.create_task(self.disconnect_timer(ctx))
                return

        await self.update_status()

        try:
            player = await YTDLSource.from_url(url, loop=self.bot.loop, stream=True)
            player.volume = self.volume
            
            ctx.voice_client.play(player, after=lambda e: self.play_next(ctx))
            
            if should_announce:
                embed = discord.Embed(title="Now Playing 🎵", description=f"[{player.title}]({url})", color=0x00ff00)
                if 'thumbnail' in player.data:
                     embed.set_thumbnail(url=player.data.get('thumbnail'))
                await ctx.send(embed=embed)

        except Exception as e:
            logger.exception(f"Play_next failed: {e}")
            await self.process_next_song(ctx)

    async def disconnect_timer(self, ctx):
        await asyncio.sleep(300)
        if ctx.voice_client and ctx.voice_client.is_connected() and not ctx.voice_client.is_playing() and len(self.music_queue) == 0:
            self.music_queue.clear()
            self.loop_mode = 'off'
            self.current_track = None
            await ctx.voice_client.disconnect()
            await ctx.send("🛑 Stopped and disconnected due to inactivity.", delete_after=30)
            await self.update_status()

    @commands.command()
    @is_music_channel()
    async def join(self, ctx):
        """Connect to the voice channel you are currently in."""
        if not ctx.message.author.voice:
            await ctx.send("You are not connected to a voice channel.")
            return
        channel = ctx.message.author.voice.channel
        await channel.connect()

    @commands.command()
    @is_music_channel()
    async def play(self, ctx, *, query):
        """Play a song from a YouTube URL or search query."""
        if not ctx.voice_client:
            await ctx.invoke(self.join)

        async with ctx.typing():
            try:
                info = await YTDLSource.get_info(query, loop=self.bot.loop)

                if isinstance(info, list):
                    MAX_PLAYLIST_ADD = 100
                    count = 0
                    for entry in info[:MAX_PLAYLIST_ADD]:
                        if not entry or not entry.get('url'):
                            continue
                        # Tier 2: Add via lazy queue (no metadata loading yet)
                        self.music_queue.add(entry.get('title', 'Unknown'), entry.get('url'))
                        count += 1

                    truncated = len(info) > MAX_PLAYLIST_ADD

                    if ctx.voice_client.is_playing():
                        msg = f'✅ Added playlist/search results to queue ({count} tracks)'
                        if truncated:
                            msg += " (truncated to 100 tracks)"
                        await ctx.send(msg)
                    else:
                        msg = f'✅ Added playlist/search results to queue ({count} tracks). Starting playback...'
                        if truncated:
                            msg += " (truncated to 100 tracks)"
                        await ctx.send(msg)
                        await self.start_playing_async(ctx)
                else:
                    url = info['url']
                    title = info['title']

                    # Tier 2: Add via lazy queue (no metadata loading yet)
                    self.music_queue.add(title, url)

                    if ctx.voice_client.is_playing():
                        await ctx.send(f'✅ Added to queue: **{title}** (Position {len(self.music_queue)})')
                    else:
                        await self.start_playing_async(ctx)

            except Exception as e:
                await ctx.send(f"❌ Could not find/load song: {e}")

    @commands.command()
    @is_music_channel()
    async def queue(self, ctx):
        """Display the current music queue."""
        if len(self.music_queue) == 0:
            await ctx.send("The queue is empty.")
            return

        view = QueueView(ctx, self.music_queue)
        embed = await view.build_embed(page=0)
        await ctx.send(embed=embed)

    @commands.command()
    @is_music_channel()
    async def search(self, ctx, *, query):
        """Search YouTube and select a song to add to the queue."""
        await ctx.trigger_typing()
        try:
            results = await YTDLSource.search(query, limit=5, loop=self.bot.loop)
            if not results:
                await ctx.send("No results found.")
                return

            desc = ""
            for i, r in enumerate(results, start=1):
                desc += f"`{i}.` [{r['title']}]({r['url']})\n"

            embed = discord.Embed(title=f"🔎 Search results for: {query}", description=desc, color=0x00ff00)
            embed.set_footer(text="Reply with a number (1-5) to add to queue, or 'cancel' (30s).")
            await ctx.send(embed=embed)

            def _check(m):
                return m.author == ctx.author and m.channel == ctx.channel

            try:
                resp = await self.bot.wait_for('message', check=_check, timeout=30)
            except asyncio.TimeoutError:
                await ctx.send("⏳ Timeout. Aborting.")
                return

            choice = resp.content.strip()
            if choice.lower() == 'cancel':
                await ctx.send("Cancelled.")
                return

            if not choice.isdigit():
                await ctx.send("❌ Invalid choice.")
                return

            idx = int(choice)
            if idx < 1 or idx > len(results):
                await ctx.send("❌ Invalid selection.")
                return

            selected = results[idx - 1]
            # Tier 2: Add via lazy queue (no metadata loading yet)
            self.music_queue.add(selected['title'], selected['url'])
            await ctx.send(f"✅ Added to queue: **{selected['title']}** (Position {len(self.music_queue)})")

            if not ctx.voice_client or not ctx.voice_client.is_playing():
                await self.start_playing_async(ctx)

        except Exception as e:
            await ctx.send(f"❌ Search failed: {e}")

    @commands.command()
    @is_music_channel()
    async def remove(self, ctx, index: int):
        """Remove a song from the queue by its index."""
        if 1 <= index <= len(self.music_queue):
            removed = self.music_queue.pop(index - 1)
            await ctx.send(f"🗑️ Removed: **{removed[0]}**")
        else:
            await ctx.send("❌ Invalid index.")

    @commands.command()
    @is_music_channel()
    async def clear(self, ctx):
        """Clear the entire music queue."""
        self.music_queue.clear()
        await ctx.send("🧹 Queue cleared.")

    @commands.command()
    @is_music_channel()
    async def shuffle(self, ctx):
        """Shuffle the current music queue."""
        random.shuffle(self.music_queue)
        await ctx.send("🔀 Queue shuffled.")

    @commands.command()
    @is_music_channel()
    async def skip(self, ctx):
        """Skip the current playing song."""
        if ctx.voice_client and ctx.voice_client.is_playing():
            ctx.voice_client.stop()
            await ctx.send("⏭️ Skipped.")

    @commands.command()
    @is_music_channel()
    async def stop(self, ctx):
        """Stop playback, clear queue, and disconnect the bot."""
        self.music_queue.clear()
        self.loop_mode = 'off'
        self.loop_playlist_backup = []
        self.current_track = None
        if ctx.voice_client:
            await ctx.voice_client.disconnect()
            await ctx.send("🛑 Stopped and disconnected.")
        await self.update_status()

    @commands.command()
    @is_music_channel()
    async def volume(self, ctx, vol: int):
        """Set the player volume (0-100)."""
        if 0 <= vol <= 100:
            self.volume = vol / 100
            if ctx.voice_client and ctx.voice_client.source:
                ctx.voice_client.source.volume = self.volume
            await ctx.send(f"🔊 Volume set to **{vol}%**")
        else:
            await ctx.send("Please choose 0-100.")

    @commands.command()
    @is_music_channel()
    async def history(self, ctx):
        """Show the last 10 played songs."""
        if not self.history:
            await ctx.send("📜 No history yet.")
            return
        desc = ""
        for i, (title, url) in enumerate(self.history[:10], start=1):
            desc += f"`{i}.` [{title}]({url})\n"
        embed = discord.Embed(title="📜 Song History", description=desc, color=0x00ff00)
        await ctx.send(embed=embed)

    @commands.command()
    @is_music_channel()
    async def previous(self, ctx):
        """Play the previously played song again."""
        if not self.history:
            await ctx.send("❌ No previous song found.")
            return
        last_track = self.history.pop(0)
        self.music_queue.insert(0, last_track)
        if ctx.voice_client and ctx.voice_client.is_playing():
            ctx.voice_client.stop()
            await ctx.send("⏮️ Replaying previous song...")
        else:
            await self.start_playing_async(ctx)

    @commands.command()
    @is_music_channel()
    async def loop(self, ctx, mode: str = None):
        """Set loop mode: 'song', 'playlist', or 'off'. No args toggles song loop."""
        if mode is None:
            if self.loop_mode == 'off':
                mode = 'song'
            else:
                mode = 'off'
        
        mode = mode.lower()
        if mode not in ("song", "playlist", "off"):
            await ctx.send("❌ Invalid mode. Choose `song`, `playlist`, or `off`.")
            return

        self.loop_mode = mode
        if mode == 'playlist':
            backup = list(self.music_queue)
            if self.current_track:
                backup.insert(0, self.current_track)
            self.loop_playlist_backup = backup
            await ctx.send("🔁 Looping playlist enabled.")
        elif mode == 'song':
            if not self.current_track:
                await ctx.send("❌ No song currently playing. Start a song first.")
                self.loop_mode = 'off'
                return
            await ctx.send("🔂 Looping current song.")
        else:
            self.loop_playlist_backup = []
            await ctx.send("⛔ Looping disabled.")

    @commands.command()
    @is_music_channel()
    async def playlist_save(self, ctx, name: str):
        """Save the current queue as a playlist."""
        if not self.music_queue:
            await ctx.send("❌ Queue is empty, nothing to save.")
            return
        # Serialize LazyQueueItems to tuples for JSON compatibility
        serialized_queue = [
            (item.title, item.url) if hasattr(item, 'title') else item
            for item in self.music_queue.items
        ]
        self.saved_playlists[name] = serialized_queue
        
        # Use atomic write to prevent corruption
        if self._save_playlists_atomic():
            await ctx.send(f"💾 Playlist **{name}** saved ({len(serialized_queue)} songs).")
        else:
            await ctx.send(f"❌ Failed to save playlist **{name}**.")

    @commands.command()
    @is_music_channel()
    async def playlist_load(self, ctx, name: str):
        """Load a saved playlist into the queue."""
        if name not in self.saved_playlists:
            await ctx.send("❌ Playlist not found.", delete_after=5)
            return
        loaded = self.saved_playlists[name]
        # Tier 2: Use lazy queue extend
        self.music_queue.extend([tuple(x) for x in loaded])
        await ctx.send(f"📂 Loaded playlist **{name}** ({len(loaded)} songs added).")
        if not ctx.voice_client or not ctx.voice_client.is_playing():
            if not ctx.voice_client:
                await ctx.invoke(self.join)
            await self.start_playing_async(ctx)

    @commands.command()
    @is_music_channel()
    async def playlist_list(self, ctx):
        """List all saved playlists."""
        if not self.saved_playlists:
            await ctx.send("No saved playlists.")
            return
        msg = "📂 **Saved Playlists:**\n" + "\n".join(f"- {name}" for name in self.saved_playlists)
        await ctx.send(msg)

    @commands.Cog.listener()
    async def on_voice_state_update(self, member, before, after):
        voice_client = member.guild.voice_client
        if not voice_client or not voice_client.is_connected():
            return
        if voice_client.channel:
            members = voice_client.channel.members
            if len(members) == 1 and members[0] == self.bot.user:
                await asyncio.sleep(300)
                if voice_client.is_connected() and len(voice_client.channel.members) == 1:
                    self.music_queue.clear()
                    self.loop_mode = 'off'
                    self.loop_playlist_backup = []
                    self.current_track = None

                    await voice_client.disconnect()

                    guild_id = str(voice_client.guild.id)
                    cfg = self.bot.server_config.get(guild_id, {})
                    ch_id = cfg.get("music_channel") or cfg.get("general_channel")
                    if ch_id:
                        ch = self.bot.get_channel(ch_id)
                        if ch:
                            try:
                                await ch.send("🛑 Stopped and disconnected because I was alone for 5 minutes.")
                            except Exception:
                                pass

                    await self.update_status()
                    logger.info("Left voice channel because I was alone for 5 minutes.")

async def setup(bot):
    await bot.add_cog(Music(bot))