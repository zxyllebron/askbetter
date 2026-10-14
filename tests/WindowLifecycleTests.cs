using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AskBetter;

// Only this test program's own disposable WPF windows are activated/closed.
// Freeze discovery to reproduce an unresponsive UI Automation provider.
class WindowLifecycleTests {
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
  Desktop desktop=null;Window first=null,other=null;
  try {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   desktop=new Desktop(new Settings());Field("scanning").SetValue(desktop,true);desktop.Show();Pump(50);
   first=new Window{Title="AskBetter lifecycle fixture",Width=360,Height=220,Left=80,Top=80};
   other=new Window{Title="AskBetter unrelated fixture",Width=340,Height=200,Left=480,Top=80};
   Prepare(desktop,first);
   HideCheck(desktop,"minimize",()=>first.WindowState=WindowState.Minimized);
   first.WindowState=WindowState.Normal;Prepare(desktop,first);
   HideCheck(desktop,"tray-style hide",()=>first.Hide());
   Prepare(desktop,first);
   HideCheck(desktop,"foreground switch",()=>{other.Show();other.Activate();});
   Require(!(bool)Call(desktop,"OwnerActive",Handle(first)),"background owner rejected for draft replacement");
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
  finally {if(desktop!=null&&!((bool)Field("closing").GetValue(desktop)))desktop.Close();if(first!=null&&IsWindow(Handle(first)))first.Close();if(other!=null)other.Close();}
 }
}
