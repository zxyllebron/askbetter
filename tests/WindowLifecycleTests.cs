using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Interop;
using System.Windows.Threading;
using AskBetter;

// Only this test program's own disposable WPF windows are activated/closed.
// Freeze discovery to reproduce an unresponsive UI Automation provider.
class WindowLifecycleTests {
 sealed class DelayedTextBox:TextBox {public bool RejectWrites;protected override AutomationPeer OnCreateAutomationPeer(){return new DelayedPeer(this);}}
 sealed class DelayedPeer:TextBoxAutomationPeer,IValueProvider {
  public DelayedPeer(DelayedTextBox owner):base(owner){}
  public override object GetPattern(PatternInterface pattern){return pattern==PatternInterface.Value?this:base.GetPattern(pattern);}
  bool IValueProvider.IsReadOnly{get{return false;}}
  string IValueProvider.Value{get{Thread.Sleep(800);return ((TextBox)Owner).Text;}}
  void IValueProvider.SetValue(string value){if(((DelayedTextBox)Owner).RejectWrites)throw new InvalidOperationException("Fixture rejects writes");((TextBox)Owner).Text=value;}
 }
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
 static readonly List<string> checks=new List<string>();
 static readonly Dictionary<string,long> latency=new Dictionary<string,long>();
 static FieldInfo Field(string name){return typeof(Desktop).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);}
 static object Call(Desktop desktop,string name,params object[] args){return typeof(Desktop).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(desktop,args);}
 static void Pump(int milliseconds){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(2);}}
 static void Require(bool condition,string label){if(!condition)throw new Exception(label);checks.Add(label);}
 static void Wait(Func<bool> condition,string label,int milliseconds=1500){var clock=Stopwatch.StartNew();while(!condition()&&clock.ElapsedMilliseconds<milliseconds)Pump(2);if(!condition())throw new Exception("Timed out: "+label);}
 static IntPtr Handle(Window window){return new WindowInteropHelper(window).Handle;}
 static void Prepare(Desktop desktop,Window window){window.Show();window.Activate();var h=Handle(window);Wait(()=>GetForegroundWindow()==h,"fixture focus");Field("owner").SetValue(desktop,h);Pump(400);desktop.Show();Pump(20);Require(desktop.IsVisible,"overlay visible over its own active fixture");Require((bool)Call(desktop,"OwnerActive",h),"active fixture accepted");}
 static void HideCheck(Desktop desktop,string reason,Action operation){int stamp=((LayoutGate)Field("layoutGate").GetValue(desktop)).Generation;var clock=Stopwatch.StartNew();operation();Wait(()=>!desktop.IsVisible,reason,300);latency[reason]=clock.ElapsedMilliseconds;Require(clock.ElapsedMilliseconds<=150,reason+" hidden within 150 ms");Require((bool)Field("scanning").GetValue(desktop),reason+" does not wait for discovery");Require(((LayoutGate)Field("layoutGate").GetValue(desktop)).Generation>stamp,reason+" invalidates late placement");}
 [STAThread] static int Main(string[] args){
  Desktop desktop=null;Window first=null,other=null,delayed=null;Dispatcher delayedDispatcher=null;Thread delayedThread=null;
  try {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   // The harness pumps frames instead of Application.Run; explicitly install
   // the same UI context so async production callbacks resume on this thread.
   SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
   desktop=new Desktop(new Settings{codexPath=args[1],timeoutSeconds=3});Field("scanning").SetValue(desktop,true);desktop.Show();Pump(50);
   first=new Window{Title="AskBetter lifecycle fixture",Width=360,Height=220,Left=80,Top=80};
   other=new Window{Title="AskBetter unrelated fixture",Width=340,Height=200,Left=480,Top=80};
   Prepare(desktop,first);
   HideCheck(desktop,"minimize",()=>first.WindowState=WindowState.Minimized);
   first.WindowState=WindowState.Normal;Prepare(desktop,first);
   HideCheck(desktop,"tray-style hide",()=>first.Hide());
   Prepare(desktop,first);
   HideCheck(desktop,"foreground switch",()=>{other.Show();other.Activate();});
   Require(!(bool)Call(desktop,"OwnerActive",Handle(first)),"background owner rejected for draft replacement");
   // A separate UI thread emulates Codex's slow provider without blocking the
   // helper/test dispatcher. The real Run/Replace path must preserve its draft.
   DelayedTextBox delayedInput=null;IntPtr delayedHandle=IntPtr.Zero;
   using(var ready=new ManualResetEvent(false)){
    delayedThread=new Thread(()=>{
     delayedDispatcher=Dispatcher.CurrentDispatcher;
     delayedInput=new DelayedTextBox{Text="Draft IMU / UDP"};AutomationProperties.SetAutomationId(delayedInput,"DelayedEditor");
     delayed=new Window{Title="AskBetter slow-provider fixture",Content=delayedInput,Width=400,Height=220,Left=80,Top=360};delayed.Show();delayed.Activate();delayedHandle=Handle(delayed);ready.Set();Dispatcher.Run();
    });delayedThread.IsBackground=true;delayedThread.SetApartmentState(ApartmentState.STA);delayedThread.Start();Wait(()=>ready.WaitOne(0),"slow fixture created");
   }
   Wait(()=>GetForegroundWindow()==delayedHandle,"slow fixture receives focus");
   Field("owner").SetValue(desktop,delayedHandle);Pump(400);desktop.Show();Pump(20);
   var locate=Task.Run(()=>AutomationElement.FromHandle(delayedHandle).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"DelayedEditor")));
   Wait(()=>locate.IsCompleted,"slow editor accessible");var editor=locate.GetAwaiter().GetResult();Require(editor!=null,"native delayed editor recognized");
   Field("editor").SetValue(desktop,editor);var identify=Task.Run(()=>editor.GetRuntimeId());Wait(()=>identify.IsCompleted,"editor identity");Field("editorId").SetValue(desktop,identify.Result);Field("ownerTitle").SetValue(desktop,"AskBetter slow-provider fixture|");
   Task request=null;
   HideCheck(desktop,"delayed editor read",()=>{request=(Task)Call(desktop,"Run");other.Activate();});
   Wait(()=>request.IsCompleted,"background request resolves",7000);request.GetAwaiter().GetResult();
   Require(((TextBox)Field("panelOutput").GetValue(desktop)).Text=="优化：Draft IMU / UDP","optimized reply received before background-write rejection");
   Require(delayedDispatcher.Invoke(()=>delayedInput.Text)=="Draft IMU / UDP","optimized reply cannot overwrite background fixture");
   Require(Field("undoEditor").GetValue(desktop)==null,"failed replacement does not record undo");
   delayedDispatcher.Invoke(()=>delayed.Activate());Wait(()=>GetForegroundWindow()==delayedHandle,"foreground replacement target");Pump(400);
   request=(Task)Call(desktop,"Run");Wait(()=>request.IsCompleted,"foreground value replacement",12000);request.GetAwaiter().GetResult();
   Require(delayedDispatcher.Invoke(()=>delayedInput.Text)=="优化：Draft IMU / UDP","real ValuePattern write succeeds from the worker");
   Require(Field("undoEditor").GetValue(desktop)==editor,"successful replacement records undo");
   delayedDispatcher.Invoke(()=>{delayedInput.Text="Write rejected draft";delayedInput.RejectWrites=true;});
   request=(Task)Call(desktop,"Run");Wait(()=>request.IsCompleted,"rejected provider write",12000);request.GetAwaiter().GetResult();
   Require(delayedDispatcher.Invoke(()=>delayedInput.Text)=="Write rejected draft","rejected value write preserves the draft");
   Require((string)Field("original").GetValue(desktop)=="Draft IMU / UDP"&&(string)Field("optimized").GetValue(desktop)=="优化：Draft IMU / UDP","rejected replacement preserves the previous successful undo");
   delayedDispatcher.Invoke(()=>delayed.Close());delayedDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);delayedThread.Join(2000);delayedDispatcher=null;
   Prepare(desktop,first);var closedOwner=Handle(first);
   HideCheck(desktop,"window close",()=>first.Close());
   Pump(500);
   Require(!IsWindow(closedOwner)&&!desktop.IsVisible,"closed fixture cannot leave an overlay on desktop");
   Require((IntPtr)Field("owner").GetValue(desktop)==IntPtr.Zero,"destroyed owner reference cleared");
   desktop.Close();Pump(50);
   Require((bool)Field("closing").GetValue(desktop),"shutdown blocks delayed continuation");
   Require(!((DispatcherTimer)Field("trackingTimer").GetValue(desktop)).IsEnabled,"discovery timer stopped on exit");
   Require(!((DispatcherTimer)Field("layoutTimer").GetValue(desktop)).IsEnabled,"visibility timer stopped on exit");
   foreach(var hook in new[]{"moveHook","locationHook","foregroundHook","lifetimeHook","minimizeHook","zoomKeyHook","zoomMouseHook"})Require((IntPtr)Field(hook).GetValue(desktop)==IntPtr.Zero,hook+" released");
   var report=new{checks=checks,hideLatencyMs=latency,discoveryWasBlocked=true,realCodex=false,fixture="Native disposable WPF windows"};
   File.WriteAllText(args[0],Backend.Json.Serialize(report));Console.WriteLine(checks.Count+" native lifecycle checks passed: "+Backend.Json.Serialize(latency));return 0;
  } catch(Exception error){Console.WriteLine(error);return 1;}
  finally {if(delayedDispatcher!=null&&!delayedDispatcher.HasShutdownStarted){delayedDispatcher.Invoke(()=>delayed.Close());delayedDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);}if(desktop!=null&&!((bool)Field("closing").GetValue(desktop)))desktop.Close();if(first!=null&&IsWindow(Handle(first)))first.Close();if(other!=null)other.Close();}
 }
}
