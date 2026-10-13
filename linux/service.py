#!/usr/bin/env python3
"""User-session service for the AskBetter GNOME extension and standalone GTK dialog."""
import argparse
import json
import os
from pathlib import Path
import signal
import sys
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
import gi
gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
from gi.repository import Gdk, GLib, Gtk
import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from crossplatform.core import Backend, Settings, MODEL_ID
from crossplatform.geometry import LayoutGate
from linux.accessibility import DesktopAccess, PasteRequired

BUS = "org.askbetter.Desktop"
PATH = "/org/askbetter/Desktop"


def wake_chromium_accessibility(bus, schedule=GLib.timeout_add):
    """Request Chromium's renderer tree, then restore the user's a11y flag."""
    status = dbus.Interface(bus.get_object("org.a11y.Bus", "/org/a11y/bus"),
                            "org.freedesktop.DBus.Properties")
    if status.Get("org.a11y.Status", "ScreenReaderEnabled"):
        return None
    status.Set("org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(True))
    restored = False

    def restore():
        nonlocal restored
        if not restored:
            status.Set("org.a11y.Status", "ScreenReaderEnabled", dbus.Boolean(False))
            restored = True
        return False

    try:
        schedule(1500, restore)
    except Exception:
        restore()
        raise
    return restore


class PromptView(Gtk.TextView):
    def __init__(self, optimize):
        super().__init__()
        self.optimize = optimize
        self.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        self.set_left_margin(10)
        self.set_right_margin(10)
        self.im = Gtk.IMMulticontext()
        self.composing = False
        self.pressed = False
        self.im.connect("preedit-changed", self._preedit)
        self.im.connect("commit", self._commit)
        self.connect("realize", lambda _: self.im.set_client_window(self.get_window(Gtk.TextWindowType.TEXT)))
        self.connect("focus-in-event", lambda *_: self.im.focus_in())
        self.connect("focus-out-event", self._blur)
        self.connect("key-press-event", self._key)
        self.connect("key-release-event", self._release)

    def _commit(self, _, text):
        buffer = self.get_buffer()
        buffer.delete_selection(True, True)
        buffer.insert_at_cursor(text)

    def _preedit(self, *_):
        self.composing = bool(self.im.get_preedit_string()[0])

    def _blur(self, *_):
        self.im.focus_out()
        self.composing = self.pressed = False
        return False

    def _release(self, _, event):
        if event.keyval in (Gdk.KEY_Return, Gdk.KEY_KP_Enter):
            self.pressed = False
        self.im.filter_keypress(event)
        return False

    def _key(self, _, event):
        composing = self.composing
        enter = event.keyval in (Gdk.KEY_Return, Gdk.KEY_KP_Enter)
        was_pressed = self.pressed
        if enter:
            self.pressed = True
        if self.im.filter_keypress(event):
            return True
        modifiers = event.state & (Gdk.ModifierType.SHIFT_MASK | Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.MOD1_MASK | Gdk.ModifierType.SUPER_MASK)
        if enter and not modifiers and not composing:
            if not was_pressed:
                self.optimize()
            return True
        return False


class Service(dbus.service.Object):
    def __init__(self, bus, config=None, backend=None, access=None):
        self.name = dbus.service.BusName(BUS, bus=bus, do_not_queue=True)
        super().__init__(bus, PATH)
        self.session_bus = bus
        self.accessibility_attempted = set()
        self.accessibility_restore = None
        config = config or Path(os.environ.get("XDG_CONFIG_HOME", Path.home()/".config"))/"askbetter/settings.json"
        self.settings = Settings(config)
        prompts = ROOT/"prompts" if (ROOT/"prompts").exists() else ROOT/"askbetter"
        self.backend = backend or Backend(prompts, Path(config).parent/"empty-workspace")
        self.access = access or DesktopAccess()
        self.gate = LayoutGate()
        self.busy = False
        self.context = {}
        self.current = None
        self.pending = None
        self.paste_pending = None
        self.paste_is_undo = False
        self.undo = None
        self.panel = None
        self.last_input = self.last_result = ""
        self.notice = ""
        if self.settings.value["uiMode"] == "standalone":
            GLib.idle_add(self._show_panel)

    def _configuration(self):
        return self.settings.value["model"] + " · " + {"brief":"简短", "moderate":"适中", "detailed":"详细"}[self.settings.value["lengthStyle"]] + " · Low · Fast"

    def _context(self, text):
        value = json.loads(text)
        if not isinstance(value, dict):
            raise ValueError("Invalid window context")
        return value

    def _status(self, message):
        self.notice = message
        if self.panel:
            self.panel_status.set_text(message or self._configuration())

    @dbus.service.method(BUS, in_signature="s", out_signature="s")
    def Poll(self, context):
        self.context = self._context(context)
        if any(item and time.monotonic()-item[4] > 8 for item in (self.pending, self.paste_pending)):
            self.pending = self.paste_pending = None
            self.busy = False
            self._status("回填请求超时；结果可在单独对话框查看")
        state = {"visible": False, "busy": self.busy, "settings": self.settings.value,
                 "notice": self.notice, "caption": self._configuration()}
        if self.settings.value["uiMode"] == "standalone" or not self.context.get("pid"):
            self.current = None
            return json.dumps(state, ensure_ascii=False)
        try:
            target = self.access.discover(self.context)
            position = self.access.position(target, self.settings.value["buttonSize"], self.settings.value["anchorGap"])
            signature = (target.identity(), position, tuple(sorted(self.context["buffer"].items())))
            settled = self.gate.update(signature, time.monotonic(), self.context.get("moving", False))
            self.current = target
            state.update({"visible": settled, "x": position.x, "y": position.y,
                          "size": position.width, "target": self.context["windowId"]})
        except Exception as error:
            self.current = None
            state["notice"] = str(error)
            pid = self.context.get("pid")
            if (str(error) in ("Codex input box/model selector not exposed through AT-SPI",
                              "The foreground Codex application is not exposed through AT-SPI")
                    and pid not in self.accessibility_attempted):
                self.accessibility_attempted.add(pid)
                try:
                    restore = wake_chromium_accessibility(self.session_bus)
                    if restore is not None:
                        self.accessibility_restore = restore
                except Exception:
                    pass
        return json.dumps(state, ensure_ascii=False)

    @dbus.service.method(BUS, in_signature="", out_signature="s")
    def Diagnostics(self):
        return json.dumps({"context": self.context, "notice": self.notice,
                           "hasTarget": self.current is not None}, ensure_ascii=False)

    @dbus.service.method(BUS, in_signature="s", out_signature="b")
    def Optimize(self, context):
        if self.busy or self.settings.value["uiMode"] != "initial":
            return False
        context = self._context(context)
        try:
            target = self.access.discover(context, fresh=True)
            draft = self.access.read(target)
            self._optimize(draft, target)
            return True
        except Exception as error:
            self._status(str(error))
            return False

    def _optimize(self, draft, target=None):
        if self.busy:
            return
        self.busy = True
        self._status("优化中 · " + self._configuration())
        options = dict(self.settings.value)
        def work():
            try:
                result = self.backend.optimize(draft, options)
                GLib.idle_add(self._complete, draft, result, target, "")
            except Exception as error:
                GLib.idle_add(self._complete, draft, "", target, str(error))
        threading.Thread(target=work, daemon=True).start()

    def _complete(self, draft, result, target, error):
        self.busy = bool(target and result and not error)
        self.last_input, self.last_result = draft, result
        if self.panel and result:
            self.panel_output.get_buffer().set_text(result)
        if error:
            self._status(error)
        elif target:
            ticket = uuid.uuid4().hex
            self.pending = (ticket, target, draft, result, time.monotonic())
            self._status("优化完成，正在回填")
            self.ResultReady(ticket)
        else:
            self._status("优化完成 · " + self._configuration())
        return False

    @dbus.service.signal(BUS, signature="s")
    def ResultReady(self, ticket):
        pass

    @dbus.service.method(BUS, in_signature="ss", out_signature="b")
    def Commit(self, ticket, context):
        pending, self.pending = self.pending, None
        if not pending or pending[0] != ticket or time.monotonic()-pending[4] > 5:
            if pending:
                self.busy = False
                self._status("回填请求已过期；原稿未覆盖")
            return False
        _, target, draft, result, _ = pending
        try:
            self.access.replace(target, draft, result, self._context(context))
            self.undo = (target, result, draft)
            self._status("优化完成；Ctrl+Alt+Z 撤销")
            self.busy = False
            return True
        except PasteRequired:
            self.paste_pending = pending
            self.paste_is_undo = False
            self.PasteReady(ticket)
            return True
        except Exception as error:
            self.busy = False
            self._status(str(error) + "；结果可在单独对话框查看")
            return False

    @dbus.service.signal(BUS, signature="s")
    def PasteReady(self, ticket):
        pass

    @dbus.service.method(BUS, in_signature="ss", out_signature="s")
    def PreparePaste(self, ticket, context):
        pending = self.paste_pending
        if not pending or pending[0] != ticket or time.monotonic()-pending[4] > 5:
            if pending:
                self.paste_pending = None
                self.busy = False
                self._status("回填请求已过期；原稿未覆盖")
            return ""
        try:
            self.access.prepare_paste(pending[1], pending[2], self._context(context))
            return pending[3]
        except Exception as error:
            self.paste_pending = None
            self.busy = False
            self._status(str(error))
            return ""

    @dbus.service.method(BUS, in_signature="ss", out_signature="b")
    def FinishPaste(self, ticket, context):
        pending, self.paste_pending = self.paste_pending, None
        self.busy = False
        if not pending or pending[0] != ticket:
            return False
        _, target, draft, result, _ = pending
        if self.access.unchanged(target, result, self._context(context)):
            self.undo = None if self.paste_is_undo else (target, result, draft)
            self._status("已恢复原稿" if self.paste_is_undo else "优化完成；Ctrl+Alt+Z 撤销")
            self.paste_is_undo = False
            return True
        self._status("未能验证回填；结果可在单独对话框查看")
        return False

    @dbus.service.method(BUS, in_signature="s", out_signature="b")
    def Undo(self, context):
        if self.busy or not self.undo:
            return False
        try:
            target, result, draft = self.undo
            self.access.replace(target, result, draft, self._context(context))
            self.undo = None
            self._status("已恢复原稿")
            return True
        except PasteRequired:
            self.busy = True
            ticket = uuid.uuid4().hex
            self.paste_pending = (ticket, target, result, draft, time.monotonic())
            self.paste_is_undo = True
            self.PasteReady(ticket)
            return True
        except Exception as error:
            self._status(str(error))
            return False

    @dbus.service.method(BUS, in_signature="s", out_signature="")
    def SetStyle(self, style):
        if not self.busy:
            self.settings.update(lengthStyle=str(style))
            self._status(self._configuration())

    @dbus.service.method(BUS, in_signature="", out_signature="")
    def ShowStandalone(self):
        if not self.busy:
            self.settings.update(uiMode="standalone")
            self._show_panel()

    @dbus.service.method(BUS, in_signature="", out_signature="")
    def ShowFirstRun(self):
        if not self.busy:
            self._show_panel()
            self._status("安装完成；重新登录桌面后圆形按钮生效，当前可在此优化草稿")

    def _show_panel(self):
        if self.panel is None:
            self.panel = Gtk.Window(title="AskBetter · 单独对话框")
            self.panel.set_default_size(600, 480)
            self.panel.set_size_request(430, 340)
            self.panel.connect("delete-event", self._close_panel)
            grid = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10, margin=18)
            self.panel.add(grid)
            self.panel_status = Gtk.Label(label=self._configuration(), xalign=0)
            self.panel_status.set_line_wrap(True)
            grid.pack_start(self.panel_status, False, False, 0)
            self.panel_input = PromptView(self._run_panel)
            self.panel_output = Gtk.TextView(editable=False, wrap_mode=Gtk.WrapMode.WORD_CHAR)
            self.panel_output.set_left_margin(10)
            self.panel_output.set_right_margin(10)
            for index, view in enumerate((self.panel_input, self.panel_output)):
                scroll = Gtk.ScrolledWindow()
                scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
                scroll.add(view)
                grid.pack_start(scroll, True, True, 0)
                if index == 0:
                    row = Gtk.Box(spacing=10)
                    optimize = Gtk.Button(label="优化")
                    optimize.connect("clicked", lambda *_: self._run_panel())
                    options = Gtk.Button(label="设置")
                    options.connect("clicked", lambda *_: self._menu().popup_at_widget(options, Gdk.Gravity.SOUTH_WEST, Gdk.Gravity.NORTH_WEST, None))
                    row.pack_start(optimize, False, False, 0)
                    row.pack_start(options, False, False, 0)
                    grid.pack_start(row, False, False, 0)
                view.connect("button-press-event", self._panel_right_click)
            self.panel_input.get_buffer().set_text(self.last_input)
            self.panel_output.get_buffer().set_text(self.last_result)
        self.panel.show_all()
        self.panel.present()
        return False

    def _menu(self):
        menu = Gtk.Menu()
        for style, title in (("brief", "简短"), ("moderate", "适中"), ("detailed", "详细")):
            item = Gtk.CheckMenuItem(label=title)
            item.set_active(self.settings.value["lengthStyle"] == style)
            item.connect("activate", lambda _, value=style: self.SetStyle(value))
            item.set_sensitive(not self.busy)
            menu.append(item)
        for title, action in (("初始模式", self.ReturnInitial), ("更改模型", self.ShowModel)):
            item = Gtk.MenuItem(label=title)
            item.connect("activate", lambda _, callback=action: callback())
            item.set_sensitive(not self.busy)
            menu.append(item)
        menu.show_all()
        return menu

    def _panel_right_click(self, _, event):
        if event.button == 3:
            self._menu().popup_at_pointer(event)
            return True
        return False

    def _run_panel(self):
        if self.busy:
            return
        buffer = self.panel_input.get_buffer()
        self._optimize(buffer.get_text(buffer.get_start_iter(), buffer.get_end_iter(), True))

    def _close_panel(self, *_):
        self.ReturnInitial()
        return True

    @dbus.service.method(BUS, in_signature="", out_signature="")
    def ReturnInitial(self):
        if not self.busy:
            self.settings.update(uiMode="initial")
            if self.panel:
                self.panel.hide()
            self.gate = LayoutGate()

    @dbus.service.method(BUS, in_signature="", out_signature="")
    def ShowModel(self):
        if self.busy:
            return
        dialog = Gtk.Dialog(title="更改优化模型", transient_for=self.panel, modal=True)
        dialog.add_buttons("取消", Gtk.ResponseType.CANCEL, "保存", Gtk.ResponseType.OK)
        dialog.set_default_size(460, 180)
        box = dialog.get_content_area()
        box.set_border_width(18)
        box.set_spacing(10)
        box.pack_start(Gtk.Label(label="模型（保持 Low / Fast）", xalign=0), False, False, 0)
        combo = Gtk.ComboBoxText.new_with_entry()
        entry = combo.get_child()
        entry.set_text(self.settings.value["model"])
        box.pack_start(combo, False, False, 0)
        status = Gtk.Label(label="正在获取模型列表，也可以手动输入", xalign=0)
        box.pack_start(status, False, False, 0)
        alive = [True]
        def loaded(models, error):
            if alive[0]:
                current = entry.get_text()
                for value in models:
                    combo.append_text(value)
                entry.set_text(current)
                status.set_text(error or "选择或输入模型标识")
            return False
        def load():
            try:
                GLib.idle_add(loaded, self.backend.models(dict(self.settings.value)), "")
            except Exception:
                GLib.idle_add(loaded, [], "模型列表暂不可用，可手动输入")
        threading.Thread(target=load, daemon=True).start()
        dialog.show_all()
        while dialog.run() == Gtk.ResponseType.OK:
            try:
                self.settings.update(model=entry.get_text().strip())
                self._status(self._configuration())
                break
            except ValueError as error:
                status.set_text(str(error))
        alive[0] = False
        dialog.destroy()

    @dbus.service.method(BUS, in_signature="", out_signature="")
    def Quit(self):
        if self.accessibility_restore:
            try:
                self.accessibility_restore()
            except dbus.DBusException:
                pass  # The accessibility bus may already be stopping at logout.
        self.backend.close()
        Gtk.main_quit()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--standalone", action="store_true")
    parser.add_argument("--first-run", action="store_true")
    args = parser.parse_args()
    DBusGMainLoop(set_as_default=True)
    bus = dbus.SessionBus()
    if bus.name_has_owner(BUS):
        if args.standalone or args.first_run:
            remote = dbus.Interface(bus.get_object(BUS, PATH), BUS)
            remote.ShowFirstRun() if args.first_run else remote.ShowStandalone()
        return
    Gtk.Settings.get_default().set_property("gtk-application-prefer-dark-theme", True)
    service = Service(bus)
    if args.standalone:
        service.ShowStandalone()
    elif args.first_run:
        service.ShowFirstRun()
    signal.signal(signal.SIGTERM, lambda *_: GLib.idle_add(service.Quit))
    signal.signal(signal.SIGINT, lambda *_: GLib.idle_add(service.Quit))
    try:
        Gtk.main()
    finally:
        service.backend.close()


if __name__ == "__main__":
    main()
