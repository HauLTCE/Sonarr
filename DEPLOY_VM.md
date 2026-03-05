# Deploy Python Bot + Lavalink on VM (Debian/Ubuntu)

This guide runs this bot (`main.py`) and Lavalink on the same Linux VM.

## 1) Prepare VM

```bash
sudo apt update
sudo apt install -y git curl wget python3 python3-venv python3-pip openjdk-21-jre-headless
```

## 2) Deploy bot source

```bash
sudo mkdir -p /opt/sonarr-bot
sudo chown -R $USER:$USER /opt/sonarr-bot
cd /opt/sonarr-bot

# If using git:
git clone <YOUR_REPO_URL> .

# If you already have source code, copy it into /opt/sonarr-bot instead.
```

Create virtualenv and install requirements:

```bash
cd /opt/sonarr-bot
python3 -m venv .venv
source .venv/bin/activate
pip install --upgrade pip
pip install -r requirements.txt
```

## 3) Create `.env` for bot

```bash
cat > /opt/sonarr-bot/.env <<'EOF'
DISCORD_TOKEN=YOUR_DISCORD_BOT_TOKEN
LAVALINK_URI=http://127.0.0.1:2333
LAVALINK_PASSWORD=youshallnotpass

# Optional AI keys:
GEMINI_API_KEY_1=
GEMINI_API_KEY_2=
GEMINI_API_KEY_3=

# Optional Ollama:
OLLAMA_API_URL=
OLLAMA_MODEL=
EOF
```

## 4) Install Lavalink + YouTube plugin

```bash
sudo mkdir -p /opt/lavalink/plugins
cd /opt/lavalink

sudo wget -O Lavalink.jar \
  https://github.com/lavalink-devs/Lavalink/releases/download/4.2.1/Lavalink.jar

sudo wget -O plugins/youtube-plugin-1.18.0.jar \
  https://maven.lavalink.dev/releases/dev/lavalink/youtube/youtube-plugin/1.18.0/youtube-plugin-1.18.0.jar
```

Create `/opt/lavalink/application.yml`:

```bash
sudo tee /opt/lavalink/application.yml > /dev/null <<'YAML'
server:
  port: 2333
  address: 127.0.0.1

lavalink:
  server:
    password: "youshallnotpass"
    sources:
      youtube: false
      soundcloud: true
      bandcamp: true
      twitch: true
      vimeo: true
      http: true
      local: false
  plugins:
    - dependency: "dev.lavalink.youtube:youtube-plugin:1.18.0"
      snapshot: false

plugins:
  youtube:
    enabled: true
    allowSearch: true
    allowDirectVideoIds: true
    allowDirectPlaylistIds: true
    clients:
      - MUSIC
      - WEB
      - WEBEMBEDDED
      - ANDROID_VR
    remoteCipher:
      url: "https://cipher.kikkia.dev/"
      userAgent: "sonarr-bot"
YAML
```

## 5) Create systemd service for Lavalink

```bash
sudo tee /etc/systemd/system/lavalink.service > /dev/null <<'EOF'
[Unit]
Description=Lavalink Node
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/lavalink
ExecStart=/usr/bin/java -jar /opt/lavalink/Lavalink.jar
Restart=always
RestartSec=5
User=root

[Install]
WantedBy=multi-user.target
EOF
```

## 6) Create systemd service for Python bot

```bash
sudo tee /etc/systemd/system/sonarrbot.service > /dev/null <<'EOF'
[Unit]
Description=SONARR Discord Bot (Python)
After=network.target lavalink.service
Wants=lavalink.service

[Service]
Type=simple
WorkingDirectory=/opt/sonarr-bot
ExecStart=/opt/sonarr-bot/.venv/bin/python -u /opt/sonarr-bot/main.py
Restart=always
RestartSec=5
User=root

[Install]
WantedBy=multi-user.target
EOF
```

## 7) Start services

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now lavalink
sudo systemctl enable --now sonarrbot
```

## 8) Quick verification

```bash
# Lavalink should return JSON:
curl -s -H "Authorization: youshallnotpass" http://127.0.0.1:2333/v4/info

# Check service status:
sudo systemctl status lavalink --no-pager -l
sudo systemctl status sonarrbot --no-pager -l
```

Realtime logs:

```bash
sudo journalctl -u lavalink -f
sudo journalctl -u sonarrbot -f
```

## 9) Common operations

```bash
# Restart
sudo systemctl restart lavalink
sudo systemctl restart sonarrbot

# Stop / Start
sudo systemctl stop sonarrbot
sudo systemctl start sonarrbot

# Last 100 log lines
sudo journalctl -u sonarrbot -n 100 --no-pager
sudo journalctl -u lavalink -n 100 --no-pager
```

## 10) Update bot on VM

```bash
cd /opt/sonarr-bot
git pull --ff-only
source .venv/bin/activate
pip install -r requirements.txt
sudo systemctl restart sonarrbot
```

## Troubleshooting

- `Could not connect to Lavalink player...`
  - Check Lavalink service: `sudo systemctl status lavalink`
  - Check `.env` values match `application.yml` (`LAVALINK_URI`, `LAVALINK_PASSWORD`).

- `No nodes are currently assigned to the wavelink.Pool in a CONNECTED state`
  - Lavalink is down or just restarted.
  - Restart bot service: `sudo systemctl restart sonarrbot`.

- YouTube plays unstable
  - Keep Lavalink + youtube-plugin updated.
  - For better stability, host your own remoteCipher instead of public one.
