"""A real GTK editor fixture. No real Codex account is used in desktop CI."""
import json
import os
import sys
import threading
import gi
gi.require_version("Gtk","3.0")
from gi.repository import GLib, Gtk

GLib.set_application_name("Codex")
GLib.set_prgname("Codex")
windows=[]
editors=[]


def create(title, draft):
    window=Gtk.Window(title=title)
    window.set_wmclass("Codex","Codex")
    window.set_default_size(900,600)
    box=Gtk.Box(orientation=Gtk.Orientation.VERTICAL,spacing=12,margin=20)
    window.add(box)
    box.pack_start(Gtk.Label(label="AskBetter CI — simulated Codex editor"),False,False,0)
    scroll=Gtk.ScrolledWindow()
    editor=Gtk.TextView(wrap_mode=Gtk.WrapMode.WORD_CHAR)
    editor.get_accessible().set_name("Work with Codex")
    editor.get_buffer().set_text(draft)
    scroll.add(editor)
    box.pack_start(scroll,True,True,0)
    row=Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL,spacing=12)
    spacer=Gtk.Label(label="")
    row.pack_start(spacer,True,True,0)
    row.pack_start(Gtk.Button(label="GPT-6 Luna"),False,False,0)
    row.pack_start(Gtk.Button(label="Microphone"),False,False,0)
    send=Gtk.Button(label="Send")
    send.connect("clicked",lambda *_:print(json.dumps({"sent":True}),flush=True))
    row.pack_start(send,False,False,0)
    box.pack_start(row,False,False,0)
    window.show_all(); window.present()
    windows.append(window); editors.append(editor)
    return window


def handle(request):
    operation=request["op"]
    index=request.get("window",len(windows)-1)
    editor=editors[index]
    buffer=editor.get_buffer()
    if operation == "set":
        buffer.set_text(request["text"])
    elif operation == "new":
        create("AskBetter fixture 2",request["text"])
    elif operation == "focus":
        windows[index].present(); editor.grab_focus()
    elif operation == "quit":
        Gtk.main_quit()
    text=buffer.get_text(buffer.get_start_iter(),buffer.get_end_iter(),True)
    print(json.dumps({"id":request["id"],"text":text},ensure_ascii=False),flush=True)
    return False


def read():
    for line in sys.stdin:
        GLib.idle_add(handle,json.loads(line))


create("AskBetter fixture 1","原始 IMU 草稿，保留 UDP 和机械臂限制。")
threading.Thread(target=read,daemon=True).start()
print(json.dumps({"ready":True,"pid":os.getpid()}),flush=True)
Gtk.main()
