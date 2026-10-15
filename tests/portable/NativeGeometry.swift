import Foundation

@main
struct Checks {
    static func main() {
        let editor=ABRect(x:10,y:200,width:800,height:180)
        let model=ABRect(x:500,y:350,width:200,height:30)
        precondition(nearModel(editor,model))
        precondition(buttonAnchor(model) == ABRect(x:466,y:352,width:26,height:26))
        precondition(buttonAnchor(ABRect(x:-100,y:350,width:200,height:30)).x == -134)
        precondition(!nearModel(editor,ABRect(x:500,y:10,width:200,height:30)))
        for name in ["GPT-4o","GPT-6.1 Sol","Select ChatGPT model","Choose Codex model","Auto","Thinking","自动","选择模型"] { precondition(modelLabel(name),name) }
        for name in ["Work with Codex","Work with ChatGPT","Ask ChatGPT","Ask anything","Message ChatGPT","输入消息"] { precondition(composerLabel(name),name) }
        for name in ["Search chats","Reply...","Rename","搜索"] { precondition(excludedEditor(name),name) }
        let window=ABRect(x:0,y:0,width:1000,height:800)
        precondition(composerAnchor(editor,model:nil,window:window) == ABRect(x:776,y:388,width:26,height:26))
        let compact=ABRect(x:100,y:700,width:400,height:36)
        precondition(nearModel(compact,ABRect(x:504,y:700,width:100,height:30)))
        precondition(composerAnchor(compact,model:ABRect(x:504,y:700,width:100,height:30),window:window)!.bottom < compact.y)
        precondition(composerAnchor(ABRect(x:100,y:760,width:400,height:36),model:nil,window:window)!.bottom < 760)
        let send=ABRect(x:760,y:388,width:50,height:30)
        let fallback=composerAnchor(editor,model:nil,window:window,obstacles:[send])!
        precondition(fallback.right < send.x && fallback.y >= editor.bottom)
        var gate=SettleGate()
        precondition(!gate.update(model,now:0))
        precondition(gate.update(model,now:0.31))
        gate.invalidate(now:0.5)
        precondition(!gate.update(model,now:0.7))
        precondition(gate.update(model,now:0.81))
        print("macOS geometry and layout checks passed")
    }
}
