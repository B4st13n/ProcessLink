"""Configuration model and JSON persistence.

Config is stored as JSON under %APPDATA%\\GamePadTrigger\\config.json:

{
  "poll_interval": 1.5,
  "games": [
    {
      "process_name": "eldenring.exe",
      "events": [
        {
          "name": "Launch helper",
          "start_commands": ["C:\\\\Tools\\\\helper.ahk"],
          "stop_commands": ["taskkill /IM helper.exe"],
          "enabled": true
        }
      ]
    }
  ]
}
"""

from __future__ import annotations

import json
import logging
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

log = logging.getLogger(__name__)

DEFAULT_POLL_INTERVAL = 1.5


@dataclass
class GameEvent:
    """One event attached to a watched game.

    start_commands run when the process appears, stop_commands (optional)
    run when it disappears. Each command is a shell command line executed
    through cmd.exe (exe, .ahk, .bat, or any program).
    """

    name: str = "New event"
    start_commands: list[str] = field(default_factory=list)
    stop_commands: list[str] = field(default_factory=list)
    enabled: bool = True

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "GameEvent":
        return cls(
            name=str(data.get("name", "New event")),
            start_commands=[str(c) for c in data.get("start_commands", [])],
            stop_commands=[str(c) for c in data.get("stop_commands", [])],
            enabled=bool(data.get("enabled", True)),
        )


@dataclass
class WatchedGame:
    """A watched process image name (e.g. 'eldenring.exe') and its events."""

    process_name: str
    events: list[GameEvent] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {
            "process_name": self.process_name,
            "events": [event.to_dict() for event in self.events],
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "WatchedGame":
        return cls(
            process_name=str(data.get("process_name", "")),
            events=[GameEvent.from_dict(e) for e in data.get("events", [])],
        )


@dataclass
class Config:
    """Root configuration."""

    games: list[WatchedGame] = field(default_factory=list)
    poll_interval: float = DEFAULT_POLL_INTERVAL

    def watched_names(self) -> set[str]:
        return {game.process_name for game in self.games}

    def to_dict(self) -> dict[str, Any]:
        return {
            "poll_interval": self.poll_interval,
            "games": [game.to_dict() for game in self.games],
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Config":
        return cls(
            games=[WatchedGame.from_dict(g) for g in data.get("games", [])],
            poll_interval=float(data.get("poll_interval", DEFAULT_POLL_INTERVAL)),
        )


def load_config(path: Path) -> Config:
    """Load config from JSON. Missing or corrupt files yield a default Config."""
    try:
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError:
        return Config()
    except (OSError, json.JSONDecodeError) as exc:
        log.warning("Could not read config %s (%s); using defaults", path, exc)
        return Config()
    if not isinstance(raw, dict):
        log.warning("Config %s has unexpected shape; using defaults", path)
        return Config()
    try:
        return Config.from_dict(raw)
    except (TypeError, ValueError) as exc:
        log.warning("Config %s is invalid (%s); using defaults", path, exc)
        return Config()


def save_config(config: Config, path: Path) -> None:
    """Write config as pretty-printed JSON (atomic via temp file + replace)."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(config.to_dict(), indent=2) + "\n", encoding="utf-8")
    tmp.replace(path)
