"""Process polling and appear/disappear detection.

The monitor snapshots the set of running process image names (psutil) every
``poll_interval`` seconds (~1-2s by default). When a watched process appears,
enabled events fire their start commands; when it disappears, they fire their
optional stop commands. Every action is recorded in the EventLog.
"""

from __future__ import annotations

import logging
import threading
from typing import Callable

import psutil

from .config import Config
from .events import EventLog
from .launcher import launch

log = logging.getLogger(__name__)

ProcessNamesFn = Callable[[], set[str]]
LauncherFn = Callable[[str], None]


def running_process_names() -> set[str]:
    """Snapshot of currently running process image names (e.g. 'game.exe')."""
    names: set[str] = set()
    for proc in psutil.process_iter(["name"]):
        try:
            name = proc.info["name"]
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
        if name:
            names.add(name)
    return names


class ProcessMonitor:
    """Polls running processes and fires start/stop commands on transitions.

    ``process_names`` and ``launcher`` are injectable so tests can substitute
    a fake process source and a mock launcher.
    """

    def __init__(
        self,
        config: Config,
        event_log: EventLog,
        process_names: ProcessNamesFn = running_process_names,
        launcher: LauncherFn = launch,
        poll_interval: float | None = None,
    ):
        self._config = config
        self._event_log = event_log
        self._process_names = process_names
        self._launcher = launcher
        self._poll_interval = (
            poll_interval if poll_interval is not None else config.poll_interval
        )
        self._lock = threading.Lock()
        self._running: set[str] = set()
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None

    @property
    def config(self) -> Config:
        with self._lock:
            return self._config

    @property
    def poll_interval(self) -> float:
        return self._poll_interval

    def reload(self, config: Config) -> None:
        """Swap in a new configuration (called when the user saves in the UI)."""
        with self._lock:
            self._config = config
            if config.poll_interval and config.poll_interval > 0:
                self._poll_interval = config.poll_interval
        log.info("Configuration reloaded (%d watched game(s))", len(config.games))

    def poll_once(self) -> None:
        """Run a single poll iteration: diff process set, fire events.

        Matching is case-insensitive: Windows reports image names in varying
        case (e.g. 'PING.EXE' vs 'ping.exe').
        """
        with self._lock:
            config = self._config
        current = {name.lower() for name in self._process_names()}
        watched = {game.process_name.lower() for game in config.games}
        appeared = (watched & current) - self._running
        disappeared = self._running - current
        for name in sorted(appeared):
            self._handle_appear(config, name)
        for name in sorted(disappeared):
            self._handle_disappear(config, name)
        self._running = watched & current

    def _handle_appear(self, config: Config, name: str) -> None:
        for game in config.games:
            if game.process_name.lower() != name:
                continue
            log.info("Process appeared: %s", game.process_name)
            for event in game.events:
                if not event.enabled:
                    continue
                for command in event.start_commands:
                    self._fire(game.process_name, "START", command)

    def _handle_disappear(self, config: Config, name: str) -> None:
        for game in config.games:
            if game.process_name.lower() != name:
                continue
            log.info("Process disappeared: %s", game.process_name)
            for event in game.events:
                if not event.enabled:
                    continue
                for command in event.stop_commands:
                    self._fire(game.process_name, "STOP", command)

    def _fire(self, process: str, action: str, command: str) -> None:
        command = command.strip()
        if not command:
            return
        self._event_log.log(process, action, command)
        try:
            self._launcher(command)
        except Exception:
            log.exception("Failed to launch %s command: %s", action.lower(), command)

    def run(self) -> None:
        """Blocking poll loop (runs until stop() is called)."""
        log.info("Monitor started (polling every %.1fs)", self._poll_interval)
        while not self._stop_event.is_set():
            try:
                self.poll_once()
            except Exception:
                log.exception("Poll iteration failed")
            self._stop_event.wait(self._poll_interval)
        log.info("Monitor stopped")

    def start(self) -> None:
        """Start the poll loop in a background daemon thread."""
        if self._thread is not None and self._thread.is_alive():
            return
        self._stop_event.clear()
        self._thread = threading.Thread(
            target=self.run, name="GamePadTriggerMonitor", daemon=True
        )
        self._thread.start()

    def stop(self) -> None:
        """Signal the poll loop to stop and wait for it to finish."""
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=5)
            self._thread = None
