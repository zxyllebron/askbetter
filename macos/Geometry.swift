import Foundation

struct ABRect: Equatable {
    var x: Double
    var y: Double
    var width: Double
    var height: Double
    var right: Double { x + width }
    var bottom: Double { y + height }
    var valid: Bool { width > 0 && height > 0 }
}

func nearModel(_ editor: ABRect, _ model: ABRect) -> Bool {
    let beside = model.x >= editor.right && model.x-editor.right <= 32 && model.y < editor.bottom && model.bottom > editor.y
    return editor.valid && model.valid && model.y >= editor.y-8 && model.y <= editor.bottom+160 && ((model.x < editor.right && model.right > editor.x) || beside)
}

func matches(_ text: String, _ pattern: String) -> Bool {
    text.range(of: pattern, options: [.regularExpression, .caseInsensitive]) != nil
}

func modelLabel(_ text: String) -> Bool {
    matches(text, "GPT[ .-]?[0-9]|(?:select|choose|change).*model|选择.*模型|切换.*模型|^(?:Auto|Automatic|Instant|Thinking|Pro|Fast|自动|快速|思考|ChatGPT|Codex)$")
}

func composerLabel(_ text: String) -> Bool {
    matches(text, "^(?:Work with (?:Codex|ChatGPT)|Ask (?:Codex|ChatGPT|anything)|Message(?: (?:Codex|ChatGPT))?|Send a message|Type a message|输入(?:消息|提示词)|发送消息|向.*发送消息|询问.*|有什么可以帮忙的)[. …。]*$")
}

func excludedEditor(_ text: String) -> Bool {
    matches(text, "search|find|reply|response|answer|rename|title|搜索|查找|回复|回答|重命名|标题")
}

// A labelled composer remains usable when an account has no model picker.
// Keep the fallback outside editable text; never guess from draft contents.
func composerAnchor(_ editor: ABRect, model: ABRect?, window: ABRect, size: Double = 26, gap: Double = 8) -> ABRect? {
    if let model = model {
        if model.x >= editor.right && model.y < editor.bottom {
            return ABRect(x:model.x-gap-size,y:editor.y-gap-size,width:size,height:size)
        }
        return buttonAnchor(model,size:size,gap:gap)
    }
    let y = editor.bottom+gap+size+gap <= window.bottom ? editor.bottom+gap : editor.y-gap-size
    guard y >= window.y, y+size <= window.bottom, editor.width >= size+2*gap else { return nil }
    return ABRect(x:editor.right-gap-size,y:y,width:size,height:size)
}

func buttonAnchor(_ model: ABRect, size: Double = 26, gap: Double = 8) -> ABRect {
    ABRect(x: model.x-gap-size, y: model.y+(model.height-size)/2, width: size, height: size)
}

struct SettleGate {
    var previous: ABRect?
    var changedAt: TimeInterval = 0
    mutating func update(_ rectangle: ABRect, now: TimeInterval) -> Bool {
        if previous != rectangle { previous = rectangle; changedAt = now }
        return now-changedAt >= 0.3
    }
    mutating func invalidate(now: TimeInterval) { changedAt = now }
}
