import AppKit
import ApplicationServices

func attribute(_ element: AXUIElement, _ name: CFString) -> AnyObject? {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(element, name, &value) == .success else { return nil }
    return value
}

func textAttribute(_ element: AXUIElement, _ name: CFString) -> String {
    attribute(element, name) as? String ?? ""
}

func rectangle(_ element: AXUIElement) -> ABRect? {
    guard let rawPosition = attribute(element, kAXPositionAttribute as CFString),
          let rawSize = attribute(element, kAXSizeAttribute as CFString),
          CFGetTypeID(rawPosition) == AXValueGetTypeID(), CFGetTypeID(rawSize) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero
    var size = CGSize.zero
    guard AXValueGetValue(rawPosition as! AXValue, .cgPoint, &point),
          AXValueGetValue(rawSize as! AXValue, .cgSize, &size), size.width > 0, size.height > 0 else { return nil }
    return ABRect(x: point.x, y: point.y, width: size.width, height: size.height)
}

struct ComposerTarget {
    let pid: pid_t
    let window: AXUIElement
    let editor: AXUIElement
    let model: AXUIElement
    let title: String
    let documentTitle: String
    let document: AXUIElement?

    func read() throws -> String {
        guard let value = attribute(editor, kAXValueAttribute as CFString) as? String else {
            throw ABError.message("输入框未提供可读文本接口")
        }
        return value
    }

    func unchanged(_ draft: String) -> Bool {
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
              textAttribute(window, kAXTitleAttribute as CFString) == title,
              (document == nil || textAttribute(document!, kAXTitleAttribute as CFString) == documentTitle),
              rectangle(editor) != nil, (try? read()) == draft else { return false }
        let app = AXUIElementCreateApplication(pid)
        guard let raw = attribute(app, kAXFocusedWindowAttribute as CFString), CFGetTypeID(raw) == AXUIElementGetTypeID() else { return false }
        return CFEqual(raw, window)
    }
}

enum ABError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let value) = self { return value }; return nil }
}

final class DesktopAccess {
    private var cached: ComposerTarget?
    private var discoveredAt: TimeInterval = 0
    private var accessibilityPID: pid_t = 0

    func invalidate() { cached = nil }

    func discover(pid: pid_t, fresh: Bool = false) throws -> ComposerTarget {
        let now = ProcessInfo.processInfo.systemUptime
        let application = AXUIElementCreateApplication(pid)
        if accessibilityPID != pid {
            // Electron documents this flag for third-party assistive clients.
            // Unsupported native apps simply ignore it; OS permission is still required.
            _ = AXUIElementSetAttributeValue(application, "AXManualAccessibility" as CFString, kCFBooleanTrue)
            accessibilityPID = pid
        }
        let focused = attribute(application, kAXFocusedWindowAttribute as CFString)
        if !fresh, let target = cached, target.pid == pid, now-discoveredAt < 2,
           focused != nil, CFEqual(focused!, target.window),
           rectangle(target.editor) != nil, rectangle(target.model) != nil,
           textAttribute(target.window, kAXTitleAttribute as CFString) == target.title { return target }
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 0.15)
        guard let rawWindow = attribute(app, kAXFocusedWindowAttribute as CFString),
              CFGetTypeID(rawWindow) == AXUIElementGetTypeID() else { throw ABError.message("无法识别当前 Codex 窗口") }
        let window = rawWindow as! AXUIElement
        guard attribute(window, kAXMinimizedAttribute as CFString) as? Bool != true else {
            throw ABError.message("Codex 窗口已最小化")
        }
        let windowRect = rectangle(window)
        var stack: [(AXUIElement, AXUIElement?)] = [(window, nil)]
        var editors: [(AXUIElement, ABRect, AXUIElement?)] = []
        var models: [(AXUIElement, ABRect)] = []
        var count = 0
        while let (node, parentDocument) = stack.popLast(), count < 2500 {
            count += 1
            let role = textAttribute(node, kAXRoleAttribute as CFString)
            let document = role == "AXWebArea" ? node : parentDocument
            if ["AXTextArea", "AXTextField"].contains(role), let rect = rectangle(node),
               attribute(node, kAXEnabledAttribute as CFString) as? Bool != false {
                if let container = windowRect, rect.bottom < container.y || rect.y > container.bottom { continue }
                if attribute(node, kAXValueAttribute as CFString) is String { editors.append((node, rect, document)) }
            } else if ["AXButton", "AXPopUpButton", "AXComboBox", "AXMenuButton"].contains(role) {
                let name = textAttribute(node, kAXTitleAttribute as CFString)+" "+textAttribute(node, kAXDescriptionAttribute as CFString)
                if name.range(of: "GPT.?[56]|select model|选择模型", options: [.regularExpression, .caseInsensitive]) != nil,
                   let rect = rectangle(node) { models.append((node, rect)) }
            }
            if let children = attribute(node, kAXChildrenAttribute as CFString) as? [AXUIElement] {
                stack.append(contentsOf: children.map { ($0, document) })
            }
        }
        var choice: ComposerTarget?
        var bottom = -Double.infinity
        var distance = Double.infinity
        for (editor, inputRect, document) in editors {
            for (model, modelRect) in models where nearModel(inputRect, modelRect) {
                let gap = abs(modelRect.y-inputRect.bottom)
                if modelRect.bottom > bottom || (modelRect.bottom == bottom && gap < distance) {
                    bottom = modelRect.bottom; distance = gap
                    choice = ComposerTarget(pid: pid, window: window, editor: editor, model: model,
                        title: textAttribute(window, kAXTitleAttribute as CFString),
                        documentTitle: document.map { textAttribute($0, kAXTitleAttribute as CFString) } ?? "", document: document)
                }
            }
        }
        guard let target = choice else { throw ABError.message("无法识别 Codex 输入框或模型选择器") }
        cached = target; discoveredAt = now
        return target
    }

    func replace(_ target: ComposerTarget, expected: String, result: String, completion: @escaping (Error?) -> Void) {
        guard target.unchanged(expected) else { completion(ABError.message("输入框或草稿已变化，未覆盖；结果可在单独对话框查看")); return }
        var settable = DarwinBoolean(false)
        if AXUIElementIsAttributeSettable(target.editor, kAXValueAttribute as CFString, &settable) == .success,
           settable.boolValue {
            guard AXUIElementSetAttributeValue(target.editor, kAXValueAttribute as CFString, result as CFString) == .success,
                  (try? target.read()) == result else { completion(ABError.message("无法确认回填结果，请检查草稿")); return }
            completion(nil); return
        }
        // Only the verified editor receives Cmd+A/Cmd+V; never send Return.
        guard AXUIElementSetAttributeValue(target.editor, kAXFocusedAttribute as CFString, kCFBooleanTrue) == .success else {
            completion(ABError.message("输入框无法获得焦点，未覆盖")); return
        }
        DispatchQueue.main.asyncAfter(deadline: .now()+0.12) {
            guard target.unchanged(expected) else { completion(ABError.message("焦点或草稿发生变化，未覆盖")); return }
            let app = AXUIElementCreateApplication(target.pid)
            guard let focused = attribute(app, kAXFocusedUIElementAttribute as CFString), CFEqual(focused, target.editor) else {
                completion(ABError.message("焦点不在原输入框，未覆盖")); return
            }
            let pasteboard = NSPasteboard.general
            let saved = (pasteboard.pasteboardItems ?? []).map { item in
                item.types.compactMap { type -> (NSPasteboard.PasteboardType, Data)? in
                    item.data(forType: type).map { (type, $0) }
                }
            }
            pasteboard.clearContents(); pasteboard.setString(result, forType: .string)
            let change = pasteboard.changeCount
            Self.chord(0); Self.chord(9)
            DispatchQueue.main.asyncAfter(deadline: .now()+0.18) {
                let confirmed = (try? target.read()) == result
                if pasteboard.changeCount == change {
                    pasteboard.clearContents()
                    let restored = saved.map { pairs -> NSPasteboardItem in
                        let item = NSPasteboardItem()
                        for (type, value) in pairs { item.setData(value, forType: type) }
                        return item
                    }
                    if !restored.isEmpty { pasteboard.writeObjects(restored) }
                }
                completion(confirmed ? nil : ABError.message("无法确认回填；请检查草稿，结果可在单独对话框查看"))
            }
        }
    }

    private static func chord(_ key: CGKeyCode) {
        let down = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: true)
        let up = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: false)
        down?.flags = .maskCommand; up?.flags = .maskCommand
        down?.post(tap: .cghidEventTap); up?.post(tap: .cghidEventTap)
    }
}
