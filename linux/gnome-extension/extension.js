import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import St from 'gi://St';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';

const BUS = 'org.askbetter.Desktop';
const PATH = '/org/askbetter/Desktop';

export default class AskBetter extends Extension {
    enable() {
        this._enabled = true;
        this._quit = false;
        this._inflight = false;
        this._moving = false;
        this._hiddenUntil = 0;
        this._settings = this.getSettings();
        this._button = new St.Button({can_focus: false, reactive: true, track_hover: true,
            accessible_name: '优化提示词', button_mask: St.ButtonMask.ONE,
            style: 'background-color:#303030;border:1px solid #484848;border-radius:13px;padding:0;min-width:0;min-height:0;'});
        this._icon = new St.Icon({gicon: new Gio.FileIcon({file: Gio.File.new_for_path(`${this.path}/wand.svg`)}), icon_size: 16});
        this._button.set_child(this._icon);
        this._button.set_size(26, 26);
        Main.layoutManager.addChrome(this._button, {affectsInputRegion: true, trackFullscreen: true});
        this._button.hide();
        this._button.connect('clicked', () => this._call('Optimize', '(s)', [JSON.stringify(this._context())]));
        this._button.connect('button-press-event', (_, event) => {
            if (event.get_button() === 3) {
                this._menu.toggle();
                return Clutter.EVENT_STOP;
            }
            return Clutter.EVENT_PROPAGATE;
        });
        this._button.connect('notify::hover', () => {
            this._button.set_style(this._button.hover
                ? 'background-color:#414141;border:1px solid #484848;border-radius:13px;padding:0;min-width:0;min-height:0;'
                : 'background-color:#303030;border:1px solid #484848;border-radius:13px;padding:0;min-width:0;min-height:0;');
            if (this._button.hover && this._caption) {
                this._tooltip.text = this._caption;
                this._tooltip.set_position(Math.max(0, this._button.x-80), this._button.y-40);
                this._tooltip.show();
            } else {
                this._tooltip.hide();
            }
        });
        this._tooltip = new St.Label({style: 'background-color:#202020;color:#eeeeee;border-radius:6px;padding:6px;font-size:12px;', reactive: false});
        Main.layoutManager.addChrome(this._tooltip, {affectsInputRegion: false});
        this._tooltip.hide();
        this._menu = new PopupMenu.PopupMenu(this._button, 0.5, St.Side.TOP);
        Main.uiGroup.add_child(this._menu.actor);
        this._menu.actor.hide();
        this._menuManager = new PopupMenu.PopupMenuManager(this._button);
        this._menuManager.addMenu(this._menu);
        this._styleItems = {};
        for (const [style, label] of [['brief', '简短'], ['moderate', '适中'], ['detailed', '详细']]) {
            const item = new PopupMenu.PopupMenuItem(label);
            item.connect('activate', () => this._call('SetStyle', '(s)', [style]));
            this._menu.addMenuItem(item);
            this._styleItems[style] = item;
        }
        for (const [label, method] of [['单独对话框模式', 'ShowStandalone'], ['更改模型', 'ShowModel']]) {
            const item = new PopupMenu.PopupMenuItem(label);
            item.connect('activate', () => this._call(method, '()', []));
            this._menu.addMenuItem(item);
        }
        this._menu.connect('open-state-changed', (_, open) => {
            if (open)
                this._tooltip.hide();
        });
        this._focusSignal = global.display.connect('notify::focus-window', () => this._watchWindow());
        this._grabBegin = global.display.connect('grab-op-begin', () => {
            this._moving = true;
            this._hideDuringLayout();
        });
        this._grabEnd = global.display.connect('grab-op-end', () => {
            this._moving = false;
            this._hideDuringLayout();
        });
        this._stageSignal = global.stage.connect('captured-event', (_, event) => {
            if (!this._context().pid)
                return Clutter.EVENT_PROPAGATE;
            const control = Boolean(event.get_state() & Clutter.ModifierType.CONTROL_MASK);
            const alt = Boolean(event.get_state() & Clutter.ModifierType.MOD1_MASK);
            if (control && !alt && (event.type() === Clutter.EventType.SCROLL ||
                (event.type() === Clutter.EventType.KEY_PRESS && [Clutter.KEY_plus, Clutter.KEY_equal,
                    Clutter.KEY_minus, Clutter.KEY_0, Clutter.KEY_KP_Add, Clutter.KEY_KP_Subtract].includes(event.get_key_symbol()))))
                this._hideDuringLayout();
            return Clutter.EVENT_PROPAGATE;
        });
        Main.wm.addKeybinding('undo', this._settings, Meta.KeyBindingFlags.NONE, Shell.ActionMode.NORMAL,
            () => this._call('Undo', '(s)', [JSON.stringify(this._context())]));
        Main.wm.addKeybinding('quit', this._settings, Meta.KeyBindingFlags.NONE, Shell.ActionMode.NORMAL,
            () => {
                this._quit = true;
                this._call('Quit', '()', []);
                this._button.hide();
                this._tooltip.hide();
            });
        this._helper = Gio.Subprocess.new(['/usr/bin/python3', `${this.path}/linux/service.py`],
            Gio.SubprocessFlags.STDOUT_SILENCE);
        Gio.DBusProxy.new_for_bus(Gio.BusType.SESSION, Gio.DBusProxyFlags.DO_NOT_AUTO_START, null,
            BUS, PATH, BUS, null, (_, result) => {
                if (!this._enabled)
                    return;
                try {
                    this._proxy = Gio.DBusProxy.new_for_bus_finish(result);
                    this._proxySignal = this._proxy.connect('g-signal', (_, sender, name, params) => {
                        if (name === 'ResultReady')
                            this._call('Commit', '(ss)', [params.deep_unpack()[0], JSON.stringify(this._context())]);
                    });
                } catch (error) {
                    console.error(`AskBetter service: ${error.message}`);
                }
            });
        this._watchWindow();
        this._timer = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 120, () => {
            this._poll();
            return GLib.SOURCE_CONTINUE;
        });
    }

    _call(method, signature, arguments_, callback = null) {
        if (!this._proxy || !this._proxy.get_name_owner())
            return;
        this._proxy.call(method, new GLib.Variant(signature, arguments_), Gio.DBusCallFlags.NONE, 10000, null,
            (proxy, result) => {
                if (!this._enabled)
                    return;
                try {
                    const values = proxy.call_finish(result).deep_unpack();
                    callback?.(values, null);
                } catch (error) {
                    callback?.(null, error);
                }
            });
    }

    _context() {
        const window = global.display.focus_window;
        if (!window || !/^(codex|chatgpt)$/i.test(window.get_wm_class() ?? ''))
            return {};
        const rect = window.get_buffer_rect();
        return {pid: window.get_pid(), windowId: String(window.get_stable_sequence()), title: window.get_title(),
            buffer: {x: rect.x, y: rect.y, width: rect.width, height: rect.height}, moving: this._moving};
    }

    _watchWindow() {
        if (this._window) {
            for (const id of this._windowSignals)
                this._window.disconnect(id);
        }
        this._window = global.display.focus_window;
        this._windowSignals = [];
        if (this._window) {
            for (const event of ['position-changed', 'size-changed', 'notify::title'])
                this._windowSignals.push(this._window.connect(event, () => this._hideDuringLayout()));
        }
        this._hideDuringLayout();
    }

    _hideDuringLayout() {
        this._hiddenUntil = GLib.get_monotonic_time()+300000;
        this._button.hide();
        this._tooltip.hide();
        this._menu.close();
    }

    _poll() {
        if (this._quit || !this._proxy?.get_name_owner()) {
            this._button.hide();
            this._tooltip.hide();
            return;
        }
        if (this._inflight)
            return;
        const context = this._context();
        if (!context.pid && !this._menu.isOpen) {
            this._button.hide();
            this._tooltip.hide();
        }
        this._inflight = true;
        this._call('Poll', '(s)', [JSON.stringify(context)], (values, error) => {
            this._inflight = false;
            if (error) {
                this._button.hide();
                return;
            }
            try {
                const state = JSON.parse(values[0]);
                this._caption = state.caption+(state.notice ? `\n${state.notice}` : '');
                this._button.accessible_name = this._caption;
                for (const [style, item] of Object.entries(this._styleItems)) {
                    item.setOrnament(style === state.settings.lengthStyle ? PopupMenu.Ornament.CHECK : PopupMenu.Ornament.NONE);
                    item.setSensitive(!state.busy);
                }
                for (const item of this._menu._getMenuItems())
                    item.setSensitive(!state.busy);
                if (state.busy && !this._busyLabel) {
                    this._busyLabel = new St.Label({text: '…'});
                    this._button.set_child(this._busyLabel);
                } else if (!state.busy && this._busyLabel) {
                    this._button.set_child(this._icon);
                    this._busyLabel.destroy();
                    this._busyLabel = null;
                }
                const current = this._context();
                if (state.visible && current.windowId === state.target && !this._moving &&
                    GLib.get_monotonic_time() >= this._hiddenUntil && !Main.overview.visible &&
                    [state.x, state.y, state.size].every(Number.isFinite)) {
                    this._button.set_position(state.x, state.y);
                    this._button.set_size(state.size, state.size);
                    this._button.show();
                } else if (!this._menu.isOpen) {
                    this._button.hide();
                    this._tooltip.hide();
                }
            } catch (exception) {
                this._button.hide();
                console.error(`AskBetter state: ${exception.message}`);
            }
        });
    }

    disable() {
        this._call('Quit', '()', []);
        if (this._helper)
            this._helper.send_signal(15);
        this._enabled = false;
        if (this._timer)
            GLib.source_remove(this._timer);
        if (this._window) {
            for (const id of this._windowSignals)
                this._window.disconnect(id);
        }
        for (const id of [this._focusSignal, this._grabBegin, this._grabEnd]) {
            if (id)
                global.display.disconnect(id);
        }
        if (this._stageSignal)
            global.stage.disconnect(this._stageSignal);
        Main.wm.removeKeybinding('undo');
        Main.wm.removeKeybinding('quit');
        this._menu?.destroy();
        this._button?.destroy();
        this._tooltip?.destroy();
        this._proxy = this._helper = this._window = this._settings = null;
        this._timer = this._focusSignal = this._stageSignal = this._grabBegin = this._grabEnd = 0;
    }
}
