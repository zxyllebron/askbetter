import AppKit

final class Fixture: NSObject, NSApplicationDelegate {
    var window:NSWindow!
    var editor:NSTextView!
    var model:NSButton!
    var timer:Timer?
    var lastID=0
    let folder=URL(fileURLWithPath:CommandLine.arguments[1],isDirectory:true)
    func applicationDidFinishLaunching(_ notification:Notification) {
        window=NSWindow(contentRect:NSRect(x:120,y:140,width:900,height:600),styleMask:[.titled,.closable,.resizable,.miniaturizable],backing:.buffered,defer:false)
        window.isReleasedWhenClosed=false
        window.title="AskBetter fixture — native macOS editor"
        let content=window.contentView!
        let scroll=NSScrollView(frame:NSRect(x:20,y:70,width:860,height:510))
        scroll.autoresizingMask=[.width,.height]; scroll.hasVerticalScroller=true
        editor=NSTextView(frame:scroll.bounds); editor.isRichText=false; editor.string="原稿：IMU、UDP 与机械臂。"
        editor.font=NSFont.systemFont(ofSize:14); editor.autoresizingMask=[.width]
        editor.textContainer?.widthTracksTextView=true; editor.setAccessibilityLabel("Work with Codex")
        scroll.documentView=editor; content.addSubview(scroll)
        model=NSButton(frame:NSRect(x:500,y:20,width:200,height:30))
        model.title="GPT-6 Luna"; model.bezelStyle = .rounded; model.autoresizingMask=[.minXMargin]
        content.addSubview(model)
        let send=NSButton(frame:NSRect(x:730,y:20,width:130,height:30))
        send.title="Send"; send.target=self; send.action=#selector(sent); send.autoresizingMask=[.minXMargin]
        content.addSubview(send)
        window.center(); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true)
        let ready:[String:Any]=["pid":ProcessInfo.processInfo.processIdentifier,"ready":true]
        try! JSONSerialization.data(withJSONObject:ready).write(to:folder.appendingPathComponent("ready.json"))
        timer=Timer(timeInterval:0.05,repeats:true) { _ in
            guard let data=try? Data(contentsOf:self.folder.appendingPathComponent("command.json")),
                  let value=try? JSONSerialization.jsonObject(with:data) as? [String:Any],
                  let identifier=value["id"] as? Int,identifier>self.lastID else { return }
            self.lastID=identifier
            if let text=value["text"] as? String { self.editor.string=text }
            if let label=value["editorLabel"] as? String { self.editor.setAccessibilityLabel(label) }
            if let label=value["modelLabel"] as? String { self.model.title=label }
            if let hidden=value["modelHidden"] as? Bool { self.model.isHidden=hidden }
            if let title=value["title"] as? String { self.window.title=title }
            if let minimized=value["minimized"] as? Bool { if minimized { self.window.miniaturize(nil) } else { self.window.deminiaturize(nil) } }
            if value["focus"] as? Bool == true { self.window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true) }
            if value["close"] as? Bool == true { self.window.close() }
            if value["quit"] as? Bool == true { NSApp.terminate(nil) }
            try? JSONSerialization.data(withJSONObject:["id":identifier,"text":self.editor.string]).write(to:self.folder.appendingPathComponent("reply.json"),options:.atomic)
        }
        RunLoop.main.add(timer!,forMode:.common)
    }
    @objc func sent() { try? Data("sent".utf8).write(to:folder.appendingPathComponent("sent.txt")) }
}

let app=NSApplication.shared
let fixture=Fixture()
app.delegate=fixture
app.setActivationPolicy(.regular)
app.run()
