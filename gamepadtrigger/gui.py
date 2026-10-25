"""Tkinter configuration window.

Left pane: live list of running processes (psutil) with a Watch/Unwatch
toggle, plus the watched games list. Right pane: events for the selected
game (add/edit/remove/toggle). Bottom bar: save, and event-log
view/clear/export.
"""

from __future__ import annotations

import copy
import tkinter as tk
from tkinter import filedialog, messagebox, scrolledtext, ttk
from typing import TYPE_CHECKING, Callable

import psutil

from .config import Config, GameEvent, WatchedGame, save_config

if TYPE_CHECKING:
    from .tray import TrayApp

PROCESS_REFRESH_MS = 2000


def list_running_processes() -> list[str]:
    """Sorted unique names of currently running process images."""
    names: set[str] = set()
    for proc in psutil.process_iter(["name"]):
        try:
            name = proc.info["name"]
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
        if name:
            names.add(name)
    return sorted(names, key=str.lower)


class EventEditor(tk.Toplevel):
    """Modal dialog editing one event (name, enabled, start/stop commands)."""

    def __init__(
        self,
        parent: tk.Misc,
        event: GameEvent,
        on_save: Callable[[GameEvent], None],
    ):
        super().__init__(parent)
        self.title("Edit Event")
        self.resizable(True, True)
        self.on_save = on_save

        tk.Label(self, text="Event name:").grid(
            row=0, column=0, sticky="w", padx=8, pady=4
        )
        self.name_var = tk.StringVar(value=event.name)
        tk.Entry(self, textvariable=self.name_var, width=44).grid(
            row=0, column=1, padx=8, pady=4
        )

        self.enabled_var = tk.BooleanVar(value=event.enabled)
        tk.Checkbutton(self, text="Enabled", variable=self.enabled_var).grid(
            row=1, column=0, columnspan=2, sticky="w", padx=8
        )

        tk.Label(self, text="Start commands (one per line):").grid(
            row=2, column=0, sticky="nw", padx=8
        )
        self.start_text = tk.Text(self, width=52, height=5)
        self.start_text.insert("1.0", "\n".join(event.start_commands))
        self.start_text.grid(row=2, column=1, padx=8, pady=4)

        tk.Label(self, text="Stop commands (optional, one per line):").grid(
            row=3, column=0, sticky="nw", padx=8
        )
        self.stop_text = tk.Text(self, width=52, height=5)
        self.stop_text.insert("1.0", "\n".join(event.stop_commands))
        self.stop_text.grid(row=3, column=1, padx=8, pady=4)

        buttons = ttk.Frame(self)
        buttons.grid(row=4, column=0, columnspan=2, pady=8)
        ttk.Button(buttons, text="OK", command=self._ok).pack(side="left", padx=4)
        ttk.Button(buttons, text="Cancel", command=self.destroy).pack(
            side="left", padx=4
        )

        self.transient(parent.winfo_toplevel())
        self.grab_set()
        self.wait_window()

    def _ok(self) -> None:
        self.on_save(
            GameEvent(
                name=self.name_var.get().strip() or "New event",
                start_commands=self._lines(self.start_text),
                stop_commands=self._lines(self.stop_text),
                enabled=bool(self.enabled_var.get()),
            )
        )
        self.destroy()

    @staticmethod
    def _lines(text: tk.Text) -> list[str]:
        return [
            line.strip()
            for line in text.get("1.0", "end").splitlines()
            if line.strip()
        ]


class ConfigWindow(tk.Toplevel):
    """Main configuration window, opened by clicking the tray icon."""

    def __init__(self, root: tk.Tk, app: "TrayApp"):
        super().__init__(root)
        self.app = app
        self.title("GamePadTrigger - Configuration")
        self.geometry("940x560")
        self.minsize(760, 420)
        self.protocol("WM_DELETE_WINDOW", self._close)

        # Work on a copy; only Save persists and activates it.
        self.config: Config = copy.deepcopy(app.config)
        self._refresh_job: str | None = None

        self._build_widgets()
        self._refresh_processes()
        self._refresh_games()
        self._schedule_process_refresh()

    # -- UI construction --------------------------------------------------

    def _build_widgets(self) -> None:
        paned = ttk.PanedWindow(self, orient="horizontal")
        paned.pack(fill="both", expand=True, padx=8, pady=8)

        left = ttk.Frame(paned)
        paned.add(left, weight=1)

        ttk.Label(left, text="Running processes").pack(anchor="w")
        proc_frame = ttk.Frame(left)
        proc_frame.pack(fill="both", expand=True)
        self.process_listbox = tk.Listbox(proc_frame, exportselection=False)
        proc_scroll = ttk.Scrollbar(
            proc_frame, orient="vertical", command=self.process_listbox.yview
        )
        self.process_listbox.configure(yscrollcommand=proc_scroll.set)
        self.process_listbox.pack(side="left", fill="both", expand=True)
        proc_scroll.pack(side="right", fill="y")
        self.process_listbox.bind("<<ListboxSelect>>", lambda _e: self._update_watch_button())

        proc_buttons = ttk.Frame(left)
        proc_buttons.pack(fill="x", pady=4)
        ttk.Button(proc_buttons, text="Refresh", command=self._refresh_processes).pack(
            side="left"
        )
        self.watch_button = ttk.Button(
            proc_buttons, text="Watch", command=self._toggle_watch
        )
        self.watch_button.pack(side="left", padx=6)

        ttk.Label(left, text="Watched games").pack(anchor="w", pady=(10, 0))
        game_frame = ttk.Frame(left)
        game_frame.pack(fill="both", expand=True)
        self.game_listbox = tk.Listbox(game_frame, exportselection=False)
        game_scroll = ttk.Scrollbar(
            game_frame, orient="vertical", command=self.game_listbox.yview
        )
        self.game_listbox.configure(yscrollcommand=game_scroll.set)
        self.game_listbox.pack(side="left", fill="both", expand=True)
        game_scroll.pack(side="right", fill="y")
        self.game_listbox.bind("<<ListboxSelect>>", lambda _e: self._refresh_events())

        game_buttons = ttk.Frame(left)
        game_buttons.pack(fill="x", pady=4)
        ttk.Button(game_buttons, text="Remove game", command=self._remove_game).pack(
            side="left"
        )

        right = ttk.Frame(paned)
        paned.add(right, weight=1)

        self.events_label = ttk.Label(right, text="Events (select a game)")
        self.events_label.pack(anchor="w")
        event_frame = ttk.Frame(right)
        event_frame.pack(fill="both", expand=True)
        self.event_listbox = tk.Listbox(event_frame, exportselection=False)
        event_scroll = ttk.Scrollbar(
            event_frame, orient="vertical", command=self.event_listbox.yview
        )
        self.event_listbox.configure(yscrollcommand=event_scroll.set)
        self.event_listbox.pack(side="left", fill="both", expand=True)
        event_scroll.pack(side="right", fill="y")
        self.event_listbox.bind("<<ListboxSelect>>", lambda _e: None)

        event_buttons = ttk.Frame(right)
        event_buttons.pack(fill="x", pady=4)
        ttk.Button(event_buttons, text="Add event", command=self._add_event).pack(
            side="left"
        )
        ttk.Button(event_buttons, text="Edit event", command=self._edit_event).pack(
            side="left", padx=4
        )
        ttk.Button(
            event_buttons, text="Toggle enabled", command=self._toggle_event
        ).pack(side="left", padx=4)
        ttk.Button(
            event_buttons, text="Remove event", command=self._remove_event
        ).pack(side="left", padx=4)

        bottom = ttk.Frame(self)
        bottom.pack(fill="x", padx=8, pady=(0, 8))
        ttk.Button(bottom, text="Save", command=self._save).pack(side="left")
        ttk.Button(bottom, text="Close", command=self._close).pack(
            side="left", padx=4
        )
        ttk.Button(bottom, text="View log", command=self._view_log).pack(
            side="left", padx=12
        )
        ttk.Button(bottom, text="Clear log", command=self._clear_log).pack(
            side="left", padx=4
        )
        ttk.Button(bottom, text="Export log", command=self._export_log).pack(
            side="left", padx=4
        )
        self.status_var = tk.StringVar(value="")
        ttk.Label(bottom, textvariable=self.status_var).pack(side="right")

    # -- process list -----------------------------------------------------

    def _refresh_processes(self) -> None:
        selected = self._selected_process()
        self.process_listbox.delete(0, "end")
        for name in list_running_processes():
            self.process_listbox.insert("end", name)
        if selected:
            names = self.process_listbox.get(0, "end")
            if selected in names:
                self.process_listbox.selection_set(names.index(selected))
        self._update_watch_button()

    def _schedule_process_refresh(self) -> None:
        def tick() -> None:
            try:
                self._refresh_processes()
            except tk.TclError:
                return
            self._refresh_job = self.after(PROCESS_REFRESH_MS, tick)

        self._refresh_job = self.after(PROCESS_REFRESH_MS, tick)

    def _selected_process(self) -> str | None:
        selection = self.process_listbox.curselection()
        if not selection:
            return None
        return self.process_listbox.get(selection[0])

    def _update_watch_button(self) -> None:
        name = self._selected_process()
        watched = bool(
            name and any(g.process_name == name for g in self.config.games)
        )
        self.watch_button.configure(text="Unwatch" if watched else "Watch")

    def _toggle_watch(self) -> None:
        name = self._selected_process()
        if not name:
            self.status_var.set("Select a process first")
            return
        existing = next(
            (g for g in self.config.games if g.process_name == name), None
        )
        if existing is not None:
            self.config.games.remove(existing)
            self.status_var.set(f"Stopped watching {name}")
        else:
            self.config.games.append(
                WatchedGame(
                    process_name=name,
                    events=[GameEvent(name="New event")],
                )
            )
            self.status_var.set(f"Watching {name}")
        self._refresh_games()
        self._update_watch_button()

    # -- watched games ----------------------------------------------------

    def _refresh_games(self) -> None:
        selected = self._selected_game_name()
        self.game_listbox.delete(0, "end")
        for game in self.config.games:
            self.game_listbox.insert("end", game.process_name)
        if selected:
            names = self.game_listbox.get(0, "end")
            if selected in names:
                self.game_listbox.selection_set(names.index(selected))
        self._refresh_events()

    def _selected_game_name(self) -> str | None:
        selection = self.game_listbox.curselection()
        if not selection:
            return None
        return self.game_listbox.get(selection[0])

    def _selected_game(self) -> WatchedGame | None:
        name = self._selected_game_name()
        if name is None:
            return None
        return next((g for g in self.config.games if g.process_name == name), None)

    def _remove_game(self) -> None:
        game = self._selected_game()
        if game is None:
            self.status_var.set("Select a game first")
            return
        self.config.games.remove(game)
        self.status_var.set(f"Removed {game.process_name}")
        self._refresh_games()

    # -- events -----------------------------------------------------------

    def _refresh_events(self) -> None:
        game = self._selected_game()
        self.event_listbox.delete(0, "end")
        if game is None:
            self.events_label.configure(text="Events (select a game)")
            return
        self.events_label.configure(text=f"Events for {game.process_name}")
        for event in game.events:
            state = "on" if event.enabled else "off"
            self.event_listbox.insert("end", f"{event.name} [{state}]")

    def _selected_event(self) -> tuple[WatchedGame, GameEvent] | None:
        game = self._selected_game()
        selection = self.event_listbox.curselection()
        if game is None or not selection:
            return None
        index = selection[0]
        if index >= len(game.events):
            return None
        return game, game.events[index]

    def _add_event(self) -> None:
        game = self._selected_game()
        if game is None:
            self.status_var.set("Select a game first")
            return
        EventEditor(self, GameEvent(name="New event"), game.events.append)
        self._refresh_events()

    def _edit_event(self) -> None:
        selected = self._selected_event()
        if selected is None:
            self.status_var.set("Select an event first")
            return
        game, event = selected

        def replace(updated: GameEvent) -> None:
            game.events[game.events.index(event)] = updated

        EventEditor(self, copy.deepcopy(event), replace)
        self._refresh_events()

    def _toggle_event(self) -> None:
        selected = self._selected_event()
        if selected is None:
            self.status_var.set("Select an event first")
            return
        _game, event = selected
        event.enabled = not event.enabled
        self._refresh_events()

    def _remove_event(self) -> None:
        selected = self._selected_event()
        if selected is None:
            self.status_var.set("Select an event first")
            return
        game, event = selected
        game.events.remove(event)
        self._refresh_events()

    # -- save / close / log -------------------------------------------------

    def _save(self) -> None:
        save_config(self.config, self.app.config_file)
        self.app.apply_config(self.config)
        self.status_var.set(f"Saved to {self.app.config_file}")

    def _close(self) -> None:
        if self._refresh_job is not None:
            try:
                self.after_cancel(self._refresh_job)
            except tk.TclError:
                pass
            self._refresh_job = None
        self.destroy()

    def _view_log(self) -> None:
        win = tk.Toplevel(self)
        win.title("GamePadTrigger - Event Log")
        win.geometry("720x420")
        text = scrolledtext.ScrolledText(win, wrap="none", state="normal")
        text.pack(fill="both", expand=True, padx=8, pady=8)

        def refresh() -> None:
            text.configure(state="normal")
            text.delete("1.0", "end")
            text.insert("1.0", "\n".join(self.app.event_log.read()))

        buttons = ttk.Frame(win)
        buttons.pack(fill="x", padx=8, pady=(0, 8))
        ttk.Button(buttons, text="Refresh", command=refresh).pack(side="left")
        ttk.Button(buttons, text="Close", command=win.destroy).pack(
            side="left", padx=4
        )
        ttk.Label(
            buttons, text=str(self.app.log_file)
        ).pack(side="right")
        refresh()

    def _clear_log(self) -> None:
        if messagebox.askyesno("Clear log", "Delete all events from the log file?"):
            self.app.event_log.clear()
            self.status_var.set("Log cleared")

    def _export_log(self) -> None:
        destination = filedialog.asksaveasfilename(
            parent=self,
            title="Export event log",
            defaultextension=".log",
            initialfile="events.log",
            filetypes=[("Log files", "*.log"), ("All files", "*.*")],
        )
        if not destination:
            return
        self.app.event_log.export(destination)
        self.status_var.set(f"Log exported to {destination}")
