# SONARR Discord bot — bot-only image. Lavalink + yt-cipher stay on the host.
FROM python:3.13-slim

# OS build deps for the ML wheels (sentence-transformers, spacy, etc.).
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Torch first, from the CPU wheel index, so CUDA is never pulled in.
# (Mirrors the proven install in deploy.py.)
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu

# Remaining Python deps.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# spaCy English model — a pip package, NOT part of the mounted HF cache.
RUN python -m spacy download en_core_web_sm

# Models and data are NOT baked in — they mount at runtime (see docker-compose.yml).
# Point the ML libraries at the mounted, pre-populated caches and forbid re-download.
ENV HF_HOME=/models/huggingface \
    NLTK_DATA=/models/nltk_data \
    TRANSFORMERS_OFFLINE=1 \
    HF_HUB_OFFLINE=1 \
    PYTHONUNBUFFERED=1

# App code last so dependency layers stay cached across code changes.
COPY . .

CMD ["python", "main.py"]
