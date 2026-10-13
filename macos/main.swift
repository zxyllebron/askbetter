import AppKit
import ApplicationServices

final class RoundPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class WandView: NSView {
    var action: (() -> Void)?
    var options: (() -> NSMenu)?
    var busy = false { didSet { needsDisplay = true } }
    private var hover = false
    private var tracking: NSTrackingArea?
    override var isFlipped: Bool { true }
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let value = tracking { removeTrackingArea(value) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self)
        addTrackingArea(area); tracking = area
    }
    override func mouseEntered(with event: NSEvent) { hover = true; needsDisplay = true }
    override func mouseExited(with event: NSEvent) { hover = false; needsDisplay = true }
    override func mouseDown(with event: NSEvent) {}
    override func mouseUp(with event: NSEvent) {
        if !busy && bounds.contains(convert(event.locationInWindow,from:nil)) { action?() }
    }
    override func rightMouseDown(with event: NSEvent) { if let menu = options?() { NSMenu.popUpContextMenu(menu, with: event, for: self) } }
    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: hover ? 65/255 : 48/255, alpha: 1).setFill()
        let circle = NSBezierPath(ovalIn: bounds.insetBy(dx: 0.5, dy: 0.5))
        circle.fill(); NSColor(calibratedWhite: 72/255, alpha: 1).setStroke(); circle.lineWidth = 1; circle.stroke()
        if busy {
            let text = "…" as NSString
            let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 16), .foregroundColor: NSColor(calibratedWhite: 220/255, alpha: 1)]
            let size = text.size(withAttributes: attributes)
            text.draw(at: NSPoint(x: (bounds.width-size.width)/2, y: (bounds.height-size.height)/2), withAttributes: attributes)
            return
        }
        NSGraphicsContext.saveGraphicsState()
        let transform = NSAffineTransform()
        transform.translateX(by:(bounds.width-16)/2,yBy:(bounds.height-16)/2)
        transform.scale(by:16/24.0); transform.concat()
        let path = NSBezierPath()
        path.move(to: NSPoint(x:16.5,y:3.5)); path.line(to:NSPoint(x:20.5,y:7.5)); path.line(to:NSPoint(x:7.5,y:20.5)); path.line(to:NSPoint(x:3.5,y:16.5)); path.close()
        for pair in [(13.5,6.5,17.5,10.5),(5.0,2.0,5.0,6.0),(3.0,4.0,7.0,4.0),(20.0,16.0,20.0,20.0),(18.0,18.0,22.0,18.0)] {
            path.move(to:NSPoint(x:pair.0,y:pair.1)); path.line(to:NSPoint(x:pair.2,y:pair.3))
        }
        NSColor(calibratedWhite:220/255,alpha:1).setStroke()
        path.lineWidth=1.7; path.lineCapStyle = .round; path.lineJoinStyle = .round; path.stroke()
        NSGraphicsContext.restoreGraphicsState()
    }
}

final class PromptTextView: NSTextView {
    var optimize: (() -> Void)?
    var options: (() -> NSMenu)?
    override func menu(for event: NSEvent) -> NSMenu? { options?() ?? super.menu(for:event) }
    override func keyDown(with event: NSEvent) {
        if (event.keyCode == 36 || event.keyCode == 76), !hasMarkedText(),
           event.modifierFlags.intersection([.shift,.command,.control,.option]).isEmpty {
            if !event.isARepeat { optimize?() }
            return
        }
        super.keyDown(with: event)
    }
}

final class ResultTextView: NSTextView {
    var options: (() -> NSMenu)?
    override func menu(for event: NSEvent) -> NSMenu? { options?() ?? super.menu(for:event) }
}

final class Controller: NSObject, NSApplicationDelegate, NSWindowDelegate {
    let config: URL
    var settings: [String: Any]
    let bridge: BackendBridge
    let desktop = DesktopAccess()
    let scanning = DispatchQueue(label: "org.askbetter.accessibility")
    var timer: Timer?
    var scanInProgress = false
    var generation = 0
    var gate = SettleGate()
    var busy = false
    var modelPicker = false
    var quitting = false
    var notice = ""
    var lastInput = ""
    var lastResult = ""
    var target: ComposerTarget?
    var undoTarget: ComposerTarget?
    var undoOriginal = ""
    var undoOptimized = ""
    let circle = RoundPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
    let wand = WandView(frame: NSRect(x:0,y:0,width:26,height:26))
    var statusItem: NSStatusItem?
    var panel: NSWindow?
    var inputView: PromptTextView?
    var outputView: NSTextView?
    var statusLabel: NSTextField?
    var optimizeButton: NSButton?
    var keyMonitor: Any?
    var observer: AXObserver?
    var observedPID: pid_t = 0
    var observedWindow: AXUIElement?
    var workspaceObserver: NSObjectProtocol?

    override init() {
        config = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/AskBetter/settings.json")
        let defaults: [String: Any] = ["model":"gpt-6-luna", "effort":"low", "serviceTier":"fast", "lengthStyle":"moderate", "uiMode":"initial", "codexPath":"", "timeoutSeconds":90, "buttonSize":26, "anchorGap":8]
        let stored = (try? Data(contentsOf: config)).flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String:Any] } ?? [:]
        settings = defaults.merging(stored) { _, right in right }
        bridge = BackendBridge(resources: Bundle.main.resourceURL!, configuration: config)
        super.init()
    }

    var style: String { settings["lengthStyle"] as? String ?? "moderate" }
    var standalone: Bool { settings["uiMode"] as? String == "standalone" }
    var caption: String { (settings["model"] as? String ?? "gpt-6-luna")+" · "+(["brief":"简短","moderate":"适中","detailed":"详细"][style] ?? "适中")+" · Low · Fast" }

    func save(_ changes: [String:Any]) throws {
        let next = settings.merging(changes) { _, right in right }
        try FileManager.default.createDirectory(at: config.deletingLastPathComponent(), withIntermediateDirectories: true,
                                               attributes: [.posixPermissions: 0o700])
        try JSONSerialization.data(withJSONObject: next, options: [.prettyPrinted,.sortedKeys]).write(to: config, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions:0o600], ofItemAtPath:config.path)
        settings = next
        statusLabel?.stringValue = caption
        refreshStatusMenu()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        circle.isOpaque = false; circle.backgroundColor = .clear
        circle.hasShadow = false; circle.level = .floating; circle.hidesOnDeactivate = false
        circle.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary]
        circle.contentView = wand
        wand.action = { [weak self] in self?.optimizeCurrent() }
        wand.options = { [weak self] in self?.optionsMenu(inPanel:false) ?? NSMenu() }
        statusItem = NSStatusBar.system.statusItem(withLength:NSStatusItem.variableLength)
        statusItem?.button?.title = "AskBetter"
        refreshStatusMenu()
        workspaceObserver = NSWorkspace.shared.notificationCenter.addObserver(forName:NSWorkspace.didActivateApplicationNotification, object:nil, queue:.main) { [weak self] _ in self?.layoutChanged(invalidate:true) }
        keyMonitor = NSEvent.addGlobalMonitorForEvents(matching:[.keyDown,.scrollWheel,.leftMouseUp]) { [weak self] event in self?.observe(event) }
        timer = Timer(timeInterval:0.2, repeats:true) { [weak self] _ in self?.track() }
        RunLoop.main.add(timer!, forMode:.common)
        if CommandLine.arguments.contains("--standalone") { try? save(["uiMode":"standalone"]) }
        if standalone { showPanel() }
        if !AXIsProcessTrusted() {
            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary
            _ = AXIsProcessTrustedWithOptions(options)
            setNotice("请在系统设置 → 隐私与安全性 → 辅助功能中允许 AskBetter")
        }
    }

    func setNotice(_ value:String) {
        notice=value; wand.toolTip="优化提示词 · "+caption+(value.isEmpty ? "" : "\n"+value)
        statusLabel?.stringValue=value.isEmpty ? caption : value
    }

    func layoutChanged(invalidate: Bool = false) {
        generation += 1; gate.invalidate(now:ProcessInfo.processInfo.systemUptime); circle.orderOut(nil)
        if invalidate { scanning.async { self.desktop.invalidate() } }
    }

    func observe(_ event:NSEvent) {
        if event.type == .keyDown, event.modifierFlags.contains(.option),
           event.modifierFlags.contains(.control) || event.modifierFlags.contains(.command) {
            if event.charactersIgnoringModifiers?.lowercased() == "z" { undo(); return }
            if event.charactersIgnoringModifiers?.lowercased() == "q" { NSApp.terminate(nil); return }
        }
        guard let app=NSWorkspace.shared.frontmostApplication, isCodex(app), !standalone else { return }
        if event.type == .leftMouseUp { scanning.async { self.desktop.invalidate() }; return }
        let modifier=event.modifierFlags.contains(.command) || event.modifierFlags.contains(.control)
        if modifier && !event.modifierFlags.contains(.option) &&
            (event.type == .scrollWheel || (event.type == .keyDown && ["+","=","-","0"].contains(event.charactersIgnoringModifiers ?? ""))) { layoutChanged() }
    }

    func isCodex(_ app:NSRunningApplication) -> Bool {
        let name=app.localizedName?.lowercased() ?? ""
        return name == "codex" || name == "chatgpt"
    }

    func watch(_ target:ComposerTarget) {
        if observedPID == target.pid, let window=observedWindow, CFEqual(window,target.window) { return }
        if let old=observer { CFRunLoopRemoveSource(CFRunLoopGetMain(),AXObserverGetRunLoopSource(old),.commonModes) }
        var value:AXObserver?
        let callback:AXObserverCallback = { _,_,_,info in
            guard let info=info else { return }
            let owner=Unmanaged<Controller>.fromOpaque(info).takeUnretainedValue()
            DispatchQueue.main.async { owner.layoutChanged(invalidate:true) }
        }
        guard AXObserverCreate(target.pid,callback,&value) == .success, let created=value else { return }
        observedPID=target.pid; observedWindow=target.window
        observer=created
        let app=AXUIElementCreateApplication(target.pid)
        let info=Unmanaged.passUnretained(self).toOpaque()
        for name in [kAXFocusedWindowChangedNotification,kAXWindowCreatedNotification] {
            AXObserverAddNotification(created,app,name as CFString,info)
        }
        for name in [kAXMovedNotification,kAXResizedNotification,kAXTitleChangedNotification,kAXUIElementDestroyedNotification] {
            AXObserverAddNotification(created,target.window,name as CFString,info)
        }
        CFRunLoopAddSource(CFRunLoopGetMain(),AXObserverGetRunLoopSource(created),.commonModes)
    }

    func track() {
        guard !standalone, !modelPicker, AXIsProcessTrusted(), let app=NSWorkspace.shared.frontmostApplication, isCodex(app) else { circle.orderOut(nil); return }
        guard !scanInProgress else { return }
        scanInProgress=true
        let pid=app.processIdentifier, stamp=generation
        scanning.async {
            let result:Result<ComposerTarget,Error>
            do { result = .success(try self.desktop.discover(pid:pid)) } catch { result = .failure(error) }
            DispatchQueue.main.async {
                self.scanInProgress=false
                guard !self.standalone, !self.modelPicker, self.generation == stamp,
                      NSWorkspace.shared.frontmostApplication?.processIdentifier == pid else { return }
                switch result {
                case .failure(let error): self.circle.orderOut(nil); self.setNotice(error.localizedDescription)
                case .success(let target):
                    self.target=target; self.watch(target)
                    guard let model=rectangle(target.model) else { self.circle.orderOut(nil); return }
                    let rect=buttonAnchor(model,size:(self.settings["buttonSize"] as? Double ?? 26),gap:(self.settings["anchorGap"] as? Double ?? 8))
                    guard self.gate.update(rect,now:ProcessInfo.processInfo.systemUptime) else { self.circle.orderOut(nil); return }
                    let top=NSScreen.screens.first?.frame.maxY ?? 0
                    self.circle.setFrame(NSRect(x:rect.x,y:top-rect.y-rect.height,width:rect.width,height:rect.height),display:true)
                    self.circle.orderFrontRegardless()
                    self.wand.toolTip="优化提示词 · "+self.caption+(self.notice.isEmpty ? "" : "\n"+self.notice)
                }
            }
        }
    }

    func optionsMenu(inPanel:Bool) -> NSMenu {
        let menu=NSMenu(); menu.autoenablesItems=false
        for (value,label) in [("brief","简短"),("moderate","适中"),("detailed","详细")] {
            let item=NSMenuItem(title:label,action:#selector(selectStyle(_:)),keyEquivalent:"")
            item.target=self; item.representedObject=value; item.state=style == value ? .on : .off
            item.isEnabled = !busy && !modelPicker; menu.addItem(item)
        }
        let mode=NSMenuItem(title:inPanel ? "初始模式" : "单独对话框模式",action:inPanel ? #selector(returnInitial) : #selector(showPanel),keyEquivalent:"")
        mode.target=self; mode.isEnabled = !busy && !modelPicker; menu.addItem(mode)
        let model=NSMenuItem(title:"更改模型",action:#selector(showModel),keyEquivalent:"")
        model.target=self; model.isEnabled = !busy && !modelPicker; menu.addItem(model)
        return menu
    }

    func refreshStatusMenu() {
        guard statusItem != nil else { return }
        let menu=optionsMenu(inPanel:standalone)
        menu.addItem(.separator())
        for (label,action,key) in [("撤销上次优化",#selector(undo),"z"),("退出 AskBetter",#selector(quit),"q")] {
            let item=NSMenuItem(title:label,action:action,keyEquivalent:key)
            item.target=self; item.keyEquivalentModifierMask=[.control,.option]; menu.addItem(item)
        }
        statusItem?.menu=menu
    }

    @objc func selectStyle(_ sender:NSMenuItem) {
        guard !busy, let value=sender.representedObject as? String else { return }
        do { try save(["lengthStyle":value]); setNotice(caption) } catch { setNotice(error.localizedDescription) }
    }

    func optimizeCurrent() {
        guard !busy, !modelPicker, let target=target else { return }
        do {
            let draft=try target.read()
            guard target.unchanged(draft) else { setNotice("输入框已变化，请稍后重试"); return }
            run(draft,target:target)
        } catch { setNotice(error.localizedDescription) }
    }

    func run(_ draft:String, target:ComposerTarget? = nil) {
        guard !busy, !modelPicker else { return }
        busy=true; wand.busy=true; optimizeButton?.isEnabled=false; refreshStatusMenu(); setNotice("优化中 · "+caption)
        bridge.request("optimize",draft:draft,settings:settings) { response in
            switch response {
            case .failure(let error): self.finish(error.localizedDescription)
            case .success(let result):
                guard let output=result["prompt"] as? String, !output.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty else { self.finish("优化结果为空，原稿已保留"); return }
                self.lastInput=draft; self.lastResult=output
                self.outputView?.string=output
                if let target=target {
                    self.desktop.replace(target,expected:draft,result:output) { error in
                        if let error=error { self.finish(error.localizedDescription) }
                        else { self.undoTarget=target; self.undoOriginal=draft; self.undoOptimized=output; self.finish("优化完成；Ctrl+Option+Z 撤销") }
                    }
                } else { self.finish(self.inputView?.string == draft ? "优化完成 · "+self.caption : "优化完成，结果对应点击时的原稿；新输入已保留。") }
            }
        }
    }

    func finish(_ message:String) { busy=false; wand.busy=false; optimizeButton?.isEnabled=true; refreshStatusMenu(); setNotice(message) }

    @objc func undo() {
        guard !busy, let target=undoTarget else { return }
        busy=true
        desktop.replace(target,expected:undoOptimized,result:undoOriginal) { error in
            if error == nil { self.undoTarget=nil }
            self.finish(error?.localizedDescription ?? "已恢复原稿")
        }
    }

    func textArea(editable:Bool) -> (NSScrollView,NSTextView) {
        let view:NSTextView=editable ? PromptTextView() : ResultTextView()
        view.isEditable=editable; view.isSelectable=true; view.isRichText=false
        view.font=NSFont.systemFont(ofSize:14); view.textColor = .white
        view.backgroundColor=NSColor(calibratedWhite:52/255,alpha:1); view.textContainerInset=NSSize(width:10,height:10)
        view.autoresizingMask=[.width]; view.isVerticallyResizable=true; view.isHorizontallyResizable=false
        view.textContainer?.widthTracksTextView=true
        view.menu=optionsMenu(inPanel:true)
        if let input=view as? PromptTextView { input.options={ [weak self] in self?.optionsMenu(inPanel:true) ?? NSMenu() } }
        if let output=view as? ResultTextView { output.options={ [weak self] in self?.optionsMenu(inPanel:true) ?? NSMenu() } }
        let scroll=NSScrollView(); scroll.hasVerticalScroller=true; scroll.borderType = .lineBorder
        scroll.documentView=view; scroll.translatesAutoresizingMaskIntoConstraints=false
        return (scroll,view)
    }

    @objc func showPanel() {
        guard !busy, !modelPicker else { return }
        do { try save(["uiMode":"standalone"]) } catch { setNotice(error.localizedDescription); return }
        circle.orderOut(nil)
        if panel == nil {
            let window=NSWindow(contentRect:NSRect(x:0,y:0,width:600,height:480),styleMask:[.titled,.closable,.resizable,.miniaturizable],backing:.buffered,defer:false)
            window.title="AskBetter · 单独对话框"; window.minSize=NSSize(width:430,height:340)
            window.appearance=NSAppearance(named:.darkAqua); window.delegate=self; window.isReleasedWhenClosed=false
            let content=window.contentView!
            let status=NSTextField(wrappingLabelWithString:caption); status.translatesAutoresizingMaskIntoConstraints=false
            let (inputScroll,input)=textArea(editable:true), (outputScroll,output)=textArea(editable:false)
            inputView=input as? PromptTextView; inputView?.optimize={ [weak self] in self?.run(self?.inputView?.string ?? "") }
            outputView=output; statusLabel=status
            input.string=lastInput; output.string=lastResult
            let optimize=NSButton(title:"优化",target:self,action:#selector(runPanel)); optimizeButton=optimize
            let settingsButton=NSButton(title:"设置",target:self,action:#selector(showOptions(_:)))
            let row=NSStackView(views:[optimize,settingsButton]); row.orientation = .horizontal; row.spacing=10; row.translatesAutoresizingMaskIntoConstraints=false
            for view in [status,inputScroll,row,outputScroll] { content.addSubview(view) }
            NSLayoutConstraint.activate([
                status.topAnchor.constraint(equalTo:content.topAnchor,constant:18),status.leadingAnchor.constraint(equalTo:content.leadingAnchor,constant:18),status.trailingAnchor.constraint(equalTo:content.trailingAnchor,constant:-18),
                inputScroll.topAnchor.constraint(equalTo:status.bottomAnchor,constant:10),inputScroll.leadingAnchor.constraint(equalTo:status.leadingAnchor),inputScroll.trailingAnchor.constraint(equalTo:status.trailingAnchor),
                row.topAnchor.constraint(equalTo:inputScroll.bottomAnchor,constant:10),row.leadingAnchor.constraint(equalTo:status.leadingAnchor),
                outputScroll.topAnchor.constraint(equalTo:row.bottomAnchor,constant:10),outputScroll.leadingAnchor.constraint(equalTo:status.leadingAnchor),outputScroll.trailingAnchor.constraint(equalTo:status.trailingAnchor),outputScroll.bottomAnchor.constraint(equalTo:content.bottomAnchor,constant:-18),
                inputScroll.heightAnchor.constraint(equalTo:outputScroll.heightAnchor),inputScroll.heightAnchor.constraint(greaterThanOrEqualToConstant:80)])
            panel=window; window.center()
        }
        inputView?.menu=optionsMenu(inPanel:true); outputView?.menu=optionsMenu(inPanel:true)
        panel?.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true)
    }

    @objc func runPanel() { run(inputView?.string ?? "") }
    @objc func showOptions(_ sender:NSButton) { optionsMenu(inPanel:true).popUp(positioning:nil,at:NSPoint(x:0,y:sender.bounds.height),in:sender) }
    @objc func returnInitial() {
        guard !busy, !modelPicker else { return }
        do { try save(["uiMode":"initial"]) } catch { setNotice(error.localizedDescription); return }
        panel?.orderOut(nil); layoutChanged(invalidate:true)
        if let target=target { NSRunningApplication(processIdentifier:target.pid)?.activate(options:[]) }
    }
    func windowShouldClose(_ sender:NSWindow) -> Bool { if sender === panel { returnInitial(); return false }; return true }

    @objc func showModel() {
        guard !busy, !modelPicker else { return }
        modelPicker=true; circle.orderOut(nil)
        let alert=NSAlert(); alert.messageText="更改优化模型"; alert.informativeText="选择或输入模型标识（保持 Low / Fast）"
        alert.addButton(withTitle:"保存"); alert.addButton(withTitle:"取消")
        let combo=NSComboBox(frame:NSRect(x:0,y:0,width:360,height:28)); combo.isEditable=true
        combo.stringValue=settings["model"] as? String ?? "gpt-6-luna"; alert.accessoryView=combo
        var alive=true
        bridge.request("models",settings:settings) { result in
            guard alive, case .success(let value)=result, let models=value["models"] as? [String] else { return }
            let text=combo.stringValue; combo.addItems(withObjectValues:models); combo.stringValue=text
        }
        NSApp.activate(ignoringOtherApps:true)
        while alert.runModal() == .alertFirstButtonReturn {
            let value=combo.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
            guard value.range(of:"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$",options:.regularExpression) != nil else { alert.informativeText="请输入有效的模型标识"; continue }
            do { try save(["model":value]); setNotice(caption); break }
            catch { alert.informativeText=error.localizedDescription }
        }
        alive=false; modelPicker=false; refreshStatusMenu()
        if !standalone, let target=target { NSRunningApplication(processIdentifier:target.pid)?.activate(options:[]) }
    }

    @objc func quit() { NSApp.terminate(nil) }
    func applicationWillTerminate(_ notification:Notification) {
        quitting=true; timer?.invalidate(); bridge.close()
        if let value=keyMonitor { NSEvent.removeMonitor(value) }
        if let value=workspaceObserver { NSWorkspace.shared.notificationCenter.removeObserver(value) }
        if let value=observer { CFRunLoopRemoveSource(CFRunLoopGetMain(),AXObserverGetRunLoopSource(value),.commonModes) }
    }
}

if let probe=CommandLine.arguments.firstIndex(of:"--desktop-probe"), CommandLine.arguments.count>probe+2 {
    let app=NSApplication.shared
    app.setActivationPolicy(.accessory)
    let pid=pid_t(CommandLine.arguments[probe+1])!
    let folder=URL(fileURLWithPath:CommandLine.arguments[probe+2],isDirectory:true)
    guard let fixture=NSRunningApplication(processIdentifier:pid),
          fixture.bundleIdentifier == "io.github.zxyllebron.askbetter.fixture",AXIsProcessTrusted() else {
        print("Fixture-only probe requires Accessibility permission"); exit(1)
    }
    fixture.activate(options:[])
    let access=DesktopAccess()
    var attempts=0
    var timer:Timer?
    timer=Timer(timeInterval:0.1,repeats:true) { _ in
        attempts += 1
        guard attempts<100 else { print("Native fixture focus timeout"); exit(1) }
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid else { return }
        do {
            let target=try access.discover(pid:pid,fresh:true)
            let draft=try target.read()
            guard draft == "原稿：IMU、UDP 与机械臂。" else { throw ABError.message("Fixture draft mismatch") }
            timer?.invalidate()
            access.replace(target,expected:draft,result:"优化：IMU、UDP 与机械臂。") { error in
                if let error=error { print(error.localizedDescription); exit(1) }
                let replacement=(try? target.read()) == "优化：IMU、UDP 与机械臂。"
                access.replace(target,expected:draft,result:"must not replace changed text") { staleError in
                    let changed=staleError != nil && (try? target.read()) == "优化：IMU、UDP 与机械臂。"
                    let command:[String:Any]=["id":1,"title":"Changed fixture title"]
                    try! JSONSerialization.data(withJSONObject:command).write(to:folder.appendingPathComponent("command.json"),options:.atomic)
                    var checks=0
                    var wait:Timer?
                    wait=Timer(timeInterval:0.05,repeats:true) { _ in
                        checks += 1
                        guard checks<100 else { exit(1) }
                        guard textAttribute(target.window,kAXTitleAttribute as CFString) == "Changed fixture title" else { return }
                        wait?.invalidate()
                        access.replace(target,expected:"优化：IMU、UDP 与机械臂。",result:"must not replace a different context") { titleError in
                            let report:[String:Any]=["read":true,"replacement":replacement,"changedDraftRejected":changed,
                                "titleChangeRejected":titleError != nil,"buttonSize":26,"anchorGap":8]
                            try! JSONSerialization.data(withJSONObject:report,options:[.sortedKeys,.prettyPrinted]).write(to:folder.appendingPathComponent("probe.json"))
                            app.stop(nil)
                        }
                    }
                    RunLoop.main.add(wait!,forMode:.common)
                }
            }
        } catch { print(error.localizedDescription); exit(1) }
    }
    RunLoop.main.add(timer!,forMode:.common)
    app.run()
} else if let smoke=CommandLine.arguments.firstIndex(of:"--ui-smoke"), CommandLine.arguments.count>smoke+1 {
    let app=NSApplication.shared
    app.setActivationPolicy(.accessory)
    let controller=Controller()
    controller.showPanel()
    let output=URL(fileURLWithPath:CommandLine.arguments[smoke+1],isDirectory:true)
    try! FileManager.default.createDirectory(at:output,withIntermediateDirectories:true)
    precondition(controller.optionsMenu(inPanel:false).items.map{$0.title} == ["简短","适中","详细","单独对话框模式","更改模型"])
    precondition(controller.optionsMenu(inPanel:true).items.map{$0.title} == ["简短","适中","详细","初始模式","更改模型"])
    var count=0
    let text=controller.inputView!
    text.optimize={ count += 1 }
    func key(_ flags:NSEvent.ModifierFlags = [], repeated:Bool = false) -> NSEvent {
        NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:flags,timestamp:0,
            windowNumber:controller.panel!.windowNumber,context:nil,characters:"\r",charactersIgnoringModifiers:"\r",isARepeat:repeated,keyCode:36)!
    }
    text.keyDown(with:key()); precondition(count == 1)
    text.keyDown(with:key(repeated:true)); precondition(count == 1)
    text.keyDown(with:key(.shift)); precondition(count == 1)
    text.setMarkedText("拼音",selectedRange:NSRange(location:0,length:2),replacementRange:NSRange(location:NSNotFound,length:0))
    precondition(text.hasMarkedText())
    text.keyDown(with:key()); precondition(count == 1)
    text.unmarkText()
    text.string="参考图片格式，将胸部相机 IMU 数据通过 UDP 正常发出。"
    controller.outputView?.string="按参考图片中的格式导出并通过 UDP 发送胸部相机 IMU 数据。当前只实现发送端，机械臂接收功能待改装后实现。"
    func png(_ view:NSView,_ name:String) {
        view.layoutSubtreeIfNeeded()
        let bitmap=view.bitmapImageRepForCachingDisplay(in:view.bounds)!
        view.cacheDisplay(in:view.bounds,to:bitmap)
        try! bitmap.representation(using:.png,properties:[:])!.write(to:output.appendingPathComponent(name))
    }
    png(controller.panel!.contentView!,"macos-standalone.png")
    let wand=WandView(frame:NSRect(x:0,y:0,width:26,height:26))
    png(wand,"macos-circle.png")
    controller.returnInitial()
    let report:[String:Any]=["menus":5,"enter":true,"shiftEnter":true,"repeatedEnterIgnored":true,"markedTextEnterIgnored":true,"buttonSize":26,"realCodex":false]
    try! JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]).write(to:output.appendingPathComponent("macos-ui-smoke.json"))
    print("Native macOS menu, Enter/IME and rendering checks passed")
} else if CommandLine.arguments.contains("--diagnose") {
    let config=Controller()
    let payload:[String:Any] = ["platform":"macOS", "version":"1.1.0", "accessibility":AXIsProcessTrusted(),
                               "buttonSize":26, "anchorGap":8, "menuItems":5,
                               "backendBundled":FileManager.default.isExecutableFile(atPath:Bundle.main.resourceURL!.appendingPathComponent("askbetter-core").path)]
    print(String(data:try! JSONSerialization.data(withJSONObject:payload,options:.sortedKeys),encoding:.utf8)!)
    _=config
} else {
    let app=NSApplication.shared
    let controller=Controller()
    app.delegate=controller
    app.run()
}
