"""GamePadTrigger entry point.

Run with the tray icon (default):
    python main.py

Run the monitor in the console without a tray icon (debugging):
    python main.py --console
"""

from gamepadtrigger.cli import main

if __name__ == "__main__":
    raise SystemExit(main())
