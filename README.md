# Spawn

A system-tray process monitor for Windows 10/11, written in C# (.NET 8, WPF).
Spawn continuously watches running processes; when a configured process
appears it launches the associated start commands (exe, AutoHotkey script,
batch file, any program), and when the process disappears it runs the optional
stop commands.

This is a full rewrite of the original Python app (GamePadTrigger). The C# app
is the only deliverable; the Python sources were removed (history remains in
git).

## Features

- Resident tray icon — left-click (or tray menu → **Open**) toggles the window
- **One single list** of processes on the left, occupying the full height of
  the window: every running process, plus pinned processes that are no longer
  running (kept at the top)
- Columns: **Pin** button, **Live** indicator (green dot = the process is
  running right now), process name, PID, path
- **Pin button** on every row — a pinned process stays at the top of the list
  even when it is no longer running; pinned processes are persisted in the
  JSON config
- The list refreshes automatically (~1.5 s, configurable) — no manual refresh
- Right panel: select any process to edit its **start/stop events** (name,
  enabled flag, start commands, stop commands)
- Config persisted as JSON at `%APPDATA%\Spawn\config.json`.
  On first run, if that file is missing, Spawn copies an existing
  `%APPDATA%\ProcessLink\config.json`, or else
  `%APPDATA%\GamePadTrigger\config.json`. The older files are left in place.
- Timestamped event log at `%APPDATA%\Spawn\events.log`
- Single instance (a second launch brings up a notice). A leftover
  ProcessLink process that still holds the old single-instance mutex also
  blocks Spawn from starting, and Spawn keeps that mutex name reserved so the
  old exe will not start beside it.
- Console/debug mode: `Spawn.exe --console` prints appear/disappear
  transitions to the console

## Requirements

- Windows 10/11, x64
- To build: .NET 8 SDK (`dotnet --version` → 8.x)
- To run the published app: nothing else — the release ships a single-file
  self-contained `Spawn.exe`

## Build & run from source

```powershell
cd C:\Users\<you>\Projects\Spawn

# Build the app
dotnet build src\Spawn\Spawn.csproj -c Release

# Run (tray mode)
dotnet run --project src\Spawn\Spawn.csproj -c Release

# Run with console output of appear/disappear transitions (debug)
dotnet run --project src\Spawn\Spawn.csproj -c Release -- --console
```

## Tests

```powershell
dotnet test
```

Covers the config JSON roundtrip (including migration from the legacy
ProcessLink and GamePadTrigger configs, and corrupt/missing-file handling), the timestamped
event log, and the appear/disappear launch/stop logic with a fake process
source and a mock launcher.

## Publish (single-file self-contained exe)

```powershell
dotnet publish src\Spawn\Spawn.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

The output is `.\publish\Spawn.exe` — a single ~170 MB file with the
.NET runtime bundled; it runs on any Windows x64 machine with no install.

## Usage

1. Launch `Spawn.exe` — the tray icon appears (no window).
2. Left-click the tray icon to open the window.
3. The left list shows all running processes with a live green dot. Click
   **Pin** on any row to keep it at the top of the list permanently (pinned
   processes stay listed, grayed live indicator, even after they exit).
4. Select a process in the list, then edit its events in the right panel:
   - **Event name** and **Enabled** checkbox
   - **Start commands** — one shell command per line, run when the process
     appears (e.g. `"C:\Program Files\AutoHotkey\AutoHotkey.exe" "D:\Scripts\game.ahk"`)
   - **Stop commands** — optional, one per line, run when the process
     disappears (e.g. `taskkill /IM helper.exe`)
   - **Add event** / **Delete event** / **Save**
5. The config is written to `%APPDATA%\Spawn\config.json` and applied
   immediately (no restart).

Tray menu: **Open**, **Reload config** (re-read the JSON from disk),
**Open event log** (opens `events.log` in the default app), **Quit**.
Closing the window hides it to the tray; **Quit** exits for real.

## Configuration format

`%APPDATA%\Spawn\config.json`:

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
  ],
  "pinned": [
    {
      "process_name": "eldenring.exe",
      "display_path": "C:\\Games\\eldenring.exe",
      "last_seen": "2026-10-25T14:03",
      "last_pid": 4242
    }
  ]
}
```

- `process_name` is the process image name as shown in Task Manager
  (e.g. `eldenring.exe`); matching is case-insensitive.
- `games` keeps the start/stop event associations (same format as the Python
  app). `pinned` is new in the C# app: pinned processes with an optional
  `display_path`, `last_seen` (minute precision) and `last_pid`.
- Commands run through `cmd.exe` detached, without opening a console window,
  so they can be any program: `.exe`, `.bat`, `.ahk` (quoted, with the
  AutoHotkey interpreter), etc.
- A process that is already running when Spawn starts counts as
  "appeared" and fires its start commands on the first poll.
- On first run, config is loaded from the first file that exists and parses:
  `%APPDATA%\Spawn\config.json`, then `%APPDATA%\ProcessLink\config.json`, then
  `%APPDATA%\GamePadTrigger\config.json` (the Python app). A legacy file is
  copied to the Spawn location — games and events are preserved — and the
  older file is left in place. A corrupt legacy file is skipped so an older
  valid config can still be migrated.

## Event log format

`%APPDATA%\Spawn\events.log`, one line per action:

```
2026-10-25 14:03:12 | process=eldenring.exe | action=START | command="C:\Program Files\AutoHotkey\AutoHotkey.exe" "D:\Scripts\eldenring.ahk"
2026-10-25 15:10:44 | process=eldenring.exe | action=STOP | command=taskkill /IM helper.exe
```

## Project layout

```
Spawn.sln
src/Spawn/              WPF app (net8.0-windows)
  App.xaml(.cs)               startup, tray icon, single instance, console mode
  MainWindow.xaml(.cs)        the single full-height process list + events panel
  Models/Config.cs            AppConfig / GameConfig / GameEventConfig / PinnedProcessConfig
  Services/
    AppPaths.cs               %APPDATA%\Spawn locations (+ legacy ProcessLink and GamePadTrigger paths)
    ConfigService.cs          JSON load/save + legacy migration
    ProcessMonitor.cs         polling loop + appear/disappear detection
    CommandLauncher.cs        detached command launching via cmd.exe
    EventLog.cs               timestamped event log (write/read/clear)
  ViewModels/                 MainViewModel (list, pinning, sorting) + row view model
tests/Spawn.Tests/      xUnit tests (config roundtrip, monitor logic, event log)
```
