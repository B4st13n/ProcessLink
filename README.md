# GamePadTrigger

A system-tray process monitor for Windows 10/11. GamePadTrigger continuously
watches running processes; when a configured game process appears it launches
the associated start commands (exe, AutoHotkey script, batch file, any
program), and when the process disappears it runs the optional stop commands.

## Features

- Resident tray icon (pystray + Pillow) — no main window by default
- Left-click the tray icon to open the configuration window (tkinter)
- Live list of all running processes (psutil); toggle any process into the
  watched-games list
- Per watched game: one or more events; each event has start command(s),
  optional stop command(s), and an enabled flag
- Config persisted as JSON at `%APPDATA%\GamePadTrigger\config.json`
- Timestamped event log at `%APPDATA%\GamePadTrigger\events.log`
  (time, process, action, path) — viewable, clearable, and exportable from the UI
- Lightweight polling (~1.5 s, configurable)

## Install (Windows 11)

Requires Python 3.10+ from python.org (the official installer includes
tkinter, which is needed for the config window).

```powershell
# 1. Clone or download this repository, then:
cd C:\Users\<you>\Projects\GamePadTrigger

# 2. (Recommended) create a virtual environment
python -m venv .venv
.\.venv\Scripts\Activate.ps1

# 3. Install dependencies
pip install -r requirements.txt
```

## Run

```powershell
# Tray mode (normal): no window, just the tray icon
python main.py

# Console mode (debugging): monitor runs in the foreground, Ctrl+C to stop
python main.py --console

# Verbose logging
python main.py --verbose
```

`python -m gamepadtrigger` also works.

## Usage

1. Click the tray icon → the configuration window opens.
2. Pick a running process in the left list and click **Watch** (click
   **Unwatch** to remove it). The process list refreshes automatically.
3. Select the game in **Watched games**, then **Add event** / **Edit event**.
   Each event has:
   - a name and an **Enabled** checkbox
   - **Start commands** — one shell command per line, run when the process
     appears (e.g. `"C:\Program Files\AutoHotkey\AutoHotkey.exe" "D:\Scripts\game.ahk"`)
   - **Stop commands** — optional, one per line, run when the process
     disappears (e.g. `taskkill /IM helper.exe`)
4. Click **Save**. The config is written to
   `%APPDATA%\GamePadTrigger\config.json` and applied immediately (no restart).

Tray menu: **Open Config** (also left-click), **Reload Config** (re-read the
JSON from disk), **Open Event Log** (opens `events.log` in the default app),
**Quit**.

## Configuration format

`%APPDATA%\GamePadTrigger\config.json`:

```json
{
  "poll_interval": 1.5,
  "games": [
    {
      "process_name": "eldenring.exe",
      "events": [
        {
          "name": "Launch helper",
          "start_commands": ["\"C:\\Program Files\\AutoHotkey\\AutoHotkey.exe\" \"D:\\Scripts\\eldenring.ahk\""],
          "stop_commands": ["taskkill /IM helper.exe"],
          "enabled": true
        }
      ]
    }
  ]
}
```

- `process_name` is the process image name as shown in Task Manager
  (e.g. `eldenring.exe`).
- Commands run through `cmd.exe` without opening a console window, so they can
  be any program: `.exe`, `.bat`, `.ahk` (quoted, with the AutoHotkey
  interpreter), etc.
- A game that is already running when GamePadTrigger starts counts as
  "appeared" and fires its start commands on the first poll.

## Event log format

`%APPDATA%\GamePadTrigger\events.log`, one line per action:

```
2026-10-25 14:03:12 | process=eldenring.exe | action=START | path="C:\Program Files\AutoHotkey\AutoHotkey.exe" "D:\Scripts\eldenring.ahk"
2026-10-25 15:10:44 | process=eldenring.exe | action=STOP | path=taskkill /IM helper.exe
```

View / clear / export buttons are in the configuration window.

## Tests

```powershell
pip install pytest
python -m pytest -v
```

Covers config JSON roundtrip (and corrupt/missing-file handling), the event
log, and the appear/disappear launch/stop logic with a fake process source and
a mock launcher.

## Project layout

```
main.py                     entry point (python main.py)
gamepadtrigger/
  cli.py                    argument parsing, console mode
  paths.py                  %APPDATA%\GamePadTrigger locations
  config.py                 Config / WatchedGame / GameEvent + JSON load/save
  events.py                 timestamped event log (read/clear/export)
  launcher.py               detached command launching via cmd.exe
  monitor.py                polling loop + appear/disappear detection
  tray.py                   pystray tray icon + menu
  gui.py                    tkinter configuration window
tests/
  test_config.py            config roundtrip tests
  test_monitor.py           appear/disappear launch/stop tests (mock launcher)
  test_events.py            event log tests
```
