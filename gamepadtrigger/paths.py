"""Filesystem locations for GamePadTrigger runtime data."""

from __future__ import annotations

import os
from pathlib import Path

APP_NAME = "GamePadTrigger"


def app_data_dir() -> Path:
    """Return %APPDATA%\\GamePadTrigger, creating it if needed."""
    base = os.environ.get("APPDATA")
    if not base:
        base = str(Path.home() / "AppData" / "Roaming")
    path = Path(base) / APP_NAME
    path.mkdir(parents=True, exist_ok=True)
    return path


def config_path() -> Path:
    """Path of the JSON configuration file."""
    return app_data_dir() / "config.json"


def event_log_path() -> Path:
    """Path of the timestamped event log file."""
    return app_data_dir() / "events.log"
