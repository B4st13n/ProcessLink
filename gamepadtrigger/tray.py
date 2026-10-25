"""System tray icon (pystray + Pillow).

Threading model: the hidden tkinter root runs its mainloop on the main
thread; the pystray icon runs its message loop on a background thread. Tray
callbacks marshal UI work back to the main thread via ``root.after``.
"""

from __future__ import annotations

import logging
import os
import threading
from pathlib import Path

import pystray
import tkinter as tk
from PIL import Image, ImageDraw

from . import paths
from .config import Config, load_config
from .events import EventLog
from .gui import ConfigWindow
from .monitor import ProcessMonitor

log = logging.getLogger(__name__)


def make_icon_image(size: int = 64) -> Image.Image:
    """Draw a simple gamepad glyph on a transparent background."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    # body
    draw.rounded_rectangle(
        [4, 16, size - 4, size - 16], radius=14, fill=(36, 96, 200, 255)
    )
    # d-pad (left)
    cx, cy = int(size * 0.32), int(size * 0.5)
    draw.rectangle([cx - 4, cy - 13, cx + 4, cy + 13], fill=(255, 255, 255, 255))
    draw.rectangle([cx - 13, cy - 4, cx + 13, cy + 4], fill=(255, 255, 255, 255))
    # buttons (right)
    ax, ay = int(size * 0.64), int(size * 0.42)
    bx, by = int(size * 0.78), int(size * 0.58)
    draw.ellipse([ax - 7, ay - 7, ax + 7, ay + 7], fill=(255, 90, 90, 255))
    draw.ellipse([bx - 7, by - 7, bx + 7, by + 7], fill=(255, 200, 90, 255))
    return img


class TrayApp:
    """Owns the tray icon, the monitor, and the (hidden) tkinter root."""

    def __init__(
        self,
        config_file: Path | None = None,
        log_file: Path | None = None,
    ):
        self.config_file = Path(config_file) if config_file else paths.config_path()
        self.log_file = Path(log_file) if log_file else paths.event_log_path()
        self.event_log = EventLog(self.log_file)
        self.log_file.touch(exist_ok=True)
        self.config: Config = load_config(self.config_file)
        self.monitor = ProcessMonitor(self.config, self.event_log)
        self._root: tk.Tk | None = None
        self._icon: pystray.Icon | None = None
        self._config_window: ConfigWindow | None = None
        self._lock = threading.Lock()

    # -- lifecycle ------------------------------------------------------

    def run(self) -> None:
        """Run the tray app (blocks until Quit)."""
        self._root = tk.Tk()
        self._root.withdraw()  # no main window by default
        self._root.title("GamePadTrigger")
        self._root.protocol("WM_DELETE_WINDOW", self.quit)

        self.monitor.start()

        self._icon = pystray.Icon(
            "GamePadTrigger",
            make_icon_image(),
            "GamePadTrigger",
            menu=pystray.Menu(
                pystray.MenuItem(
                    "Open Config", self._on_open_config, default=True
                ),
                pystray.MenuItem("Reload Config", self._on_reload_config),
                pystray.MenuItem("Open Event Log", self._on_open_log),
                pystray.MenuItem("Quit", self._on_quit),
            ),
        )
        threading.Thread(
            target=self._icon.run, name="GamePadTriggerTray", daemon=True
        ).start()
        log.info("GamePadTrigger running (tray icon active, config: %s)", self.config_file)
        try:
            self._root.mainloop()
        finally:
            self.monitor.stop()

    def quit(self) -> None:
        """Stop monitor + tray icon and close the app."""

        def shutdown() -> None:
            self.monitor.stop()
            if self._icon is not None:
                self._icon.stop()
            if self._root is not None:
                self._root.destroy()

        if self._root is not None:
            self._root.after(0, shutdown)

    # -- shared helpers ---------------------------------------------------

    def apply_config(self, config: Config) -> None:
        """Persist and activate a config edited in the UI."""
        self.config = config
        self.monitor.reload(config)

    def _notify(self, message: str) -> None:
        try:
            if self._icon is not None:
                self._icon.notify(message, "GamePadTrigger")
        except Exception:
            log.exception("Tray notification failed")

    # -- tray menu callbacks (run on the pystray thread) ------------------

    def _on_open_config(self, _icon=None, _item=None) -> None:
        def open_window() -> None:
            with self._lock:
                try:
                    if (
                        self._config_window is not None
                        and self._config_window.winfo_exists()
                    ):
                        self._config_window.lift()
                        self._config_window.focus_force()
                        return
                except tk.TclError:
                    self._config_window = None
                self._config_window = ConfigWindow(self._root, self)

        if self._root is not None:
            self._root.after(0, open_window)

    def _on_reload_config(self, _icon=None, _item=None) -> None:
        config = load_config(self.config_file)

        def apply() -> None:
            self.apply_config(config)

        if self._root is not None:
            self._root.after(0, apply)
        self._notify("Configuration reloaded")

    def _on_open_log(self, _icon=None, _item=None) -> None:
        try:
            os.startfile(str(self.log_file))  # type: ignore[attr-defined]
        except OSError:
            log.exception("Could not open event log file")
            self._notify(f"Event log: {self.log_file}")

    def _on_quit(self, _icon=None, _item=None) -> None:
        self.quit()
