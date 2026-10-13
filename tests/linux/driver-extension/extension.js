import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';

const XML=`<node><interface name="org.askbetter.Tests">
<method name="State"><arg type="s" direction="out"/></method>
<method name="Action"><arg type="s" direction="in"/></method>
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
    State() {
        const extension=this._extension();
        const context=extension._context();
        const window=global.display.focus_window;
        return JSON.stringify({context,focusedClass:window?.get_wm_class(),focusedTitle:window?.get_title(),
            visible:extension._button.visible,x:extension._button.x,y:extension._button.y,
            size:extension._button.width,caption:extension._caption,
            menu:extension._menu._getMenuItems().map(item=>item.label?.text)});
    }
    Action(action) {
        const extension=this._extension();
        if (action === 'desktop') {
            Main.overview.hide();
            return;
        }
        const window=global.display.focus_window;
        if (!window?.get_title()?.startsWith('AskBetter fixture'))
            throw new Error('Test driver only operates on its own fixture');
        if (action === 'click')
            extension._button.emit('clicked');
        else if (action === 'resize') {
            const rect=window.get_frame_rect();
            window.move_resize_frame(true,rect.x,rect.y,rect.width-100,rect.height-60);
        } else if (action === 'undo')
            extension._call('Undo','(s)',[JSON.stringify(extension._context())]);
        else if (['brief','moderate','detailed'].includes(action))
            extension._styleItems[action].emit('activate');
        else
            throw new Error('Unknown test action');
    }
    disable() {
        this._object?.unexport();
        if(this._name) Gio.bus_unown_name(this._name);
    }
}
