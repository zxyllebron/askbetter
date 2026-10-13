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
        var gate=SettleGate()
        precondition(!gate.update(model,now:0))
        precondition(gate.update(model,now:0.31))
        gate.invalidate(now:0.5)
        precondition(!gate.update(model,now:0.7))
        precondition(gate.update(model,now:0.81))
        print("macOS geometry and layout checks passed")
    }
}
