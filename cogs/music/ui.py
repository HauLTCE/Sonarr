import math
import discord
import wavelink

def _format_duration(seconds: int | float) -> str:
    """Format seconds (or milliseconds if very large) as MM:SS or HH:MM:SS."""
    # Wavelink lengths are in milliseconds
    if seconds > 10000:
        seconds = seconds // 1000
    if seconds <= 0:
        return "Live"
    m, s = divmod(int(seconds), 60)
    h, m = divmod(m, 60)
    if h > 0:
        return f"{h:02d}:{m:02d}:{s:02d}"
    return f"{m:02d}:{s:02d}"

class PromptView(discord.ui.View):
    def __init__(self, on_yes, on_no):
        super().__init__(timeout=60)
        self.on_yes_callback = on_yes
        self.on_no_callback = on_no

    @discord.ui.button(label="Yes", style=discord.ButtonStyle.success)
    async def yes_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        for child in self.children: child.disabled = True
        await interaction.response.edit_message(view=self)
        await self.on_yes_callback(interaction)
        self.stop()

    @discord.ui.button(label="No", style=discord.ButtonStyle.danger)
    async def no_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        for child in self.children: child.disabled = True
        await interaction.response.edit_message(view=self)
        await self.on_no_callback(interaction)
        self.stop()

class PlayerControlView(discord.ui.View):
    def __init__(self, cog, player: wavelink.Player, track: wavelink.Playable):
        super().__init__(timeout=None)
        self.cog = cog
        self.player = player
        self.track = track

    @discord.ui.button(emoji="⏸️", style=discord.ButtonStyle.primary, custom_id="music_play_pause", row=0)
    async def play_pause(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.player.connected:
            return await interaction.response.send_message("Player disconnected.", ephemeral=True)
        if self.player.paused:
            await self.player.pause(False)
            button.emoji = "⏸️"
        else:
            await self.player.pause(True)
            button.emoji = "▶️"
        await interaction.response.edit_message(view=self)

    @discord.ui.button(emoji="⏭️", style=discord.ButtonStyle.secondary, custom_id="music_skip", row=0)
    async def skip(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.player.connected:
            return await interaction.response.send_message("Player disconnected.", ephemeral=True)
        await self.player.skip(force=True)
        await interaction.response.send_message("Skipped.", ephemeral=True, delete_after=5)

    @discord.ui.button(emoji="🔁", style=discord.ButtonStyle.secondary, custom_id="music_loop", row=0)
    async def loop(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.player.connected:
            return await interaction.response.send_message("Player disconnected.", ephemeral=True)
        queue = self.player.queue
        if queue.mode == wavelink.QueueMode.normal:
            queue.mode = wavelink.QueueMode.loop
            button.emoji = "🔂"
            button.style = discord.ButtonStyle.primary
            msg = "Looping current song."
        elif queue.mode == wavelink.QueueMode.loop:
            queue.mode = wavelink.QueueMode.loop_all
            button.emoji = "🔁"
            button.style = discord.ButtonStyle.primary
            msg = "Looping entire queue."
        else:
            queue.mode = wavelink.QueueMode.normal
            button.emoji = "🔁"
            button.style = discord.ButtonStyle.secondary
            msg = "Looping disabled."
        await interaction.response.edit_message(view=self)
        await interaction.followup.send(msg, ephemeral=True)

    @discord.ui.button(emoji="📜", style=discord.ButtonStyle.secondary, custom_id="music_queue", row=0)
    async def view_queue(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self.player.queue and not self.player.current:
            return await interaction.response.send_message("Queue is empty.", ephemeral=True)
        view = QueuePaginationView(list(self.player.queue), self.player.current)
        await interaction.response.send_message(embed=view.get_embed(), view=view, ephemeral=True)

    @discord.ui.button(emoji="🔀", style=discord.ButtonStyle.secondary, custom_id="music_shuffle", row=1)
    async def shuffle(self, interaction: discord.Interaction, button: discord.ui.Button):
        self.player.queue.shuffle()
        await interaction.response.send_message("🔀 Queue shuffled.", ephemeral=True, delete_after=5)

    @discord.ui.button(emoji="⏹️", style=discord.ButtonStyle.danger, custom_id="music_stop", row=1)
    async def stop(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.player.connected:
            self.player.queue.clear()
            await self.player.disconnect()
        for child in self.children:
            child.disabled = True
        await interaction.response.edit_message(view=self)

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
            title = getattr(self.current, "title", "Unknown")
            lines.append(f"**Now Playing:** {title}\n")

        start = self.page * self.per_page
        end = start + self.per_page

        if self.queue:
            lines.append("**Up Next:**")
            for i, track in enumerate(self.queue[start:end], start=start + 1):
                title = getattr(track, "title", "Unknown")
                dur = _format_duration(getattr(track, "length", 0))
                lines.append(f"`{i}.` {title} | `{dur}`")
        else:
            lines.append("Queue is empty.")

        embed.description = "\n".join(lines)
        embed.set_footer(text=f"Page {self.page + 1}/{self.max_page + 1} | {len(self.queue)} tracks")
        return embed

    @discord.ui.button(emoji="◀", style=discord.ButtonStyle.secondary)
    async def prev_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        self.page = max(0, self.page - 1)
        await interaction.response.edit_message(embed=self.get_embed(), view=self)

    @discord.ui.button(emoji="▶", style=discord.ButtonStyle.secondary)
    async def next_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        self.page = min(self.max_page, self.page + 1)
        await interaction.response.edit_message(embed=self.get_embed(), view=self)
