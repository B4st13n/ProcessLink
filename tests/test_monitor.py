"""Tests for the appear/disappear launch/stop logic with a mock launcher."""

import time

import pytest

from gamepadtrigger.config import Config, GameEvent, WatchedGame
from gamepadtrigger.events import EventLog
from gamepadtrigger.monitor import ProcessMonitor


class FakeProcesses:
    """Callable returning the current set of running process names."""

    def __init__(self, names=()):
        self.names = set(names)

    def __call__(self):
        return set(self.names)


class MockLauncher:
    """Records launched commands instead of executing them."""

    def __init__(self):
        self.commands: list[str] = []

    def __call__(self, command: str):
        self.commands.append(command)


def make_config(
    start_commands=("start-game.bat", "C:\\AHK\\aim.ahk"),
    stop_commands=("stop-game.bat",),
    enabled=True,
) -> Config:
    return Config(
        games=[
            WatchedGame(
                process_name="game.exe",
                events=[
                    GameEvent(
                        name="Launch",
                        start_commands=list(start_commands),
                        stop_commands=list(stop_commands),
                        enabled=enabled,
                    )
                ],
            )
        ]
    )


@pytest.fixture
def env(tmp_path):
    procs = FakeProcesses()
    launcher = MockLauncher()
    event_log = EventLog(tmp_path / "events.log")
    monitor = ProcessMonitor(
        make_config(),
        event_log,
        process_names=procs,
        launcher=launcher,
        poll_interval=0.01,
    )
    return monitor, procs, launcher, event_log


def test_appear_launches_start_commands(env):
    monitor, procs, launcher, _ = env
    procs.names = {"game.exe", "other.exe"}
    monitor.poll_once()
    assert launcher.commands == ["start-game.bat", "C:\\AHK\\aim.ahk"]


def test_running_process_does_not_refire(env):
    monitor, procs, launcher, _ = env
    procs.names = {"game.exe"}
    monitor.poll_once()
    monitor.poll_once()
    monitor.poll_once()
    assert launcher.commands == ["start-game.bat", "C:\\AHK\\aim.ahk"]


def test_disappear_runs_stop_commands(env):
    monitor, procs, launcher, _ = env
    procs.names = {"game.exe"}
    monitor.poll_once()
    procs.names = set()
    monitor.poll_once()
    assert launcher.commands == [
        "start-game.bat",
        "C:\\AHK\\aim.ahk",
        "stop-game.bat",
    ]


def test_reappear_fires_start_commands_again(env):
    monitor, procs, launcher, _ = env
    procs.names = {"game.exe"}
    monitor.poll_once()
    procs.names = set()
    monitor.poll_once()
    procs.names = {"game.exe"}
    monitor.poll_once()
    assert launcher.commands.count("start-game.bat") == 2
    assert launcher.commands.count("stop-game.bat") == 1


def test_disabled_event_never_fires(tmp_path):
    procs = FakeProcesses({"game.exe"})
    launcher = MockLauncher()
    config = make_config(enabled=False)
    monitor = ProcessMonitor(
        config, EventLog(tmp_path / "events.log"), process_names=procs, launcher=launcher
    )
    monitor.poll_once()
    procs.names = set()
    monitor.poll_once()
    assert launcher.commands == []


def test_event_without_stop_commands_fires_nothing_on_disappear(tmp_path):
    procs = FakeProcesses({"game.exe"})
    launcher = MockLauncher()
    config = make_config(stop_commands=())
    monitor = ProcessMonitor(
        config, EventLog(tmp_path / "events.log"), process_names=procs, launcher=launcher
    )
    monitor.poll_once()
    procs.names = set()
    monitor.poll_once()
    assert launcher.commands == ["start-game.bat", "C:\\AHK\\aim.ahk"]


def test_multiple_events_all_fire_in_order(tmp_path):
    procs = FakeProcesses({"game.exe"})
    launcher = MockLauncher()
    config = Config(
        games=[
            WatchedGame(
                process_name="game.exe",
                events=[
                    GameEvent(name="e1", start_commands=["a1"], stop_commands=["a2"]),
                    GameEvent(name="e2", start_commands=["b1", "b2"], stop_commands=["b3"]),
                ],
            )
        ]
    )
    monitor = ProcessMonitor(
        config, EventLog(tmp_path / "events.log"), process_names=procs, launcher=launcher
    )
    monitor.poll_once()
    assert launcher.commands == ["a1", "b1", "b2"]
    procs.names = set()
    monitor.poll_once()
    assert launcher.commands == ["a1", "b1", "b2", "a2", "b3"]


def test_unwatched_process_is_ignored(env):
    monitor, procs, launcher, _ = env
    procs.names = {"something-else.exe"}
    monitor.poll_once()
    assert launcher.commands == []


def test_matching_is_case_insensitive(env):
    monitor, procs, launcher, _ = env
    procs.names = {"GAME.EXE"}  # Windows may report image names in any case
    monitor.poll_once()
    assert launcher.commands == ["start-game.bat", "C:\\AHK\\aim.ahk"]
    procs.names = set()
    monitor.poll_once()
    assert "stop-game.bat" in launcher.commands


def test_multiple_watched_games_tracked_independently(tmp_path):
    procs = FakeProcesses({"game.exe"})
    launcher = MockLauncher()
    config = Config(
        games=[
            WatchedGame(
                process_name="game.exe",
                events=[GameEvent(name="e", start_commands=["g-start"], stop_commands=["g-stop"])],
            ),
            WatchedGame(
                process_name="other.exe",
                events=[GameEvent(name="e", start_commands=["o-start"], stop_commands=["o-stop"])],
            ),
        ]
    )
    monitor = ProcessMonitor(
        config, EventLog(tmp_path / "events.log"), process_names=procs, launcher=launcher
    )
    monitor.poll_once()
    assert launcher.commands == ["g-start"]
    procs.names = {"game.exe", "other.exe"}
    monitor.poll_once()
    assert launcher.commands == ["g-start", "o-start"]
    procs.names = {"other.exe"}
    monitor.poll_once()
    assert launcher.commands == ["g-start", "o-start", "g-stop"]


def test_blank_commands_are_skipped(tmp_path):
    procs = FakeProcesses({"game.exe"})
    launcher = MockLauncher()
    config = make_config(start_commands=["  ", "real.cmd", ""], stop_commands=["\t"])
    monitor = ProcessMonitor(
        config, EventLog(tmp_path / "events.log"), process_names=procs, launcher=launcher
    )
    monitor.poll_once()
    assert launcher.commands == ["real.cmd"]


def test_launcher_exception_does_not_break_polling(tmp_path):
    procs = FakeProcesses({"game.exe"})

    def bad_launcher(command):
        raise RuntimeError("boom")

    event_log = EventLog(tmp_path / "events.log")
    monitor = ProcessMonitor(
        make_config(start_commands=["bad.cmd"]),
        event_log,
        process_names=procs,
        launcher=bad_launcher,
    )
    monitor.poll_once()  # must not raise
    procs.names = set()
    monitor.poll_once()  # stop command also raises internally; still no crash
    assert len(event_log.read()) == 2


def test_actions_are_logged_with_time_process_action_path(env):
    monitor, procs, _launcher, event_log = env
    procs.names = {"game.exe"}
    monitor.poll_once()
    procs.names = set()
    monitor.poll_once()
    lines = event_log.read()
    assert len(lines) == 3
    for line in lines:
        assert line[:4].isdigit()  # timestamp starts the line
        assert "process=game.exe" in line
    assert "action=START" in lines[0] and "path=start-game.bat" in lines[0]
    assert "action=START" in lines[1] and "path=C:\\AHK\\aim.ahk" in lines[1]
    assert "action=STOP" in lines[2] and "path=stop-game.bat" in lines[2]


def test_background_thread_start_stop(tmp_path):
    procs = FakeProcesses()
    launcher = MockLauncher()
    monitor = ProcessMonitor(
        make_config(),
        EventLog(tmp_path / "events.log"),
        process_names=procs,
        launcher=launcher,
        poll_interval=0.01,
    )
    monitor.start()
    try:
        procs.names = {"game.exe"}
        deadline = time.time() + 5
        while not launcher.commands and time.time() < deadline:
            time.sleep(0.01)
        assert launcher.commands == ["start-game.bat", "C:\\AHK\\aim.ahk"]
        procs.names = set()
        deadline = time.time() + 5
        while launcher.commands.count("stop-game.bat") < 1 and time.time() < deadline:
            time.sleep(0.01)
        assert "stop-game.bat" in launcher.commands
    finally:
        monitor.stop()
    assert monitor._thread is None


def test_reload_swaps_config(tmp_path):
    procs = FakeProcesses({"game.exe"})
    launcher = MockLauncher()
    monitor = ProcessMonitor(
        make_config(),
        EventLog(tmp_path / "events.log"),
        process_names=procs,
        launcher=launcher,
    )
    new_config = Config(
        poll_interval=0.5,
        games=[
            WatchedGame(
                process_name="game.exe",
                events=[GameEvent(name="e", start_commands=["new-start"])],
            )
        ],
    )
    monitor.reload(new_config)
    assert monitor.config is new_config
    assert monitor.poll_interval == 0.5
    monitor.poll_once()
    assert launcher.commands == ["new-start"]
