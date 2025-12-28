# SONARR Discord Bot

A feature-rich Discord bot with AI personality, advanced economy system, stock market, gambling, Pokémon, and moderation tools.

**Stack**: Discord.py 2.4.0 | Google Gemini AI | SQLite | Python 3.x

---

## 📋 Table of Contents

1. [Overview](#overview)
2. [Features](#features)
3. [Installation & Setup](#installation--setup)
4. [Architecture](#architecture)
5. [Economy System](#economy-system)
6. [Stock Market](#stock-market)
7. [Loan Shark System](#loan-shark-system)
8. [Gambling](#gambling)
9. [Pokémon System](#pokémon-system)
10. [AI Personality](#ai-personality)
11. [Commands Reference](#commands-reference)
12. [Deployment](#deployment)

---

## Overview

SONARR is a sarcastic, diva-personality Discord bot that manages a complete in-game economy with stocks, loans, gambling, and AI-driven interactions. The bot features autonomous money management (auto-robbing, idle chat), comprehensive economy tracking, and sophisticated market mechanics.

**Key Stats**:
- 8 tradeable stocks with hourly updates
- Loan shark system with credit scoring and bankruptcy
- Advanced gambling (slots, blackjack, duel)
- Pokémon collection/trading system
- AI message classification with smart caching
- 200+ database commands tracked

---

## Features

### 🎭 AI Personality
- **Smart Classification**: Uses Gemini AI with keyword fallback
- **Context-Aware Responses**: Replies to mentions with cached, categorized responses
- **Debt Enforcement**: Automatically harasses users with overdue loans
- **Auto-Robbing**: 3% hourly chance to steal from wallets/banks with weighted targeting
- **Idle Chat**: Breaks silence after 4+ hours with random quips
- **Gossip System**: 10% chance to comment on mentioned users based on their status
  - Rich/poor/bankrupt comments
  - Investor mocking (portfolio roasting)
  - Debtor threats (loan shark attitude)
  - Pokémon trainer teasing
  - **Loudmouth detection** (level 15+ with 50+ commands: comments on excessive talking)
  - Generic sarcasm fallback

### 💰 Economy System
- **Wallet/Bank Balance**: Dual-currency storage
- **Daily Rewards**: !daily command with streak tracking
- **Work System**: !work command with Pokémon passive income bonuses
- **Passive Income**: Pokémon rarity/level affects earning rate
- **Transactions**: Buy/sell items, trade Pokémon, gamble
- **Bankruptcy Protection**: Automatic debt forgiveness with 3-day shame period

### 📈 Stock Market
**8 Stocks**: LABOR, TECH, WATER, FIRE, GRASS, ELECTRIC, PSYCHIC, DRAGON

**Advanced Balancing System** (3-factor algorithm):
1. **Rubber Band to Base Price**
   - Overvalued (>150% base): Increased crash chance, reduced boom chance
   - Undervalued (<70% base): Increased boom chance, reduced crash chance
   - Gentle drift toward equilibrium

2. **Market Cap Pressure**
   - When total user holdings exceed 100 shares, volatility scales up
   - High demand + overvalued = bubble risk (crashes)
   - High demand + undervalued = buying pressure (booms)

3. **Momentum Reversals**
   - 2+ consecutive gains: Increased reversal probability
   - 2+ consecutive losses: Recovery bounce probability

4. **News Sentiment**
   - Recent positive/negative news affects short-term price bias

**Market Events**:
- Hourly price updates (1% swing normally, ±30% crash/boom)
- **Hourly news cycle** with 1-4 news items per update
- 8 news categories (earnings, dividends, layoffs, innovation, scandal, seasonal, partnerships, external)
- Market news posted to dedicated channel
- Price history tracking (7 days)

**Commands**:
- `!stocks` - View all stock prices & volatility
- `!buy_stock <ticker> <shares>` - Purchase shares
- `!sell_stock <ticker> <shares>` - Sell shares
- `!portfolio` - View holdings with live P/L
- `!stock_info <ticker>` - Detailed ticker info
- `!market_news` - Latest market news
- `!trade <user>` - Accept/propose trades

### 🏦 Loan Shark System
**Credit Tiers**:
- Excellent (score 200+): 3% daily interest, up to $5,000 loan
- Good (100-199): 5% daily, up to $3,000
- Fair (50-99): 10% daily, up to $1,500
- Poor (<50): 15% daily, up to $1,000

**Features**:
- Credit score based on history (work activity, won/lost gambling, etc.)
- Interest compounds daily
- Automated enforcement on overdue debt
- Late notice escalation (3→7→14+ days overdue)
- Collateral support (seize Pokémon on default)
- Bankruptcy option with 7-day cooldown
- Financial Ruin shame role (3 days, reduced earnings)

**Commands**:
- `!loan <amount>` - Request a loan
- `!loan_status` - View current debt
- `!pay_loan <amount>` - Make payments
- `!bankruptcy` - Declare bankruptcy (costs collateral/shame)

### 🎰 Gambling
**Slots** (`!slots <amount>`):
- 10 symbols, min bet $50
- Payouts: 3x match = 2x, rainbow = 5x
- Variance penalty: Higher bets = lower multipliers

**Blackjack** (`!blackjack <amount>`):
- Player vs AI dealer
- Hit/stand/double down
- Dealer rules: Hit on 16, stand on 17+
- Ace flexibility

**Duel** (`!duel <@user> <amount>`):
- 50/50 coin flip
- Winner takes all
- Both players must accept

**Modifications**:
- Pokémon passive income reduces losses
- Rarity matters: Legendary = 25% loss reduction

### 🔴 Pokémon System
**Catch Mechanics**:
- Find Pokémon in specific zones (water, fire, grass, etc.)
- Species rarity affects success rate
- IVs, EVs, level tracking

**Features**:
- Trading between players
- Daycare system for leveling
- Pokedex progress tracking
- Passive income from owned Pokémon (rarity/level bonus)
- Collateral for loans

**Commands**:
- `!hunt <zone>` - Hunt Pokémon in zone
- `!pokemon` - View collection
- `!trade <@user>` - Trade Pokémon
- `!daycare` - Breed/level Pokémon

---

## Installation & Setup

### Prerequisites
- Python 3.10+
- Discord bot token
- Google Gemini API key (x3 recommended for quota rotation)
- SQLite (included in Python)

### Install

```bash
# Clone repository
git clone https://github.com/HauLTCE/SONARR.git
cd SONARR

# Create virtual environment
python -m venv venv
source venv/bin/activate  # or: venv\Scripts\activate (Windows)

# Install dependencies
pip install -r requirements.txt

# Setup environment
cp .env.example .env
# Edit .env with your tokens:
# DISCORD_TOKEN=your_token
# GEMINI_API_KEY_1=key1
# GEMINI_API_KEY_2=key2
# GEMINI_API_KEY_3=key3
```

### Run Locally

```bash
python main.py
```

### Deploy to Server

```bash
scp -r . root@192.168.1.101:/root/sonarr/bot/
ssh root@192.168.1.101
systemctl restart discordbot
systemctl status discordbot
```

---

## Architecture

### Directory Structure
```
bot/
├── main.py                    # Bot initialization & startup
├── requirements.txt           # Python dependencies
├── items.json                 # Shop inventory
├── pokemon_moves.json         # Pokémon move database
├── pokemon_species.json       # Species rarity/stats
├── pokemon_zones.json         # Hunt zones
│
├── cogs/                      # Command modules
│   ├── admin.py               # Admin commands (!eval, etc)
│   ├── bot_economy.py         # Economy commands (!balance, !work, !daily)
│   ├── bot_personality.py     # AI personality (message listener)
│   ├── fun.py                 # Fun commands
│   ├── gambling.py            # Gambling system
│   ├── game.py                # General game commands
│   ├── levels.py              # Level/XP tracking
│   ├── loans.py               # Loan shark system
│   ├── moderation.py          # Mod commands
│   ├── music.py               # Music playback
│   ├── pokemon.py             # Pokémon commands
│   ├── stocks.py              # Stock market system
│   ├── utility.py             # Utility commands
│   ├── views.py               # UI components
│   ├── welcome.py             # Welcome messages
│   └── ...
│
└── utils/                     # Shared utilities
    ├── database.py            # SQLite ORM (1500+ lines)
    ├── economy.py             # Economy manager
    ├── cache.py               # YouTube/message caching
    ├── checks.py              # Permission checks
    ├── config.py              # Server configuration
    ├── command_history.py     # Command logging
    ├── help.py                # Custom help formatter
    ├── internal_commands.py    # Internal command parser
    ├── logger.py              # Logging setup
    ├── music_queue.py         # Music queue management
    ├── premade_answers.py     # Cold response templates
    ├── response_effects.py    # Effect processing
    ├── spam.py                # Spam detection
    ├── ytdl.py                # YouTube-DL wrapper
    └── pokemon_system.py      # Pokémon logic
```

### Database Schema
SQLite with 15+ tables:
- **economy** - User balances, daily streak
- **portfolio** - Stock holdings
- **stock_history** - Price history
- **market_news** - News items
- **loans** - Active loans, repayment tracking
- **bankruptcies** - Bankruptcy records
- **pokemon** - Pokémon collection
- **pokemon_daily** - Hunt cooldowns
- **items** - User inventory
- **command_history** - Command tracking
- **message_cache** - AI response cache (LRU per guild)
- And more...

### Data Flow

```
User Message
    ↓
bot_personality.on_message()
    ├─ Spam check
    ├─ Debt enforcement (20% chance)
    ├─ Rude user punishment (30% if negative keywords)
    ├─ Gossip trigger (10% if user mentioned)
    └─ AI Classification
        ├─ Keyword match (short messages)
        ├─ Cache lookup (smart hashing)
        ├─ Gemini API (full classification)
        └─ Rate limit + key rotation
    ↓
Response Selection
    ├─ COLD_RESPONSES dict (300+ premade lines)
    ├─ Status-based gossip (weighted pool)
    └─ Effect processing (rename, rob, timeout, etc)
    ↓
Send Reply
```

---

## Economy System

### Balance Management

```python
# Wallet: Daily spending, earnings from work/gambling
# Bank: Savings, protected from theft
wallet = 1000
bank = 5000
total = 6000  # What matters for net worth

# Commands
!balance              # View wallet + bank
!daily                # $100-500 daily reward (streak bonus)
!work                 # Earn $50-300 based on Pokémon passive income
!deposit <amount>     # Move wallet → bank
!withdraw <amount>    # Move bank → wallet
```

### Auto-Features

- **Auto Rob** (every 10-30 min): 3% chance to steal from weighted targets (richer = higher chance)
- **Secure Bot Wallet**: Keeps only $500 in wallet, deposits excess to bank
- **Bankruptcy Protection**: Automatic after 14+ days of overdue debt

---

## Stock Market

### Price Dynamics

```python
# Price change calculation with 4-factor balancing:

1. Rubber Band:
   if price > base_price * 1.5:
       crash_chance *= (price/base_price - 0.5)
   if price < base_price * 0.7:
       boom_chance *= (1.5 - price/base_price)

2. Market Cap Pressure:
   total_shares = db.get_total_shares_held(ticker)
   if total_shares > 100:
       volatility *= 1 + (total_shares / 500)

3. Momentum:
   history = db.get_stock_price_history(ticker, 24)
   if 2+ consecutive gains:
       crash_chance *= (1 + gains * 0.3)
   if 2+ consecutive losses:
       boom_chance *= (1 + losses * 0.3)

4. News Sentiment:
   if recent_positive_news:
       crash_chance *= 1.2  # Correction bias
   if recent_negative_news:
       boom_chance *= 1.2   # Recovery bias
```

### News Categories (8 types)

Weighted probabilities:
- **Positive** (18%): Earnings beat, dividend announcement, product success
- **Negative** (18%): Layoffs, scandal, product recall, failed partnership
- **Bullish** (9%): New partnership, market expansion
- **Bearish** (9%): Competition, regulation
- **Neutral** (10%): Status updates, conferences
- **Seasonal** (15%): Holiday sales, Q4 rush, summer slump
- **External** (13%): Macro economy, sector cascades
- **Stock-Specific** (8%): Insider rumors, analyst ratings

### Example Session

```
Market Update (Hourly):
LABOR: $150 → $155 (+3.3%)  ← Momentum reversal after 3-day rally
TECH:  $200 → $196 (-2.0%)  ← News: Layoffs announced
WATER: $100 → $108 (+8%)    ← Rubber band: Was undervalued
DRAGON: $300 → $310 (+3.3%) ← Market cap pressure easing

Portfolio Impact:
Player A: 50 TECH @ $200 = $10,000 → Now $9,800 (−$200) 😬
Player B: 200 LABOR @ $100 = $20,000 → Now $31,000 (+$11,000) 🤑
```

---

## Loan Shark System

### Credit Scoring

Credit score factors:
- Work activity (positive: +1 per $100 earned)
- Gambling wins (positive: +5 per win)
- Gambling losses (negative: −2 per loss)
- Loan repayment (positive: +20 on full repay)
- Loan default (negative: −50 on bankruptcy)

### Enforcement Timeline

```
Day 0-7:     Grace period (no action)
Day 7-14:    Late notices, friendly reminders
Day 14-21:   Debt enforcement kicks in
             - 10% wallet seizure every interaction
             - Rename to "Debtor"
             - Reduce work earnings
Day 21+:     Severe enforcement
             - 20% wallet seizure
             - 30 min timeout
             - Seize Pokémon collateral
```

### Bankruptcy

**Trigger**: User declares bankruptcy when debt > income

**Effects**:
- Debt completely forgiven
- Financial Ruin role (3 days)
- 50% work earnings for 3 days
- 7-day cooldown before borrowing again
- Reputation hit (public announcement)

---

## Gambling

### Slots Payouts (nerf active)

Min bet: **$50** (prevents spam)
Symbols: 10 (reduced from 16)

| Match | Payout | Variance Penalty |
|-------|--------|------------------|
| 3x    | 2x     | Higher bet = lower multiplier |
| Rainbow | 5x | Max reduction: -30% |

**Example**:
- Small bet ($50): 2x payout = $100
- Medium bet ($500): 1.8x payout = $900
- Large bet ($2000): 1.4x payout = $2,800

### Blackjack

- Standard 21 rules
- Hit/stand/double down
- Dealer hits on <17
- Ace = 1 or 11

### Duel

- 50/50 coin flip
- Winner doubles money
- Both must accept
- Can refuse rigged attempt

---

## Pokémon System

### Hunt Zones

Each zone has species pool:
- **Water**: Squirtle, Psyduck, Seel, Slowpoke, etc.
- **Fire**: Charmander, Vulpix, Growlithe, etc.
- **Grass**: Bulbasaur, Bellsprout, Oddish, etc.
- **Electric**: Pikachu, Magnemite, Voltorb, etc.
- **Psychic**: Abra, Drowzee, Exeggcute, etc.
- **Dragon**: Dratini, Bagon, Jangmo-o, etc.

### Rarity Modifiers

| Rarity | Catch Rate | Passive Bonus |
|--------|-----------|---------------|
| Common | 80% | +5% work |
| Uncommon | 60% | +7% work |
| Rare | 40% | +10% work |
| Epic | 20% | +13% work |
| Legendary | 5% | +18% work |

### Passive Income Example

```
Base work: $100
Own 1 Rare Pokemon: +10%
Rare Pokemon level 50: +5% (50 * 0.1%)
Total bonus: 15%
Actual earnings: $115
```

---

## AI Personality

### Classification Pipeline

1. **Keyword Check** (<3 words): Instant categorization
2. **Dominant Keywords** (3+ words): If 2+ matches in category
3. **Cache Lookup**: Hash message content words, check SQLite cache
4. **Gemini API**: Full classification if not cached
5. **Rate Limit**: 3 AI calls per user per hour
6. **Key Rotation**: Cycles through 3 API keys on quota exhaustion

### Response Categories (35+)

greeting, thanks, goodbye, question, confusion, insult, affection, vent, excitement, complaint, joke, help, agreement, disagreement, bored, flirt, brag, flex, beg, chitchat, advice, compliment, request, apology, statement, sarcasm, threat, command, praise, spam, excuse, overshare, challenge, opinion, lie, guilt, random

### Gossip Categories (8)

| Category | Trigger | Example |
|----------|---------|---------|
| Bankrupt | In shame period | "They hit rock bottom. I was there. I pushed." |
| Rich | >$50k | "Ah yes, {target} the walking ATM." |
| Poor | <$500 | "They're broke. Can't rob someone with nothing." |
| Investor | Has stocks | "I've seen {target}'s portfolio. Bold strategy." |
| Debtor | Has active loan | "I've got {target}'s loan papers right here." |
| Pokémon | Has caught | "{target} collects Pokemon. At their age. Bold." |
| **Loudmouth** | **Level 15+ & 50+ commands** | **"{target} at level {level} is living their best loud life."** |
| Gambler | 10+ games | "The house always wins. {target} never learns." |
| Criminal | 5+ robberies | "We're in the same business, {target} and I." |

---

## Commands Reference

### Economy

```
!balance              View wallet + bank + total
!daily                Daily reward ($100-500)
!work                 Earn $50-300
!deposit <amount>     Move to bank
!withdraw <amount>    Move to wallet
!inventory            View items
!shop                 Buy items
```

### Stock Market

```
!stocks               All prices & volatility
!buy_stock TECH 10    Buy 10 shares
!sell_stock TECH 5    Sell 5 shares
!portfolio            Your holdings
!stock_info LABOR     Ticker details
!market_news          Recent news
!trade @user          Trade with someone
```

### Loans

```
!loan 1000            Request $1000
!loan_status          View debt + interest
!pay_loan 500         Pay $500 toward debt
!bankruptcy           Declare bankruptcy (7-day cooldown)
```

### Gambling

```
!slots 100            Spin slots ($50 min)
!blackjack 100        Play blackjack
!duel @user 100       Challenge another player
```

### Pokémon

```
!hunt water           Hunt in water zone
!pokemon              View collection
!trade @user          Trade Pokémon
!daycare              Breed/level Pokémon
!pokedex              Pokedex progress
```

### Other

```
!help                 Bot help menu
!level                Your level/XP
!leaderboard          Top earners
!ping                 Bot latency
```

---

## Deployment

### Server Setup (Linux)

```bash
# SSH into server
ssh root@192.168.1.101

# Install dependencies
apt update && apt install python3-pip python3-venv

# Clone and setup
git clone https://github.com/HauLTCE/SONARR.git /root/sonarr/bot
cd /root/sonarr/bot
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt

# Create systemd service
sudo nano /etc/systemd/system/discordbot.service
```

**Service file** (`discordbot.service`):
```ini
[Unit]
Description=Discord Bot SONARR
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=/root/sonarr/bot
ExecStart=/root/sonarr/venv/bin/python main.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

### Management

```bash
# Enable auto-start
sudo systemctl enable discordbot

# Start/stop/restart
sudo systemctl start discordbot
sudo systemctl stop discordbot
sudo systemctl restart discordbot

# View logs
sudo journalctl -u discordbot -n 50 -f
systemctl status discordbot

# Deploy code changes
scp -r . root@192.168.1.101:/root/sonarr/bot/
ssh root@192.168.1.101 "systemctl restart discordbot"
```

---

## Configuration

### `.env` File

```env
DISCORD_TOKEN=your_bot_token_here
GEMINI_API_KEY_1=key1
GEMINI_API_KEY_2=key2
GEMINI_API_KEY_3=key3
```

### Server Config (`config.json`)

```json
{
  "guild_id": {
    "general_channel": 12345,
    "music_channel": 12346,
    "market_channel": 12347,
    "admin_role": "Mods"
  }
}
```

---

## Stats & Metrics

### Database Tables: 15+
- economy, portfolio, stock_history, market_news
- loans, bankruptcies, pokemon, pokemon_daily
- items, message_cache, command_history, etc.

### Cogs: 16
- 12 feature cogs + 4 system cogs

### Commands: 100+
- 50+ economy/market commands
- 20+ Pokémon commands
- 15+ admin/utility commands

### Features:
- 8 tradeable stocks
- 300+ Pokémon species
- 30+ items in shop
- 3 gambling games
- 35+ response categories
- 8 gossip categories

---

## Development

### Adding a New Command

```python
# In cogs/example.py
from discord.ext import commands

class Example(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
    
    @commands.command()
    async def example(self, ctx, arg: str):
        """Example command."""
        await ctx.send(f"You said: {arg}")

async def setup(bot):
    await bot.add_cog(Example(bot))
```

Then load in `main.py`:
```python
await bot.load_extension("cogs.example")
```

### Adding a Cog

1. Create `cogs/newcog.py`
2. Implement `class NewCog(commands.Cog)`
3. Add `setup()` function
4. Load in `main.py` at startup

### Database Query

```python
from utils.database import db

# Get user balance
balance = db.get_user_economy(user_id)

# Add command
db.add_command(user_id, command_name)

# Update loan
db.update_loan_amount(user_id, new_amount)
```

---

## License

MIT License - See LICENSE file

---

## Support

For issues, feature requests, or questions:
- GitHub Issues: https://github.com/HauLTCE/SONARR/issues
- Contact: letrunghau2244@gmail.com

---

**Last Updated**: December 28, 2025
**Version**: 3.1
