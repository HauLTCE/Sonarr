# Music "Now Playing" message — diagnosis (READ-ONLY, no code changed)

You asked why the Now Playing embed stopped appearing. I traced the path
without touching the music cog or Lavalink. Here's what I found and what I'd
*propose* (for your approval — I won't change music code unsupervised).

## The path
`on_wavelink_track_start` (cog.py:344) → if not looping-same →
`_send_interactive_player` (cog.py:275) → `_get_bound_channel` (cog.py:266)
→ `channel.send(embed, view=PlayerControlView)`.

## The single point of failure
Everything depends on **`player.text_channel_id`**, a runtime attribute set in
exactly ONE place: `_get_or_create_player` at cog.py:242, when you run a
command. `_get_bound_channel` does:
```python
channel_id = getattr(player, "text_channel_id", None)
if not channel_id:
    return None            # <-- silent. no message, no log, no error.
```
And `_send_interactive_player` returns immediately if channel is None, and even
wraps the actual `channel.send` in `except discord.HTTPException: pass`. So
**every failure mode here is invisible** — the embed just never appears.

## Most likely causes, ranked
1. **Autoplay/Smart Radio starts a track before `text_channel_id` is set.**
   `player.autoplay = AutoPlayMode.enabled` (cog.py:240). When wavelink
   auto-enqueues a recommended track and the player object was created on a
   path that didn't run `_get_or_create_player` (e.g. session resume, or a
   reconnect after Lavalink blipped), `text_channel_id` is missing → silent
   None. This best matches "worked before, stopped now."
2. **The bound channel id no longer resolves.** `bot.get_channel(channel_id)`
   returns None if the channel is uncached (bot restarted and hasn't cached it
   yet) or isn't a `TextChannel` (e.g. a thread/forum). Also silent.
3. **`is_looping_same` suppression false-positive** (cog.py:356) — if queue
   mode is loop, the same URI replaying is intentionally silenced. Probably not
   your case, but worth ruling out.
4. **A wavelink event/payload mismatch** from a version change. You said
   versions are fixed, so I rank this last — but if `on_wavelink_track_start`
   isn't firing at all, that's the tell.

## How to confirm (no code change, just observe)
- `docker logs sonarr-bot -f`, play a song. If you see the track play but NO
  "Track exception" and NO embed → it's the silent-None path (cause 1 or 2).
- If the embed appears when you use `!play` but NOT when autoplay picks the
  next song → it's cause 1, confirmed.

## Proposed fix (for your OK — I'll only touch the cog, never Lavalink)
Small, safe, and it makes the failure *visible* instead of silent:
1. In `on_wavelink_track_start`, fall back to the player's voice-channel guild's
   system/last channel, OR persist `text_channel_id` so it survives autoplay.
   Simplest: when `text_channel_id` is missing, log a warning instead of
   silently returning — so we stop guessing.
2. Set `text_channel_id` in more places than just the command path (e.g. on
   session restore), so autoplay tracks inherit it.

Tell me which cause the logs confirm and I'll write the minimal patch. I did
NOT modify any music file for this diagnosis.
