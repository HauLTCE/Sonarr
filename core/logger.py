import logging
import os
import sys

class BracketFormatter(logging.Formatter):
    LEVEL_MAP = {
        "DEBUG": "[Debug]",
        "INFO": "[Information]",
        "WARNING": "[Warning]",
        "ERROR": "[Critical]",
        "CRITICAL": "[Critical]"
    }
    
    def format(self, record):
        prefix = self.LEVEL_MAP.get(record.levelname, f"[{record.levelname}]")
        original = super().format(record)
        return f"{prefix} {original}"


def _resolve_level(name, default):
    """Map a level NAME (e.g. from an env var) to a logging level constant.

    Unknown / empty values fall back to `default` rather than crashing startup.
    """
    if not name:
        return default
    level = getattr(logging, str(name).strip().upper(), None)
    return level if isinstance(level, int) else default


def setup_logging():
    """Configure bot logging."""
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(BracketFormatter("%(message)s"))
    # Handler passes everything through; each logger's own level does the gating
    # (so the "elaine" logger can emit DEBUG while "bot" stays at INFO).
    handler.setLevel(logging.DEBUG)
    
    logger = logging.getLogger("bot")
    logger.setLevel(logging.INFO)
    if not logger.handlers:
        logger.addHandler(handler)

    # E.L.A.I.N.E chat engine — its own logger namespace so its verbosity is tunable
    # independently of the rest of the bot. Set ELAINE_LOG_LEVEL in the environment
    # (DEBUG shows the full per-turn pipeline: sentiment, affect deltas, FSM routing,
    # cooldown; INFO — the default — shows a one-line summary per message). Shares the
    # bot's stdout handler so everything lands in the same journal/console stream.
    elaine_level = _resolve_level(os.getenv("ELAINE_LOG_LEVEL"), logging.INFO)
    elaine_logger = logging.getLogger("elaine")
    elaine_logger.setLevel(elaine_level)
    if not elaine_logger.handlers:
        elaine_logger.addHandler(handler)
    # Own handler already attached; don't also bubble up to the root logger (avoids
    # duplicate lines if anything ever calls logging.basicConfig()).
    elaine_logger.propagate = False
    
    logging.getLogger("discord").setLevel(logging.INFO)
    return logger
