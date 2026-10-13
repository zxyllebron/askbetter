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
    editor.valid && model.valid && model.y >= editor.y-8 && model.y <= editor.bottom+160 && model.x < editor.right && model.right > editor.x
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
