"""
High Availability Manager for Sonarr Bot

This module provides leader election and failover capabilities
for running multiple bot instances across distributed machines.

Components:
- HAManager: Main coordinator for leader election
- HeartbeatService: Keep-alive service for primary detection
- FailoverHandler: Automatic takeover when primary fails
"""

from .manager import HAManager
from .heartbeat import HeartbeatService
from .config import HAConfig

__all__ = ['HAManager', 'HeartbeatService', 'HAConfig']
