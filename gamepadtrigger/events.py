"""Timestamped event log.

Each line: ``2026-10-25 14:03:12 | process=game.exe | action=START | path=C:\\Tools\\run.bat``
Stored under %APPDATA%\\GamePadTrigger\\events.log.
"""

from __future__ import annotations

import shutil
from datetime import datetime
from pathlib import Path


class EventLog:
    """Append-only timestamped log of monitor actions."""

    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def log(self, process: str, action: str, path: str = "") -> str:
        """Append one event line and return it."""
        line = (
            f"{datetime.now():%Y-%m-%d %H:%M:%S} | "
            f"process={process} | action={action} | path={path}"
        )
        with self.path.open("a", encoding="utf-8") as fh:
            fh.write(line + "\n")
        return line

    def read(self) -> list[str]:
        """Return all log lines (empty list if the file does not exist)."""
        if not self.path.exists():
            return []
        return self.path.read_text(encoding="utf-8").splitlines()

    def clear(self) -> None:
        """Truncate the log file."""
        self.path.write_text("", encoding="utf-8")

    def export(self, destination: Path) -> None:
        """Copy the log to an arbitrary destination (for 'Export' in the UI)."""
        destination = Path(destination)
        if destination.parent and not destination.parent.exists():
            destination.parent.mkdir(parents=True, exist_ok=True)
        if self.path.exists():
            shutil.copyfile(self.path, destination)
        else:
            destination.write_text("", encoding="utf-8")
