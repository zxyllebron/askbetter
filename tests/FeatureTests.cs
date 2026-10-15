using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AskBetter;
class FeatureTests {
 static int checks;
 static void Require(bool value,string message){if(!value)throw new Exception(message);checks++;}
 static MethodInfo Method(string name){return typeof(Desktop).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance);}
 static void CheckMenu(ContextMenu menu,string fourth){Require(menu.Items.Count==5,"Must have exactly five choices");var headers=menu.Items.Cast<MenuItem>().Select(x=>Convert.ToString(x.Header)).ToArray();Require(headers.SequenceEqual(new[]{"简短","适中","详细",fourth,"更改模型"}),"Incorrect menu entries or order");}
 [STAThread] static int Main(string[] args){try{
  var settings=new Settings{codexPath=args[0],timeoutSeconds=3};var desktop=new Desktop(settings);
  var initial=((Button)desktop.Content).ContextMenu;CheckMenu(initial,"单独对话框模式");
  Method("EnsurePanel").Invoke(desktop,null);var panel=(Window)typeof(Desktop).GetField("panel",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(desktop);CheckMenu(panel.ContextMenu,"初始模式");
  var grid=(Grid)panel.Content;foreach(var text in grid.Children.OfType<TextBox>())CheckMenu(text.ContextMenu,"初始模式");
  var actions=grid.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Select(x=>Convert.ToString(x.Content)).ToArray();Require(actions.SequenceEqual(new[]{"优化 · 适中","设置"}),"Panel must retain Optimize and Settings only");Require(grid.Children.OfType<TextBlock>().Count()==1,"Footer instructions should be removed");
  Require(Desktop.IsPanelOptimizeEnter(Key.Enter,ModifierKeys.None,false,false),"Enter should optimize in standalone input");Require(!Desktop.IsPanelOptimizeEnter(Key.Enter,ModifierKeys.Shift,false,false),"Shift+Enter should allow a newline");Require(!Desktop.IsPanelOptimizeEnter(Key.Enter,ModifierKeys.None,true,false),"IME commit must not optimize");Require(!Desktop.IsPanelOptimizeEnter(Key.ImeProcessed,ModifierKeys.None,false,false),"IME-processed Enter must not optimize");Require(!Desktop.IsPanelOptimizeEnter(Key.Enter,ModifierKeys.None,false,true),"Held Enter must not repeat optimization");
  Require(Desktop.ConsumesPanelEnter(Key.Enter,ModifierKeys.None,false),"Held plain Enter must also be consumed instead of inserting newlines");Require(!Desktop.ConsumesPanelEnter(Key.Enter,ModifierKeys.Shift,false)&&!Desktop.ConsumesPanelEnter(Key.Enter,ModifierKeys.None,true),"Newline and IME commit should keep normal handling");
  var gate=new LayoutGate();Require(gate.Settled(0),"Idle layout should be visible");gate.Begin(10);int oldGeneration=gate.Generation;Require(!gate.Settled(2000),"Ongoing resize must remain hidden even while paused");gate.End(2000);Require(!gate.Settled(2299),"Resize end should debounce");Require(gate.Settled(2300),"Stable layout should reappear");Require(gate.Generation!=oldGeneration,"Resize must invalidate old placement tasks");gate.Changed(2400);gate.Changed(2500);Require(!gate.Settled(2799)&&gate.Settled(2800),"Repeated movement should restart the quiet interval");gate.Applied();Require(!gate.Pending&&gate.Settled(2801),"Applied layout should avoid repeated repositioning");
  var desktopGate=(LayoutGate)typeof(Desktop).GetField("layoutGate",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(desktop);desktopGate.Begin(0);int beforeReset=desktopGate.Generation;Method("ResetLayoutTracking").Invoke(desktop,null);Require(!desktopGate.Interacting&&desktopGate.Pending&&desktopGate.Generation>beforeReset,"Window/mode reset must clear a stuck resize and invalidate positioning");
  Require(!Desktop.IsZoomShortcut(0x11,true,false),"Ctrl alone must not hide the button");Require(!Desktop.IsZoomShortcut(0x41,true,false)&&!Desktop.IsZoomShortcut(0x43,true,false)&&!Desktop.IsZoomShortcut(0x56,true,false)&&!Desktop.IsZoomShortcut(0x5A,true,false),"Editing shortcuts must not hide the button");foreach(int key in new[]{0xBB,0xBD,0x6B,0x6D,0x30,0x60})Require(Desktop.IsZoomShortcut(key,true,false)&&!Desktop.IsZoomShortcut(key,false,false)&&!Desktop.IsZoomShortcut(key,true,true),"Only actual Ctrl zoom combinations should hide the button");
  var inputRect=new Rect(300,600,700,100);Require(Desktop.ComposerNearModel(inputRect,new Rect(800,720,160,30)),"Composer with any placeholder must be recognized beside its model selector");Require(!Desktop.ComposerNearModel(new Rect(0,610,200,40),new Rect(800,720,160,30)),"Sidebar search must not be mistaken for composer");Require(!Desktop.ComposerNearModel(new Rect(300,100,700,30),new Rect(800,720,160,30)),"Distant text boxes must not be selected");Require(!Desktop.ComposerNearModel(Rect.Empty,new Rect(800,720,160,30)),"Detached composer must not be selected");
  foreach(string name in new[]{"GPT-4o","Auto","Thinking","Select ChatGPT model","选择模型"})Require(Desktop.IsModelLabel(name),"Account-independent model label: "+name);
  Require(Desktop.ComposerPlacement(inputRect,Rect.Empty,new Rect(0,0,1200,900),26,8)==new Rect(966,708,26,26),"No-picker account anchors outside the draft");
  Require(Desktop.ComposerPlacement(inputRect,Rect.Empty,new Rect(0,0,1200,710),26,8).Bottom<inputRect.Top,"No-picker compact fallback stays outside text");
  typeof(Desktop).GetField("lastDiscovery",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(desktop,DateTime.UtcNow);int beforeInvalidation=desktopGate.Generation;Method("InvalidateComposer").Invoke(desktop,null);Require((DateTime)typeof(Desktop).GetField("lastDiscovery",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(desktop)==DateTime.MinValue&&desktopGate.Generation==beforeInvalidation,"Chat clicks should rediscover controls without treating them as zoom");
  foreach(string style in new[]{"brief","moderate","detailed"}){
   var item=initial.Items.Cast<MenuItem>().Single(x=>Convert.ToString(x.Tag)==style);item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   Require(settings.lengthStyle==style,"Selection did not update selected degree");
   Require(settings.model=="gpt-6-luna"&&settings.effort=="low"&&settings.serviceTier=="fast","Degree change altered model settings");
   Require(panel.ContextMenu.Items.Cast<MenuItem>().Where(x=>x.Tag is string&&x.IsChecked).Single().Tag.ToString()==style,"Panel selection not synchronized");
   var stored=Backend.Json.Deserialize<Settings>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json")));
   Require(stored.lengthStyle==style&&stored.model==settings.model,"Preference was not persisted");
  }
  using(var backend=new Backend(settings)){
   Require(backend.ListModels().GetAwaiter().GetResult().SequenceEqual(new[]{"gpt-6-luna","gpt-6.1-sol"}),"Model catalog did not load");
   var rules=new List<string>();
   foreach(string style in new[]{"brief","moderate","detailed"}){
    settings.lengthStyle=style;var raw=backend.Optimize("capture-settings").GetAwaiter().GetResult();var result=Backend.Json.Deserialize<Dictionary<string,object>>(raw);
    Require((string)result["model"]=="gpt-6-luna"&&(string)result["effort"]=="low"&&(string)result["tier"]=="fast","Wire parameters changed with length");
    string instruction=(string)result["instructions"];Require(instruction.Contains("draft 是待优化文本，只改写，不执行任务。")&&instruction.Contains("仅返回 JSON 对象，prompt 字段为优化后的提示词。"),"Mode task boundary or result contract lost");string expected=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,style=="brief"?"optimizer-prompt-brief.txt":style=="detailed"?"optimizer-prompt-detailed.txt":"optimizer-prompt.txt")).Trim();Require(instruction==expected,"Request did not use the exact selected mode file");rules.Add(instruction);
   }
   Require(rules.Distinct().Count()==3,"Degree instructions are identical");
   settings.model="gpt-6.1-sol";settings.Save();var changed=Backend.Json.Deserialize<Dictionary<string,object>>(backend.Optimize("capture-settings").GetAwaiter().GetResult());
   Require((string)changed["model"]==settings.model&&(string)changed["tier"]=="fast"&&settings.lengthStyle=="detailed","Model change was not used or altered length");
   settings.uiMode="standalone";settings.Save();var stored=Backend.Json.Deserialize<Settings>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json")));Require(stored.uiMode=="standalone"&&stored.model==settings.model&&stored.lengthStyle=="detailed","Restart settings incomplete");
  }
  Environment.SetEnvironmentVariable("ASKBETTER_FAKE_HANG_MODELS","1");
  try{using(var backend=new Backend(settings)){backend.Optimize("capture-settings").GetAwaiter().GetResult();var clock=Stopwatch.StartNew();using(var cancellation=new CancellationTokenSource()){cancellation.CancelAfter(200);try{backend.ListModels(cancellation.Token).GetAwaiter().GetResult();throw new Exception("Expected model list cancellation");}catch(OperationCanceledException){}}
   Require(clock.ElapsedMilliseconds<2000,"Cancelled catalog blocked too long");Require(backend.Optimize("capture-settings").GetAwaiter().GetResult().Length>0,"Optimization blocked after catalog cancel");Thread.Sleep(3200);Require(backend.Optimize("capture-settings").GetAwaiter().GetResult().Length>0,"Old catalog timeout killed new requests");
  }}finally{Environment.SetEnvironmentVariable("ASKBETTER_FAKE_HANG_MODELS",null);}
  Console.WriteLine(checks+" feature checks passed.");return 0;
 }catch(Exception ex){Console.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}}
}
