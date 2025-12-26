import discord
from discord.ext import commands
import random
import asyncio
import logging
from collections import Counter
from itertools import combinations

from .views import BlackjackView, DuelView
from utils.economy import EconomyManager

logger = logging.getLogger("bot")

class Gambling(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()

    def get_balance(self, user_id, location="wallet"):
        return self.economy_manager.get_balance(user_id, location)

    def update_balance(self, user_id, amount, location="wallet"):
        self.economy_manager.update_balance(user_id, amount, location)

    @commands.command()
    async def slots(self, ctx, *args):
        """Play the slot machine. Usage: !slots <amount> or !slots advanced <amount>."""
        if not args:
            return await ctx.send("Usage: `!slots <amount>` or `!slots advanced <amount>`")

        mode = 'classic'
        try:
            if isinstance(args[0], str) and args[0].lower() in ('advanced', 'adv'):
                if len(args) < 2:
                    return await ctx.send("Usage: `!slots advanced <amount>`")
                amount = int(args[1])
                mode = 'advanced'
            else:
                amount = int(args[0])
        except ValueError:
            return await ctx.send("❌ Please enter a valid number for the bet.")

        bal = self.get_balance(ctx.author.id, "wallet")
        if amount <= 0:
            return await ctx.send("Bet must be positive.")
        if amount > bal:
            return await ctx.send("You don't have enough money in your wallet!")

        self.update_balance(ctx.author.id, -amount, "wallet")

        if mode == 'classic':
            emojis = ["🍎", "🍊", "🍇", "🍒", "💎"]
            a, b, c = random.choice(emojis), random.choice(emojis), random.choice(emojis)
            result_msg = f"🎰 | {a} | {b} | {c} | 🎰"

            if a == b == c:
                winnings = amount * 5
                self.update_balance(ctx.author.id, winnings, "wallet")
                await ctx.send(f"{result_msg}\nJACKPOT!! 🚨 You won **${winnings}**!")
            elif a == b or b == c or a == c:
                winnings = amount * 2
                self.update_balance(ctx.author.id, winnings, "wallet")
                await ctx.send(f"{result_msg}\nNice! Match 2. You won **${winnings}**!")
            else:
                await ctx.send(f"{result_msg}\nNo match. You lost ${amount}.")
        else:
            emojis = ["🍎", "🍊", "🍇", "🍒", "💎", "🍋", "🍉", "⭐", "🔔", "7️⃣"]
            reels = [random.choice(emojis) for _ in range(5)]
            result_msg = "🎰 | " + " | ".join(reels) + " | 🎰"
            counts = Counter(reels)
            most_common_symbol, count = counts.most_common(1)[0]
            multipliers = {5: 12, 4: 5, 3: 1.5, 2: 0.5}
            multiplier = multipliers.get(count, 0)

            if multiplier > 0:
                base_winnings = int(amount * multiplier)
                bonus = 0
                if '💎' in reels and multiplier < multipliers[5]:
                    bonus = int(base_winnings * 0.03)
                winnings = base_winnings + bonus
                if winnings > amount * 60: winnings = amount * 60
                self.update_balance(ctx.author.id, winnings, "wallet")
                await ctx.send(f"{result_msg}\nYou matched **{count}x {most_common_symbol}**! You won **${winnings}** (x{multiplier})")
            else:
                await ctx.send(f"{result_msg}\nNo significant match. You lost ${amount}.")

    @commands.command(aliases=['bj'])
    async def blackjack(self, ctx, amount: int):
        """Play a game of Blackjack against the dealer."""
        bal = self.get_balance(ctx.author.id, "wallet")
        if amount <= 0: return await ctx.send("Bet must be positive.")
        if amount > bal: return await ctx.send("You don't have enough money.")
        view = BlackjackView(self, ctx, amount)
        embed = discord.Embed(title="🃏 Blackjack", description=f"**Your Hand:** {view.player_hand} (Score: {view.calculate_score(view.player_hand)})\n**Dealer Hand:** [{view.dealer_hand[0]}, ?]", color=0x3498db)
        embed.set_footer(text=f"Bet: ${amount}")
        await ctx.send(embed=embed, view=view)

    @commands.command()
    async def roulette(self, ctx, amount: int, choice: str):
        """Bet on Roulette. Options: red, black, odd, even, or number 0-36."""
        bal = self.get_balance(ctx.author.id, "wallet")
        if amount <= 0: return await ctx.send("Bet must be positive.")
        if amount > bal: return await ctx.send("You don't have enough money.")
        choice = choice.lower()
        valid_choices = ["red", "black", "odd", "even"]
        is_number = False
        try:
            val = int(choice)
            if 0 <= val <= 36: is_number = True
        except ValueError: pass

        if not is_number and choice not in valid_choices:
            return await ctx.send("Invalid choice! Use: red, black, odd, even, or 0-36")

        self.update_balance(ctx.author.id, -amount, "wallet")
        result = random.randint(0, 36)
        red_nums = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36]
        color = "green"
        if result in red_nums: color = "red"
        elif result != 0: color = "black"

        msg = f"🎡 Result: **{result} ({color})**\n"
        winnings = 0
        if is_number and int(choice) == result: winnings = amount * 35
        elif choice == "red" and color == "red": winnings = amount * 2
        elif choice == "black" and color == "black": winnings = amount * 2
        elif choice == "odd" and result != 0 and result % 2 != 0: winnings = amount * 2
        elif choice == "even" and result != 0 and result % 2 == 0: winnings = amount * 2

        if winnings > 0:
            self.update_balance(ctx.author.id, winnings, "wallet")
            await ctx.send(msg + f"🎉 You won **${winnings}**!")
        else:
            await ctx.send(msg + f"❌ You lost ${amount}.")

    @commands.command()
    async def poker(self, ctx, bet: int):
        """Start a poker lobby. Usage: !poker <bet>"""
        if bet <= 0:
            return await ctx.send("Bet must be positive.")
        bal = self.get_balance(ctx.author.id, "wallet")
        if bet > bal:
            return await ctx.send("You don't have enough money in your wallet.")

        view = PokerLobbyView(self, ctx, bet)
        embed = view.build_embed()
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @commands.command()
    async def duel(self, ctx, opponent: discord.Member, amount: int):
        """Challenge a user to a wild west duel for cash."""
        bal = self.get_balance(ctx.author.id, "wallet")
        opp_bal = self.get_balance(opponent.id, "wallet")
        if amount <= 0: return await ctx.send("Bet must be positive.")
        if amount > bal: return await ctx.send("You don't have the money.")
        if amount > opp_bal: return await ctx.send(f"{opponent.display_name} doesn't have the money.")
        if opponent.bot or opponent == ctx.author: return await ctx.send("Invalid opponent.")
        self.update_balance(ctx.author.id, -amount, "wallet")
        self.update_balance(opponent.id, -amount, "wallet")
        view = DuelView(self, ctx, opponent, amount)
        await ctx.send(f"🔫 **Duel Started!** {ctx.author.mention} vs {opponent.mention}\nPot: **${amount * 2}**\n{ctx.author.mention} goes first.", view=view)



class PokerLobbyView(discord.ui.View):
    def __init__(self, game_cog, ctx, bet, max_players=6):
        super().__init__(timeout=120)
        self.game_cog = game_cog
        self.ctx = ctx
        self.bet = bet
        self.max_players = max_players
        self.players = [ctx.author]
        self.started = False
        self.message = None

    def build_embed(self, status=None):
        lines = [f"{idx}. {p.display_name}" for idx, p in enumerate(self.players, start=1)]
        if not lines:
            lines = ["(empty)"]
        desc = "\n".join(lines)
        if status:
            desc = f"{status}\n\n{desc}"

        embed = discord.Embed(title="Poker Lobby", description=desc, color=0x2ecc71)
        embed.add_field(name="Bet", value=f"${self.bet}", inline=True)
        embed.add_field(name="Players", value=f"{len(self.players)}/{self.max_players}", inline=True)
        embed.set_footer(text=f"Host: {self.ctx.author.display_name}")
        return embed

    def _player_ids(self):
        return {p.id for p in self.players}

    def _bot_user(self):
        return self.game_cog.bot.user

    def _ensure_bot_wallet(self, required):
        bot_user = self._bot_user()
        if not bot_user:
            return False
        bot_id = bot_user.id
        wallet = self.game_cog.get_balance(bot_id, "wallet")
        if wallet >= required:
            return True
        bank = self.game_cog.get_balance(bot_id, "bank")
        needed = required - wallet
        if bank >= needed:
            self.game_cog.update_balance(bot_id, -needed, "bank")
            self.game_cog.update_balance(bot_id, needed, "wallet")
            return True
        return False

    def _min_buyin(self):
        return self.bet * 2

    def _insufficient_players(self):
        missing = []
        min_buyin = self._min_buyin()
        for player in self.players:
            if player.bot:
                if not self._ensure_bot_wallet(min_buyin):
                    missing.append(player.display_name)
                continue
            balance = self.game_cog.get_balance(player.id, "wallet")
            if balance < min_buyin:
                missing.append(player.display_name)
        return missing

    async def _check_dms(self):
        failures = []
        for player in self.players:
            if player.bot:
                continue
            try:
                await player.send("Poker: game starting. You'll receive your hole cards soon.")
            except discord.Forbidden:
                failures.append(player)
        return failures

    async def _close_lobby(self, status):
        if self.message:
            for child in self.children:
                child.disabled = True
            embed = self.build_embed(status=status)
            await self.message.edit(embed=embed, view=self)
        self.stop()

    @discord.ui.button(label="Join", style=discord.ButtonStyle.success)
    async def join(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.started:
            return await interaction.response.send_message("Game already started.", ephemeral=True)
        if interaction.user.bot:
            return await interaction.response.send_message("Bots can't join.", ephemeral=True)
        if interaction.user.id in self._player_ids():
            return await interaction.response.send_message("You already joined.", ephemeral=True)
        if len(self.players) >= self.max_players:
            return await interaction.response.send_message("Lobby is full.", ephemeral=True)

        self.players.append(interaction.user)
        await interaction.response.edit_message(embed=self.build_embed(), view=self)

    @discord.ui.button(label="Leave", style=discord.ButtonStyle.secondary)
    async def leave(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.started:
            return await interaction.response.send_message("Game already started.", ephemeral=True)
        if interaction.user.id not in self._player_ids():
            return await interaction.response.send_message("You are not in the lobby.", ephemeral=True)

        if interaction.user.id == self.ctx.author.id:
            await interaction.response.send_message("Host left. Lobby cancelled.", ephemeral=True)
            await self._close_lobby("Lobby cancelled by host.")
            return

        self.players = [p for p in self.players if p.id != interaction.user.id]
        await interaction.response.edit_message(embed=self.build_embed(), view=self)

    @discord.ui.button(label="Add Bot", style=discord.ButtonStyle.primary)
    async def add_bot(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.started:
            return await interaction.response.send_message("Game already started.", ephemeral=True)
        if interaction.user.id != self.ctx.author.id:
            return await interaction.response.send_message("Only the host can add the bot.", ephemeral=True)
        if len(self.players) >= self.max_players:
            return await interaction.response.send_message("Lobby is full.", ephemeral=True)

        bot_user = self._bot_user()
        if not bot_user:
            return await interaction.response.send_message("Bot user not available.", ephemeral=True)
        if bot_user.id in self._player_ids():
            return await interaction.response.send_message("Bot already joined.", ephemeral=True)
        if not self._ensure_bot_wallet(self._min_buyin()):
            return await interaction.response.send_message("Bot doesn't have enough money.", ephemeral=True)

        self.players.append(bot_user)
        await interaction.response.edit_message(embed=self.build_embed(), view=self)

    @discord.ui.button(label="Start", style=discord.ButtonStyle.danger)
    async def start(self, interaction: discord.Interaction, button: discord.ui.Button):
        if self.started:
            return await interaction.response.send_message("Game already started.", ephemeral=True)
        if interaction.user.id != self.ctx.author.id:
            return await interaction.response.send_message("Only the host can start.", ephemeral=True)
        if len(self.players) < 2:
            return await interaction.response.send_message("Need at least 2 players.", ephemeral=True)

        missing = self._insufficient_players()
        if missing:
            names = ", ".join(missing)
            return await interaction.response.send_message(f"Not enough money: {names}", ephemeral=True)

        dm_fail = await self._check_dms()
        if dm_fail:
            names = ", ".join(p.display_name for p in dm_fail)
            return await interaction.response.send_message(
                f"Cannot start. DMs disabled for: {names}", ephemeral=True
            )

        self.started = True
        for child in self.children:
            child.disabled = True

        await interaction.response.edit_message(embed=self.build_embed(status="Game started."), view=self)

        game_view = PokerGameView(self.game_cog, self.ctx, list(self.players), self.bet)
        await game_view.start_game()
        self.stop()

    @discord.ui.button(label="Cancel", style=discord.ButtonStyle.secondary)
    async def cancel(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            return await interaction.response.send_message("Only the host can cancel.", ephemeral=True)
        await interaction.response.send_message("Lobby cancelled.", ephemeral=True)
        await self._close_lobby("Lobby cancelled.")

    async def on_timeout(self):
        if not self.started:
            await self._close_lobby("Lobby timed out.")


class PokerRaiseModal(discord.ui.Modal):
    def __init__(self, game_view, player_id):
        super().__init__(title="Raise")
        self.game_view = game_view
        self.player_id = player_id
        self.raise_to = discord.ui.TextInput(
            label="Raise to (total bet this round)",
            placeholder="e.g. 200",
            required=True
        )
        self.add_item(self.raise_to)

    async def on_submit(self, interaction: discord.Interaction):
        await self.game_view.handle_raise(interaction, self.player_id, self.raise_to.value)


class PokerGameView(discord.ui.View):
    RANK_DISPLAY = {
        14: "A",
        13: "K",
        12: "Q",
        11: "J",
        10: "10",
        9: "9",
        8: "8",
        7: "7",
        6: "6",
        5: "5",
        4: "4",
        3: "3",
        2: "2"
    }

    SUIT_DISPLAY = {
        "S": "\u2660",
        "H": "\u2665",
        "D": "\u2666",
        "C": "\u2663"
    }

    STAGE_LABELS = {
        "preflop": "Preflop",
        "flop": "Flop",
        "turn": "Turn",
        "river": "River",
        "showdown": "Showdown"
    }

    def __init__(self, game_cog, ctx, players, bet):
        super().__init__(timeout=None)
        self.game_cog = game_cog
        self.ctx = ctx
        self.players = players
        self.player_map = {p.id: p for p in players}
        self.bet = bet
        self.small_blind = bet
        self.big_blind = bet * 2
        self.min_raise = self.big_blind
        self.pot = 0
        self.stage = "preflop"
        self.community_cards = []
        self.deck = self._build_deck()
        random.shuffle(self.deck)
        self.hole_cards = {}
        self.active_ids = {p.id for p in players}
        self.folded_ids = set()
        self.all_in_ids = set()
        self.committed = {p.id: 0 for p in players}
        self.round_bets = {p.id: 0 for p in players}
        self.current_bet = 0
        self.to_act = []
        self.current_player_id = None
        self.dealer_index = random.randint(0, len(players) - 1)
        self.small_blind_index = None
        self.big_blind_index = None
        self.message = None
        self.last_action = "Game started"
        self.turn_task = None
        self.turn_timeout = 30
        self.ended = False

    async def start_game(self):
        if not await self._deal_hole_cards():
            return
        self._post_blinds()
        self._set_preflop_order()
        self.message = await self.ctx.send(embed=self.build_embed(), view=self)
        await self._advance_turn()

    def _build_deck(self):
        suits = ["S", "H", "D", "C"]
        ranks = list(range(2, 15))
        return [(rank, suit) for suit in suits for rank in ranks]

    async def _deal_hole_cards(self):
        for player in self.players:
            self.hole_cards[player.id] = [self.deck.pop(), self.deck.pop()]

        for player in self.players:
            if player.bot:
                continue
            cards = self._format_cards(self.hole_cards[player.id])
            try:
                await player.send(f"Your hole cards: {cards}")
            except discord.Forbidden:
                await self.ctx.send(
                    f"Cannot start: {player.display_name} has DMs disabled."
                )
                self.ended = True
                return False
        return True

    def _format_card(self, card):
        rank, suit = card
        suit_icon = self.SUIT_DISPLAY.get(suit, suit)
        rank_text = self.RANK_DISPLAY.get(rank, str(rank))
        return f"{rank_text}{suit_icon}"

    def _format_cards(self, cards):
        return " ".join(self._format_card(card) for card in cards) if cards else "-"

    def _player_name(self, player_id):
        player = self.player_map.get(player_id)
        return player.display_name if player else str(player_id)

    def _seat_index(self, player_id):
        for idx, player in enumerate(self.players):
            if player.id == player_id:
                return idx
        return 0

    def _order_from(self, start_index, exclude_id=None):
        order = []
        for i in range(len(self.players)):
            idx = (start_index + i) % len(self.players)
            pid = self.players[idx].id
            if pid in self.active_ids and pid not in self.all_in_ids and pid != exclude_id:
                order.append(pid)
        return order

    def _post_blinds(self):
        if len(self.players) == 2:
            self.small_blind_index = self.dealer_index
            self.big_blind_index = (self.dealer_index + 1) % len(self.players)
        else:
            self.small_blind_index = (self.dealer_index + 1) % len(self.players)
            self.big_blind_index = (self.dealer_index + 2) % len(self.players)

        sb_player = self.players[self.small_blind_index]
        bb_player = self.players[self.big_blind_index]

        self._apply_bet(sb_player.id, self.small_blind, allow_all_in=True)
        self._apply_bet(bb_player.id, self.big_blind, allow_all_in=True)
        self.current_bet = max(self.round_bets.get(sb_player.id, 0), self.round_bets.get(bb_player.id, 0))

        self.last_action = (
            f"Blinds posted: {sb_player.display_name} ${self.small_blind}, "
            f"{bb_player.display_name} ${self.big_blind}"
        )

    def _preflop_start_index(self):
        if len(self.players) == 2:
            return self.dealer_index
        return (self.big_blind_index + 1) % len(self.players)

    def _postflop_start_index(self):
        if len(self.players) == 2:
            return self.big_blind_index
        return (self.dealer_index + 1) % len(self.players)

    def _set_preflop_order(self):
        self.to_act = self._order_from(self._preflop_start_index())

    def _reset_round(self):
        self.round_bets = {pid: 0 for pid in self.active_ids}
        self.current_bet = 0
        self.min_raise = self.big_blind
        self.to_act = self._order_from(self._postflop_start_index())

    def _is_bot(self, player_id):
        player = self.player_map.get(player_id)
        return player.bot if player else False

    def _ensure_bot_wallet(self, player_id, required):
        wallet = self.game_cog.get_balance(player_id, "wallet")
        if wallet >= required:
            return True
        bank = self.game_cog.get_balance(player_id, "bank")
        needed = required - wallet
        if bank >= needed:
            self.game_cog.update_balance(player_id, -needed, "bank")
            self.game_cog.update_balance(player_id, needed, "wallet")
            return True
        return False

    def _get_wallet(self, player_id):
        return self.game_cog.get_balance(player_id, "wallet")

    def _apply_bet(self, player_id, amount, allow_all_in=False):
        if amount <= 0:
            return 0
        if player_id in self.folded_ids:
            return 0
        if player_id in self.all_in_ids:
            return 0
        if self._is_bot(player_id):
            self._ensure_bot_wallet(player_id, amount)
        wallet = self._get_wallet(player_id)
        if wallet <= 0:
            return 0
        bet_amount = amount
        if amount > wallet:
            if not allow_all_in:
                return 0
            bet_amount = wallet
        self.game_cog.update_balance(player_id, -bet_amount, "wallet")
        self.pot += bet_amount
        self.round_bets[player_id] = self.round_bets.get(player_id, 0) + bet_amount
        self.committed[player_id] = self.committed.get(player_id, 0) + bet_amount
        if bet_amount >= wallet:
            self.all_in_ids.add(player_id)
            if player_id in self.to_act:
                self.to_act.remove(player_id)
        return bet_amount

    def _call_needed(self, player_id):
        return max(0, self.current_bet - self.round_bets.get(player_id, 0))

    def _actionable_ids(self):
        return [pid for pid in self.active_ids if pid not in self.all_in_ids]

    def build_embed(self):
        board = self._format_cards(self.community_cards)
        stage_name = self.STAGE_LABELS.get(self.stage, self.stage.title())
        lines = []
        for idx, player in enumerate(self.players):
            pid = player.id
            tags = []
            if idx == self.dealer_index:
                tags.append("D")
            if idx == self.small_blind_index:
                tags.append("SB")
            if idx == self.big_blind_index:
                tags.append("BB")
            tag_text = f"[{'/'.join(tags)}] " if tags else ""
            if pid in self.folded_ids:
                status = "Folded"
            elif pid in self.all_in_ids:
                status = "All-in"
            else:
                status = "Active"
            bet = self.round_bets.get(pid, 0)
            stack = self._get_wallet(pid)
            marker = "\u25B6 " if pid == self.current_player_id else ""
            lines.append(f"{marker}{tag_text}{player.display_name}: {status} | bet ${bet} | stack ${stack}")

        embed = discord.Embed(title="Texas Hold'em", color=0x1abc9c)
        embed.add_field(name="Stage", value=stage_name, inline=True)
        embed.add_field(name="Pot", value=f"${self.pot}", inline=True)
        embed.add_field(name="Current Bet", value=f"${self.current_bet}", inline=True)
        embed.add_field(name="Board", value=board, inline=False)
        embed.add_field(name="Players", value="\n".join(lines), inline=False)

        turn_name = self._player_name(self.current_player_id) if self.current_player_id else "-"
        embed.set_footer(text=f"Turn: {turn_name} | Last action: {self.last_action}")
        return embed

    def _sync_buttons(self):
        if not self.current_player_id:
            return
        call_needed = self._call_needed(self.current_player_id)
        self.check_call.label = "Check" if call_needed == 0 else f"Call ${call_needed}"
        wallet = self._get_wallet(self.current_player_id)
        min_raise_to = self.min_raise if self.current_bet == 0 else self.current_bet + self.min_raise
        required = max(0, min_raise_to - self.round_bets.get(self.current_player_id, 0))
        self.raise_btn.disabled = wallet < (call_needed + self.min_raise) or wallet < required
        self.all_in.disabled = wallet <= 0

    async def _update_message(self):
        if not self.message:
            return
        self._sync_buttons()
        await self.message.edit(embed=self.build_embed(), view=self)

    def _remove_from_to_act(self, player_id):
        if player_id in self.to_act:
            self.to_act = [pid for pid in self.to_act if pid != player_id]

    def _cancel_turn_timer(self):
        if self.turn_task and not self.turn_task.done():
            self.turn_task.cancel()
        self.turn_task = None

    def _start_turn_timer(self):
        self._cancel_turn_timer()
        player_id = self.current_player_id
        self.turn_task = asyncio.create_task(self._turn_timeout(player_id))

    async def _turn_timeout(self, player_id):
        await asyncio.sleep(self.turn_timeout)
        if self.ended or self.current_player_id != player_id:
            return
        call_needed = self._call_needed(player_id)
        if call_needed > 0:
            await self._do_fold(player_id, "auto-fold (timeout)")
        else:
            await self._do_check_call(player_id, "auto-check (timeout)")

    async def _advance_turn(self):
        if self.ended:
            return
        self._cancel_turn_timer()
        if len(self.active_ids) == 1:
            await self._award_single_winner()
            return
        if not self._actionable_ids():
            await self._runout_board()
            return
        if not self.to_act:
            await self._advance_stage()
            return

        self.current_player_id = self.to_act[0]
        await self._update_message()
        if self._is_bot(self.current_player_id):
            asyncio.create_task(self._bot_take_turn(self.current_player_id))
        else:
            self._start_turn_timer()

    async def _advance_stage(self):
        if self.stage == "preflop":
            self.community_cards.extend([self.deck.pop() for _ in range(3)])
            self.stage = "flop"
            self.last_action = "Flop dealt"
        elif self.stage == "flop":
            self.community_cards.append(self.deck.pop())
            self.stage = "turn"
            self.last_action = "Turn dealt"
        elif self.stage == "turn":
            self.community_cards.append(self.deck.pop())
            self.stage = "river"
            self.last_action = "River dealt"
        else:
            await self._showdown()
            return

        self._reset_round()
        await self._advance_turn()

    async def _runout_board(self):
        self.last_action = "All players are all-in. Running board."
        while len(self.community_cards) < 5:
            self.community_cards.append(self.deck.pop())
        await self._showdown()

    async def _award_single_winner(self):
        winner_id = next(iter(self.active_ids))
        self.game_cog.update_balance(winner_id, self.pot, "wallet")
        winner_name = self._player_name(winner_id)
        self.last_action = f"{winner_name} won ${self.pot} (everyone folded)"
        await self._end_game(f"{winner_name} wins **${self.pot}**!")

    async def _end_game(self, result_text):
        self.ended = True
        self._cancel_turn_timer()
        for child in self.children:
            child.disabled = True
        embed = self.build_embed()
        embed.add_field(name="Result", value=result_text, inline=False)
        if self.message:
            await self.message.edit(embed=embed, view=self)
        self.stop()

    def _straight_high(self, ranks):
        unique = sorted(set(ranks), reverse=True)
        if len(unique) != 5:
            return 0
        if unique == [14, 5, 4, 3, 2]:
            return 5
        if unique[0] - unique[-1] == 4:
            return unique[0]
        return 0

    def _evaluate_five(self, cards):
        ranks = sorted([r for r, _ in cards], reverse=True)
        suits = [s for _, s in cards]
        counts = Counter(ranks)
        count_sorted = sorted(counts.items(), key=lambda x: (x[1], x[0]), reverse=True)
        counts_values = sorted(counts.values(), reverse=True)
        is_flush = len(set(suits)) == 1
        straight_high = self._straight_high(ranks)

        if is_flush and straight_high:
            return (8, (straight_high,), "Straight Flush")
        if counts_values == [4, 1]:
            four_rank = count_sorted[0][0]
            kicker = count_sorted[1][0]
            return (7, (four_rank, kicker), "Four of a Kind")
        if counts_values == [3, 2]:
            three_rank = count_sorted[0][0]
            pair_rank = count_sorted[1][0]
            return (6, (three_rank, pair_rank), "Full House")
        if is_flush:
            return (5, tuple(ranks), "Flush")
        if straight_high:
            return (4, (straight_high,), "Straight")
        if counts_values == [3, 1, 1]:
            three_rank = count_sorted[0][0]
            kickers = [rank for rank, count in count_sorted[1:]]
            return (3, (three_rank, *kickers), "Three of a Kind")
        if counts_values == [2, 2, 1]:
            pair_high = count_sorted[0][0]
            pair_low = count_sorted[1][0]
            kicker = count_sorted[2][0]
            return (2, (pair_high, pair_low, kicker), "Two Pair")
        if counts_values == [2, 1, 1, 1]:
            pair_rank = count_sorted[0][0]
            kickers = [rank for rank, count in count_sorted[1:]]
            return (1, (pair_rank, *kickers), "One Pair")
        return (0, tuple(ranks), "High Card")

    def _best_hand(self, cards):
        best_score = None
        best_name = ""
        for combo in combinations(cards, 5):
            rank, tie, name = self._evaluate_five(combo)
            score = (rank, tie)
            if best_score is None or score > best_score:
                best_score = score
                best_name = name
        return best_score, best_name

    def _compute_side_pots(self):
        contribs = {pid: self.committed.get(pid, 0) for pid in self.player_map}
        levels = sorted({amt for amt in contribs.values() if amt > 0})
        pots = []
        prev = 0
        for level in levels:
            contributors = [pid for pid, amt in contribs.items() if amt >= level]
            pot_amount = (level - prev) * len(contributors)
            if pot_amount <= 0:
                prev = level
                continue
            eligible = [pid for pid in self.active_ids if contribs.get(pid, 0) >= level]
            pots.append((pot_amount, eligible))
            prev = level
        return pots

    async def _showdown(self):
        self.stage = "showdown"
        scores = {}
        hand_names = {}
        for player_id in self.active_ids:
            seven_cards = self.hole_cards[player_id] + self.community_cards
            score, name = self._best_hand(seven_cards)
            scores[player_id] = score
            hand_names[player_id] = name

        pots = self._compute_side_pots()
        payouts = {pid: 0 for pid in self.player_map}
        for pot_amount, eligible in pots:
            eligible = [pid for pid in eligible if pid in self.active_ids]
            if not eligible:
                continue
            best_score = max(scores[pid] for pid in eligible)
            winners = [pid for pid in eligible if scores[pid] == best_score]
            share = pot_amount // len(winners)
            remainder = pot_amount % len(winners)
            for idx, winner_id in enumerate(winners):
                payout = share + (1 if idx < remainder else 0)
                self.game_cog.update_balance(winner_id, payout, "wallet")
                payouts[winner_id] += payout

        self.ended = True
        self._cancel_turn_timer()
        for child in self.children:
            child.disabled = True

        embed = discord.Embed(title="Texas Hold'em Results", color=0x3498db)
        embed.add_field(name="Board", value=self._format_cards(self.community_cards), inline=False)

        for player in self.players:
            pid = player.id
            if pid in self.folded_ids:
                embed.add_field(name=player.display_name, value="Folded", inline=False)
                continue
            hole = self._format_cards(self.hole_cards[pid])
            hand_name = hand_names.get(pid, "-")
            embed.add_field(name=player.display_name, value=f"{hole} | {hand_name}", inline=False)

        payout_lines = [
            f"{self._player_name(pid)} (${amount})"
            for pid, amount in payouts.items()
            if amount > 0
        ]
        embed.add_field(
            name="Payouts",
            value=", ".join(payout_lines) if payout_lines else "None",
            inline=False
        )
        if self.message:
            await self.message.edit(embed=embed, view=self)
        self.stop()

    async def _do_fold(self, player_id, reason):
        if player_id in self.active_ids:
            self.active_ids.remove(player_id)
        self.folded_ids.add(player_id)
        self._remove_from_to_act(player_id)
        self.last_action = f"{self._player_name(player_id)} folded ({reason})"
        await self._advance_turn()

    async def _do_check_call(self, player_id, reason):
        call_needed = self._call_needed(player_id)
        wallet = self._get_wallet(player_id)
        if call_needed > 0:
            if wallet <= 0:
                await self._do_fold(player_id, "not enough to call")
                return
            if wallet >= call_needed:
                self._apply_bet(player_id, call_needed, allow_all_in=False)
                self.last_action = f"{self._player_name(player_id)} called ${call_needed} ({reason})"
            else:
                paid = self._apply_bet(player_id, wallet, allow_all_in=True)
                self.last_action = f"{self._player_name(player_id)} all-in for ${paid} ({reason})"
        else:
            self.last_action = f"{self._player_name(player_id)} checked ({reason})"

        self._remove_from_to_act(player_id)
        await self._advance_turn()

    async def _do_raise(self, player_id, raise_to, reason):
        prev_current = self.current_bet
        current_round = self.round_bets.get(player_id, 0)
        amount_needed = raise_to - current_round
        if amount_needed <= 0:
            await self._do_check_call(player_id, "invalid raise")
            return
        paid = self._apply_bet(player_id, amount_needed, allow_all_in=True)
        if paid <= 0:
            await self._do_fold(player_id, "not enough to raise")
            return
        new_total = self.round_bets.get(player_id, 0)
        if new_total > self.current_bet:
            self.current_bet = new_total
        raise_gap = self.current_bet - prev_current
        if new_total <= prev_current:
            self.last_action = f"{self._player_name(player_id)} called (${paid})"
            self._remove_from_to_act(player_id)
            await self._advance_turn()
            return
        if raise_gap >= self.min_raise:
            self.min_raise = raise_gap
            start_index = (self._seat_index(player_id) + 1) % len(self.players)
            self.to_act = self._order_from(start_index, exclude_id=player_id)
        else:
            self._remove_from_to_act(player_id)
        self.last_action = f"{self._player_name(player_id)} raised to ${self.current_bet} ({reason})"
        await self._advance_turn()

    async def _do_all_in(self, player_id, reason):
        wallet = self._get_wallet(player_id)
        if wallet <= 0:
            await self._do_fold(player_id, "no chips")
            return
        prev_current = self.current_bet
        current_round = self.round_bets.get(player_id, 0)
        paid = self._apply_bet(player_id, wallet, allow_all_in=True)
        new_total = current_round + paid
        if new_total > self.current_bet:
            self.current_bet = new_total
        raise_gap = self.current_bet - prev_current
        if new_total > prev_current and raise_gap >= self.min_raise:
            self.min_raise = raise_gap
            start_index = (self._seat_index(player_id) + 1) % len(self.players)
            self.to_act = self._order_from(start_index, exclude_id=player_id)
            self.last_action = f"{self._player_name(player_id)} all-in to ${self.current_bet} ({reason})"
        else:
            self._remove_from_to_act(player_id)
            if new_total > prev_current:
                self.last_action = f"{self._player_name(player_id)} all-in to ${self.current_bet} ({reason})"
            else:
                self.last_action = f"{self._player_name(player_id)} all-in for ${paid} ({reason})"
        await self._advance_turn()

    async def _bot_take_turn(self, player_id):
        await asyncio.sleep(random.uniform(1.0, 2.0))
        if self.ended or self.current_player_id != player_id:
            return
        call_needed = self._call_needed(player_id)
        wallet = self._get_wallet(player_id)
        if call_needed > 0:
            if wallet <= 0:
                await self._do_fold(player_id, "bot fold")
                return
            if random.random() < 0.15 and wallet >= (call_needed + self.min_raise):
                await self._do_raise(player_id, self.current_bet + self.min_raise, "bot raise")
            elif wallet >= call_needed:
                await self._do_check_call(player_id, "bot call")
            else:
                await self._do_all_in(player_id, "bot all-in")
        else:
            if random.random() < 0.2 and wallet >= self.min_raise:
                await self._do_raise(player_id, self.min_raise, "bot raise")
            elif wallet > 0 and random.random() < 0.05:
                await self._do_all_in(player_id, "bot all-in")
            else:
                await self._do_check_call(player_id, "bot check")

    @discord.ui.button(label="Fold", style=discord.ButtonStyle.danger)
    async def fold(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.current_player_id:
            return await interaction.response.send_message("Not your turn.", ephemeral=True)
        await interaction.response.defer()
        await self._do_fold(interaction.user.id, "player fold")

    @discord.ui.button(label="Check/Call", style=discord.ButtonStyle.success)
    async def check_call(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.current_player_id:
            return await interaction.response.send_message("Not your turn.", ephemeral=True)
        await interaction.response.defer()
        await self._do_check_call(interaction.user.id, "player action")

    @discord.ui.button(label="Raise", style=discord.ButtonStyle.primary)
    async def raise_btn(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.current_player_id:
            return await interaction.response.send_message("Not your turn.", ephemeral=True)
        call_needed = self._call_needed(interaction.user.id)
        wallet = self._get_wallet(interaction.user.id)
        if wallet <= call_needed:
            return await interaction.response.send_message("Not enough to raise.", ephemeral=True)
        modal = PokerRaiseModal(self, interaction.user.id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="All-in", style=discord.ButtonStyle.secondary)
    async def all_in(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.current_player_id:
            return await interaction.response.send_message("Not your turn.", ephemeral=True)
        wallet = self._get_wallet(interaction.user.id)
        if wallet <= 0:
            return await interaction.response.send_message("No chips to go all-in.", ephemeral=True)
        await interaction.response.defer()
        await self._do_all_in(interaction.user.id, "player all-in")

    async def handle_raise(self, interaction, player_id, raise_to_value):
        if player_id != self.current_player_id:
            return await interaction.response.send_message("Not your turn.", ephemeral=True)
        try:
            raise_to = int(raise_to_value)
        except ValueError:
            return await interaction.response.send_message("Invalid raise amount.", ephemeral=True)

        min_raise_to = self.min_raise if self.current_bet == 0 else self.current_bet + self.min_raise
        if raise_to < min_raise_to:
            return await interaction.response.send_message(
                f"Raise must be at least ${min_raise_to}.", ephemeral=True
            )

        wallet = self._get_wallet(player_id)
        max_total = self.round_bets.get(player_id, 0) + wallet
        if raise_to > max_total:
            return await interaction.response.send_message(
                f"Max raise is ${max_total}.", ephemeral=True
            )

        await interaction.response.defer()
        await self._do_raise(player_id, raise_to, "player raise")


async def setup(bot):
    await bot.add_cog(Gambling(bot))
