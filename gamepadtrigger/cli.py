"""Command-line entry point logic."""

from __future__ import annotations

import argparse
import logging
import time

from . import paths
from .config import load_config
from .events import EventLog
from .monitor import ProcessMonitor


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="GamePadTrigger",
        description="System-tray monitor that launches scripts when games start/stop.",
    )
    parser.add_argument(
        "--console",
        action="store_true",
        help="run the monitor in the console without the tray icon (debugging)",
    )
    parser.add_argument(
        "--verbose", action="store_true", help="enable debug logging"
    )
    args = parser.parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    if args.console:
        config = load_config(paths.config_path())
        event_log = EventLog(paths.event_log_path())
        monitor = ProcessMonitor(config, event_log)
        monitor.start()
        print(
            f"GamePadTrigger console monitor running "
            f"(config: {paths.config_path()}, log: {paths.event_log_path()}). "
            "Press Ctrl+C to stop."
        )
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            pass
        finally:
            monitor.stop()
        return 0

    from .tray import TrayApp

    TrayApp().run()
    return 0
