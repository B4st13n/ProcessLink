"""Tests for config JSON persistence (roundtrip + tolerant loading)."""

import json

from gamepadtrigger.config import (
    Config,
    GameEvent,
    WatchedGame,
    load_config,
    save_config,
)


def make_config() -> Config:
    return Config(
        poll_interval=1.5,
        games=[
            WatchedGame(
                process_name="eldenring.exe",
                events=[
                    GameEvent(
                        name="Launch helper",
                        start_commands=["C:\\Tools\\helper.ahk", "D:\\game\\start.exe"],
                        stop_commands=["taskkill /IM helper.exe"],
                        enabled=True,
                    ),
                    GameEvent(
                        name="Idle mode",
                        start_commands=["ping localhost"],
                        stop_commands=[],
                        enabled=False,
                    ),
                ],
            ),
            WatchedGame(process_name="notepad.exe", events=[]),
        ],
    )


def test_roundtrip(tmp_path):
    path = tmp_path / "config.json"
    config = make_config()
    save_config(config, path)
    loaded = load_config(path)
    assert loaded == config


def test_load_missing_file_returns_default(tmp_path):
    assert load_config(tmp_path / "does-not-exist.json") == Config()


def test_load_corrupt_file_returns_default(tmp_path):
    path = tmp_path / "config.json"
    path.write_text("{ not valid json", encoding="utf-8")
    assert load_config(path) == Config()


def test_load_non_dict_json_returns_default(tmp_path):
    path = tmp_path / "config.json"
    path.write_text('["not", "a", "dict"]', encoding="utf-8")
    assert load_config(path) == Config()


def test_saved_file_is_valid_json_with_expected_shape(tmp_path):
    path = tmp_path / "config.json"
    save_config(make_config(), path)
    raw = json.loads(path.read_text(encoding="utf-8"))
    assert raw["poll_interval"] == 1.5
    assert raw["games"][0]["process_name"] == "eldenring.exe"
    assert raw["games"][0]["events"][0]["start_commands"] == [
        "C:\\Tools\\helper.ahk",
        "D:\\game\\start.exe",
    ]
    assert raw["games"][0]["events"][0]["stop_commands"] == [
        "taskkill /IM helper.exe"
    ]
    assert raw["games"][0]["events"][0]["enabled"] is True
    assert raw["games"][0]["events"][1]["enabled"] is False
    assert raw["games"][1]["events"] == []


def test_roundtrip_preserves_defaults_for_empty_event(tmp_path):
    path = tmp_path / "config.json"
    config = Config(games=[WatchedGame(process_name="game.exe", events=[GameEvent()])])
    save_config(config, path)
    loaded = load_config(path)
    assert loaded == config
    assert loaded.games[0].events[0].enabled is True
    assert loaded.games[0].events[0].start_commands == []


def test_watched_names(tmp_path):
    config = make_config()
    assert config.watched_names() == {"eldenring.exe", "notepad.exe"}
