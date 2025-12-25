import logging
import sys

class BracketFormatter(logging.Formatter):
    LEVEL_MAP = {
        "INFO": "[Information]",
        "WARNING": "[Warning]",
        "ERROR": "[Critical]",
        "CRITICAL": "[Critical]"
    }
    
    def format(self, record):
        prefix = self.LEVEL_MAP.get(record.levelname, f"[{record.levelname}]")
        original = super().format(record)
        return f"{prefix} {original}"

def setup_logging():
    """Configure bot logging."""
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(BracketFormatter("%(message)s"))
    
    logger = logging.getLogger("bot")
    logger.setLevel(logging.DEBUG)
    if not logger.handlers:
        logger.addHandler(handler)
    
    logging.getLogger("discord").setLevel(logging.INFO)
    return logger
