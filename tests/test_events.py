"""Tests for the timestamped event log."""

from gamepadtrigger.events import EventLog


def test_log_read_clear_export(tmp_path):
    log = EventLog(tmp_path / "events.log")
    log.log("game.exe", "START", "run.bat")
    log.log("game.exe", "STOP", "stop.bat")

    lines = log.read()
    assert len(lines) == 2
    assert "process=game.exe" in lines[0]
    assert "action=START" in lines[0]
    assert "path=run.bat" in lines[0]
    assert lines[0][:4].isdigit()
    assert "action=STOP" in lines[1]

    dest = tmp_path / "export.log"
    log.export(dest)
    assert dest.read_text(encoding="utf-8") == "\n".join(lines) + "\n"

    log.clear()
    assert log.read() == []


def test_read_missing_file_returns_empty(tmp_path):
    log = EventLog(tmp_path / "events.log")
    assert log.read() == []


def test_export_missing_file_creates_empty_destination(tmp_path):
    log = EventLog(tmp_path / "events.log")
    dest = tmp_path / "export.log"
    log.export(dest)
    assert dest.exists()
    assert dest.read_text(encoding="utf-8") == ""
