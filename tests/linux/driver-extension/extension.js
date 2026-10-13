import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Clutter from 'gi://Clutter';
import St from 'gi://St';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';

const XML=`<node><interface name="org.askbetter.Tests">
<method name="State"><arg type="s" direction="out"/></method>
<method name="Action"><arg type="s" direction="in"/></method>
<method name="ClipboardText"><arg type="s" direction="out"/></method>
</interface></node>`;

export default class Driver extends Extension {
    enable() {
        this._object=Gio.DBusExportedObject.wrapJSObject(XML,this);
        this._object.export(Gio.DBus.session,'/org/askbetter/Tests');
        this._name=Gio.bus_own_name_on_connection(Gio.DBus.session,'org.askbetter.Tests',Gio.BusNameOwnerFlags.NONE,null,null);
    }
    _extension() {
        return Main.extensionManager.lookup('askbetter@zxyllebron.github.io').stateObj;
    }
    ClipboardTextAsync(_, invocation) {
        St.Clipboard.get_default().get_text(St.ClipboardType.CLIPBOARD, (_, text) =>
            invocation.return_value(new GLib.Variant('(s)',[text ?? ''])));
    }
    State() {
        const extension=this._extension();
        const context=extension?._context?.() ?? {};
        const window=global.display.focus_window;
        return JSON.stringify({context,xwaylandDisplay:GLib.getenv('DISPLAY'),xwaylandAuthority:GLib.getenv('XAUTHORITY'),
            clipboardFormats:St.Clipboard.get_default().get_mimetypes(St.ClipboardType.CLIPBOARD),
            focusedClass:window?.get_wm_class(),focusedTitle:window?.get_title(),
            enabled:global.settings.get_strv('enabled-extensions'),disabled:global.settings.get_strv('disabled-extensions'),
            overview:Main.overview.visible,inflight:extension?._inflight,
            hiddenFor:((extension?._hiddenUntil ?? 0)-GLib.get_monotonic_time())/1000,
            serviceOwner:extension?._proxy?.get_name_owner(),lastState:extension?._lastState,
            visible:extension?._button?.visible,x:extension?._button?.x,y:extension?._button?.y,
            size:extension?._button?.width,caption:extension?._caption,
            menu:extension?._menu?._getMenuItems().map(item=>item.label?.text)});
    }
    Action(action) {
        const extension=this._extension();
        if (action === 'desktop') {
            Main.overview.hide();
            return;
        }
        if (action === 'focus1' || action === 'focus2') {
            Main.overview.hide();
            const target=global.display.get_tab_list(Meta.TabList.NORMAL_ALL,null)
                .find(candidate=>candidate.get_title() === `AskBetter fixture ${action.slice(-1)}`);
            if (!target)
                throw new Error('Fixture window is not available');
            target.activate(global.get_current_time());
            return;
        }
        const window=global.display.focus_window;
        if (!window?.get_title()?.startsWith('AskBetter fixture'))
            throw new Error('Test driver only operates on its own fixture');
        if (action === 'click')
            extension._button.emit('clicked',1);
        else if (action === 'clipboard-text')
            St.Clipboard.get_default().set_text(St.ClipboardType.CLIPBOARD,'原剪贴板：IMU / UDP');
        else if (action === 'ctrl-down' || action === 'ctrl-up') {
            this._keyboard ??= Clutter.get_default_backend().get_default_seat()
                .create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);
            this._keyboard.notify_keyval(GLib.get_monotonic_time(),Clutter.KEY_Control_L,
                action === 'ctrl-down' ? Clutter.KeyState.PRESSED : Clutter.KeyState.RELEASED);
        } else if (action === 'resize') {
            window.unmaximize(Meta.MaximizeFlags.BOTH);
            const rect=window.get_frame_rect();
            window.move_resize_frame(true,rect.x,rect.y,rect.width-100,rect.height-60);
        } else if (action === 'undo')
            extension._call('Undo','(s)',[JSON.stringify(extension._context())]);
        else if (['brief','moderate','detailed'].includes(action))
            extension._styleItems[action].activate(null);
        else
            throw new Error('Unknown test action');
    }
    disable() {
        this._object?.unexport();
        if(this._name) Gio.bus_unown_name(this._name);
    }
}
