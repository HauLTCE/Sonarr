# SONARR Discord Bot 🤖

A feature-rich Discord bot with a sarcastic diva personality, advanced economy system, stock market simulation, loan shark mechanics, gambling, Pokémon collection, and AI-driven interactions.

![Discord.py](https://img.shields.io/badge/Discord.py-2.4.0-blue)
![Python](https://img.shields.io/badge/Python-3.10+-green)
![License](https://img.shields.io/badge/License-MIT-yellow)

---

## 🌟 Features Overview

| Feature | Description |
|---------|-------------|
| 💰 **Economy** | Wallet/bank system with daily rewards, work commands, and transactions |
| 📈 **Stock Market** | 8 tradeable stocks with hourly updates, news cycles, and advanced balancing |
| 🦈 **Loan Shark** | Credit-based lending with interest, collateral, and bankruptcy system |
| 🎰 **Gambling** | Slots, blackjack, roulette, coinflip, and poker |
| 🔴 **Pokémon** | Catch, trade, and collect Pokémon with passive income bonuses |
| 🎭 **AI Personality** | Gemini-powered responses with smart caching and sarcastic attitude |
| 🎵 **Music** | YouTube playback with queue management |
| 🛡️ **Moderation** | Kick, ban, mute, and channel management |

---

## 📋 Table of Contents

- [Installation](#installation)
- [Configuration](#configuration)
- [Economy System](#-economy-system)
- [Stock Market](#-stock-market)
- [Loan Shark System](#-loan-shark-system)
- [Gambling](#-gambling)
- [Pokémon System](#-pokémon-system)
- [AI Personality](#-ai-personality)
- [Commands Reference](#-commands-reference)
- [Architecture](#-architecture)
- [Deployment](#-deployment)

---

## Installation

### Prerequisites
- Python 3.10+
- Discord Bot Token
- Google Gemini API Keys (1-3 recommended for quota rotation)

### Setup

```bash
# Clone the repository
git clone https://github.com/HauLTCE/SONARR.git
cd SONARR

# Create virtual environment
python -m venv venv
source venv/bin/activate  # Linux/Mac
# or: venv\Scripts\activate  # Windows

# Install dependencies
pip install -r requirements.txt

# Configure environment
cp .env.example .env
# Edit .env with your tokens
```

### Dependencies

```
discord.py==2.4.0
google-genai>=1.0.0
yt-dlp==2024.12.23
PyNaCl==1.5.0
python-dotenv==1.0.0
psutil==6.0.0
aiohttp==3.9.1
```

---

## Configuration

### Environment Variables (`.env`)

```env
DISCORD_TOKEN=your_bot_token
GEMINI_API_KEY_1=key1
GEMINI_API_KEY_2=key2
GEMINI_API_KEY_3=key3
```

### Server Configuration

The bot uses `config.json` for per-server settings:
- `general_channel` - Main chat channel
- `music_channel` - Music command channel
- `market_channel` - Stock market announcements
- `announce_channel` - Bot announcements

---

## 💰 Economy System

### Core Features
- **Dual Currency**: Wallet (spending) and Bank (savings)
- **Daily Rewards**: `!daily` with streak bonuses
- **Work System**: `!work` with Pokémon passive income
- **Transactions**: Deposit, withdraw, transfer

### Commands

| Command | Description |
|---------|-------------|
| `!balance` | View wallet, bank, and total balance |
| `!daily` | Claim daily reward ($100-500 + streaks) |
| `!work` | Earn $50-300 (Pokémon boost up to 25%) |
| `!deposit <amount>` | Move money to bank |
| `!withdraw <amount>` | Move money to wallet |
| `!give @user <amount>` | Transfer to another user |
| `!donate <amount>` | Donate to the bot |

### Passive Income Formula

```
Base Work: $50-300
Pokémon Bonus = Rarity% + (Level × 0.1%)
Max Bonus: 25%

Example: Rare Pokémon (10%) at Level 50 = 15% bonus
```

---

## 📈 Stock Market

### Available Stocks

| Ticker | Company | Base Price | Volatility |
|--------|---------|------------|------------|
| LABOR | Labor Industries | $100 | Medium |
| TECH | TechCorp Holdings | $250 | High |
| BANK | First National Bank | $500 | Low |
| MEME | Meme Stonks Inc | $50 | Extreme |
| PKMN | Pokemon Corp | $150 | Medium |
| FOOD | FoodChain Ltd | $75 | Low |
| ENRG | Energy Dynamics | $200 | High |
| GAMB | GambleCorp | $100 | Extreme |

### Price Balancing Algorithm

The stock market uses a sophisticated 4-factor balancing system:

**1. Rubber Band to Base Price**
```python
if price > base_price × 1.5:  # Overvalued
    crash_chance increases, boom_chance decreases
if price < base_price × 0.7:  # Undervalued
    boom_chance increases, crash_chance decreases
```

**2. Market Cap Pressure**
- Total user holdings affect volatility
- High demand + overvalued = bubble risk
- High demand + undervalued = buying pressure

**3. Momentum Reversals**
- 2+ consecutive gains → increased crash probability
- 2+ consecutive losses → recovery bounce probability

**4. News Sentiment**
- Recent positive news → slight correction bias
- Recent negative news → slight recovery bias

### Market Events

- **Hourly Updates**: Prices fluctuate ±1-5% normally
- **Hourly News**: 1-4 news items affecting specific stocks
- **Crashes**: 2% chance, -30% price impact
- **Booms**: 3% chance, +30% price impact
- **8 News Categories**: Earnings, layoffs, partnerships, seasonal, etc.

### Commands

| Command | Description |
|---------|-------------|
| `!stocks` | View all stock prices and changes |
| `!buy_stock <ticker> <shares>` | Purchase shares |
| `!sell_stock <ticker> <shares>` | Sell shares |
| `!portfolio` | View your holdings with P/L |
| `!stock_info <ticker>` | Detailed ticker information |
| `!market_news` | Recent market news |

---

## 🦈 Loan Shark System

### Credit Tiers

| Tier | Min Score | Interest Rate | Max Loan |
|------|-----------|---------------|----------|
| Excellent | 200+ | 3% daily | $5,000 |
| Good | 100-199 | 5% daily | $3,000 |
| Fair | 50-99 | 10% daily | $1,500 |
| Poor | 0-49 | 15% daily | $1,000 |

### Credit Score Factors
- ✅ Work activity (+1 per $100 earned)
- ✅ Gambling wins (+5 per win)
- ❌ Gambling losses (-2 per loss)
- ✅ Loan repayment (+20 on full repay)
- ❌ Loan default (-50 on bankruptcy)

### Loan Terms
- **Duration**: 14 days
- **Grace Period**: 7 days before enforcement
- **Interest**: Compounds daily
- **Collateral**: Optional Pokémon as security

### Enforcement Timeline

| Days Overdue | Action |
|--------------|--------|
| 0-7 | Grace period, friendly reminders |
| 7-14 | Wallet seizure (10%), rename to "Debtor" |
| 14-21 | Escalated seizure (20%), timeouts |
| 21+ | Collateral seizure, severe penalties |

### Bankruptcy
- Wipes all debt
- **3-day "Financial Ruin" shame role**
- 50% work earnings for 3 days
- 7-day cooldown before borrowing again

### Commands

| Command | Description |
|---------|-------------|
| `!loan <amount>` | Request a loan |
| `!loan_status` | View debt and interest |
| `!pay_loan <amount>` | Make a payment |
| `!bankruptcy` | Declare bankruptcy |
| `!credit_score` | Check your credit rating |

---

## 🎰 Gambling

### Slots (`!slots <amount>`)
- **Minimum Bet**: $50
- **Symbols**: 9 emojis
- **Payouts**: 3-match = 6x, 2-match = 2.5x
- **Variance Penalty**: Higher bets = slightly lower multipliers

### Blackjack (`!blackjack <amount>`)
- Standard 21 rules
- Hit, Stand, Double Down
- Dealer hits on <17
- Ace = 1 or 11

### Roulette (`!roulette <amount> <choice>`)
- Bet on: red, black, odd, even, or 0-36
- Number hit = 35x
- Color/parity = 2x

### Coinflip (`!coinflip <amount> <heads/tails>`)
- 50/50 odds
- Win = 2x

### Duel (`!duel @user <amount>`)
- Challenge another player
- Both must accept
- Winner takes all

### Pokémon Gambling Modifiers
- **Win Bonus**: Up to +18% based on Pokémon rarity
- **Loss Refund**: Up to 18% of losses returned
- **Bet Cap**: Bonuses only apply up to $1,000 bets

---

## 🔴 Pokémon System

### Hunting Zones
- Water, Fire, Grass, Electric, Psychic, Dragon, etc.
- Each zone has unique species pool
- Cooldown between hunts

### Rarity Tiers

| Rarity | Catch Rate | Passive Bonus |
|--------|------------|---------------|
| Common | 80% | +5% work |
| Uncommon | 60% | +7% work |
| Rare | 40% | +10% work |
| Epic | 20% | +13% work |
| Legendary | 5% | +18% work |

### Features
- **Collection**: Catch and store Pokémon
- **Trading**: Trade with other players
- **Daycare**: Level up Pokémon over time
- **Passive Income**: Equipped Pokémon boost earnings
- **Loan Collateral**: Risk Pokémon for larger loans

### Commands

| Command | Description |
|---------|-------------|
| `!hunt <zone>` | Hunt Pokémon in a zone |
| `!pokemon` | View your collection |
| `!equip <pokemon_id>` | Equip for passive bonus |
| `!trade @user` | Initiate trade |
| `!pokedex` | View completion progress |
| `!daycare` | Manage daycare |

---

## 🎭 AI Personality

### Classification Pipeline (Priority Order)

```
User Message (@Sonarr)
    ↓
1. EMPTY CHECK (0 words) → random response
    ↓
2. SHORT MESSAGE (≤2 words) → keyword_classify()
    ↓
3. SMART CLASSIFY (3+ words)
    ├─ Pattern Matching (Subject-Action-Target) ← confidence 3-4
    └─ Keyword Matching ← confidence 1-2
    ↓
4. CACHE LOOKUP (exact hash + fuzzy word match)
    ↓
5. RATE LIMIT CHECK (3 calls/user/hour)
    ↓
6. GEMINI API CALL → cache result
    ↓
7. FAILOVER: Key/Model rotation → Random fallback
```

### Pattern Matching Engine (`sonarr/patterns.py`)

The pattern matcher uses **Subject-Action-Target anchoring** for context-aware classification:

| Pattern Type | Example | Result |
|--------------|---------|--------|
| Self + hate + target | "I hate you" | insult |
| Self + love + target | "I love you" | affection |
| Target + insult | "You're stupid" | insult |
| Third-party + anything | "He's dumb" | gossip |
| Target + hate + self | "You hate me" | confusion |
| Object + insult | "This is stupid" | insult |

**Advanced Features:**

| Feature | Example | Detection |
|---------|---------|-----------|
| **Clause Splitting** | "I like you, **but** you're annoying" | Prioritizes after-clause |
| **Sarcasm Markers** | "**Oh wow**, you're SO smart" | Detects irony |
| **Backhanded Compliments** | "You're **smarter than you look**" | Insult disguised as praise |
| **Conditional Insults** | "**If you weren't** so dumb..." | Hidden insult |
| **Conditional Threats** | "**If you keep this up**, I'm gonna hate you" | Future harm warning |
| **Hedged Insults** | "**I think you might be** annoying" | Softened insult |
| **Negation Handling** | "I **don't** hate you" | Flips category |
| **Multi-Target** | "He sucks, **you suck**, everyone sucks" | Finds you-clause |
| **Collective Nouns** | "**People say** you're trash" | Insult, not gossip |
| **Gen-Z Slang** | "ur lowkey mid ngl" | Modern vocabulary |

**Supported Vocabulary:**
- Pronouns: `u`, `ur`, `bro`, `sis`, `dude`, `yall`
- Intensifiers: `lowkey`, `highkey`, `deadass`, `fr`, `frfr`, `ngl`, `tbh`, `ong`, `no cap`
- Insults: `mid`, `cringe`, `salty`, `toxic`, `sus`, `cap`, `L`, `ratio`, `npc`, `simp`
- Praise: `based`, `goated`, `fire`, `lit`, `bussin`, `iconic`, `W`, `slay`, `valid`

### Response Categories (35+)
greeting, thanks, goodbye, question, confusion, insult, affection, vent, excitement, complaint, joke, help, agreement, disagreement, bored, flirt, brag, flex, beg, chitchat, advice, compliment, request, apology, statement, sarcasm, threat, command, praise, spam, excuse, overshare, challenge, opinion, lie, guilt, gossip, random

### Auto-Behaviors

| Feature | Description |
|---------|-------------|
| **Auto-Rob** | 3% chance every 10-30 min to steal from users |
| **Idle Chat** | Breaks silence after 4+ hours |
| **Debt Enforcement** | 20% chance to harass debtors |
| **Rude Punishment** | 30% chance to rob users who swear |
| **Gossip** | 10% chance to comment on mentioned users |

### Status-Based Gossip

| Status | Trigger | Example |
|--------|---------|---------|
| Bankrupt | In shame period | "They hit rock bottom. I was there. I pushed." |
| Rich | >$50,000 | "Ah yes, the walking ATM." |
| Poor | <$500 | "Can't rob someone with nothing." |
| Investor | Has stocks | "I've seen their portfolio. Bold strategy." |
| Debtor | Has loan | "I've got their loan papers right here." |
| Pokémon | Has collection | "Collects Pokemon. At their age. Bold." |
| Loudmouth | Level 15+ & 50+ commands | "High level and no off button." |

---

## 📜 Commands Reference

### Economy
```
!balance, !daily, !work, !deposit, !withdraw, !give, !donate
!inventory, !shop, !buy, !sell, !use
```

### Stock Market
```
!stocks, !buy_stock, !sell_stock, !portfolio, !stock_info, !market_news
```

### Loans
```
!loan, !loan_status, !pay_loan, !bankruptcy, !credit_score
```

### Gambling
```
!slots, !blackjack, !roulette, !coinflip, !duel, !poker
```

### Pokémon
```
!hunt, !pokemon, !equip, !trade, !pokedex, !daycare, !release
```

### Music
```
!play, !skip, !stop, !queue, !pause, !resume, !volume, !nowplaying
```

### Moderation
```
!kick, !ban, !unban, !mute, !unmute, !clear, !warn
```

### Utility
```
!help, !ping, !level, !leaderboard, !serverinfo, !userinfo
```

### Admin (Owner Only)
```
!bot_balance, !bot_config, !reload, !eval
```

---

## 🏗 Architecture

### Directory Structure

```
bot/
├── main.py                 # Bot initialization & startup
├── requirements.txt        # Python dependencies
├── items.json              # Shop inventory
├── pokemon_species.json    # Pokémon data
├── pokemon_moves.json      # Move database
├── pokemon_zones.json      # Hunt zones
│
├── sonarr/                 # AI Classification Module
│   ├── __init__.py         # Module exports
│   ├── classifier.py       # Smart classifier (pattern → keyword → cache → AI)
│   ├── keywords.py         # Keyword maps & stopwords
│   ├── patterns.py         # Pattern matching engine (600+ lines)
│   ├── responses.py        # Cold responses & templates (380+ lines)
│   └── time_utils.py       # Sleep/grace period management
│
├── cogs/                   # Command modules (16 cogs)
│   ├── admin.py            # Admin commands
│   ├── bot_economy.py      # Bot's own economy
│   ├── bot_personality.py  # AI personality (1,200+ lines)
│   ├── fun.py              # Fun commands
│   ├── gambling.py         # Gambling games (1,000+ lines)
│   ├── game.py             # Economy & shop
│   ├── levels.py           # XP & leveling
│   ├── loans.py            # Loan shark (650+ lines)
│   ├── moderation.py       # Mod tools
│   ├── music.py            # Music playback
│   ├── pokemon.py          # Pokémon system
│   ├── pokemon_views.py    # Pokémon UI
│   ├── stocks.py           # Stock market (1,300+ lines)
│   ├── utility.py          # Utility commands
│   ├── views.py            # Discord UI components
│   └── welcome.py          # Welcome messages
│
└── utils/                  # Shared utilities (15 modules)
    ├── cache.py            # YouTube/message caching
    ├── checks.py           # Permission checks
    ├── command_history.py  # Command logging
    ├── config.py           # Server configuration
    ├── database.py         # SQLite ORM (1,500+ lines)
    ├── economy.py          # Economy manager
    ├── help.py             # Custom help formatter
    ├── internal_commands.py # Internal command parser
    ├── logger.py           # Logging setup
    ├── music_queue.py      # Music queue management
    ├── pokemon_system.py   # Pokémon logic
    ├── premade_answers.py  # Cold responses (300+ lines)
    ├── response_effects.py # Effect processing
    ├── spam.py             # Spam detection
    └── ytdl.py             # YouTube-DL wrapper
```

### Database Schema (15+ Tables)

- **economy** - User balances, daily streaks
- **inventory** - User items
- **portfolio** - Stock holdings
- **stock_history** - Price history
- **market_news** - News items
- **loans** - Active loans
- **bankruptcies** - Bankruptcy records
- **pokemon** - Pokémon collection
- **pokemon_daily** - Hunt cooldowns
- **message_cache** - AI response cache
- **command_history** - Command tracking
- And more...

### Data Flow

```
User Message
    ↓
on_message() listener
    ├─ Spam check
    ├─ Debt enforcement (20%)
    ├─ Rude punishment (30%)
    ├─ Gossip trigger (10%)
    └─ AI Classification
        ├─ Keyword match
        ├─ Cache lookup
        ├─ Gemini API
        └─ Rate limiting
    ↓
Response Selection
    ├─ COLD_RESPONSES (300+ lines)
    ├─ Status-based gossip
    └─ Effect processing
    ↓
Send Reply
```

---

## 🚀 Deployment

### Local Development

```bash
python main.py
```

### Server Deployment (Linux)

```bash
# Create systemd service
sudo nano /etc/systemd/system/discordbot.service
```

**Service file:**
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

**Commands:**
```bash
# Enable and start
sudo systemctl enable discordbot
sudo systemctl start discordbot

# View logs
journalctl -u discordbot -f

# Restart after updates
sudo systemctl restart discordbot
```

## 📊 Statistics

- **Cogs**: 16 command modules
- **Utils**: 15 utility modules
- **Commands**: 100+ commands
- **Response Categories**: 35+
- **Gossip Categories**: 9
- **Stocks**: 8 tradeable
- **Pokémon Species**: 300+
- **Database Tables**: 15+
- **Lines of Code**: 10,000+

---

## 🔧 Development

### Adding a New Command

```python
# In cogs/example.py
@commands.command()
async def mycommand(self, ctx, arg: str):
    """Command description."""
    await ctx.send(f"You said: {arg}")
```

### Adding a New Cog

1. Create `cogs/newcog.py`
2. Define class inheriting `commands.Cog`
3. Add `async def setup(bot)` function
4. Bot auto-loads from cogs directory

### Database Queries

```python
from utils.database import db

# Get user balance
balance = db.get_user_economy(user_id)

# Update loan
db.update_loan_amount(user_id, new_amount)

# Get stock portfolio
portfolio = db.get_portfolio(user_id)
```

---

## 📄 License

MIT License - See [LICENSE](LICENSE) for details.

---

## 🤝 Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/amazing`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing`)
5. Open Pull Request

---

## 📞 Support

- **GitHub Issues**: [Report bugs](https://github.com/HauLTCE/SONARR/issues)
- **Email**: letrunghau2244@gmail.com

---

**Last Updated**: December 29, 2025  
**Version**: 4.0
