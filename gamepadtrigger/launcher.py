"""Launching of start/stop commands."""

from __future__ import annotations

import logging
import subprocess

log = logging.getLogger(__name__)

# Windows-only flag: run the child without flashing a console window.
_CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def launch(command: str) -> None:
    """Launch one command line, detached, through cmd.exe.

    The command string is passed to cmd.exe (shell=True) so it can point at
    an .exe, an AutoHotkey script (.ahk), a .bat file, or any program, e.g.:
    ``"C:\\Program Files\\AutoHotkey\\AutoHotkey.exe" "D:\\Scripts\\game.ahk"``
    """
    log.info("Launching: %s", command)
    subprocess.Popen(command, shell=True, creationflags=_CREATE_NO_WINDOW)
