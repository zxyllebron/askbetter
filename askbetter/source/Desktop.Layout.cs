using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AskBetter {
public sealed partial class Desktop {
 [StructLayout(LayoutKind.Sequential)] struct WindowRect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 delegate void WinEventCallback(IntPtr hook,uint evt,IntPtr window,int objectId,int childId,uint thread,uint time);
 [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventCallback callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
 delegate IntPtr InputHookCallback(int code,IntPtr message,IntPtr data);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type,InputHookCallback callback,IntPtr module,uint thread);
 [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
 readonly LayoutGate layoutGate=new LayoutGate();
 readonly Stopwatch layoutClock=Stopwatch.StartNew();
 WinEventCallback layoutCallback;
 IntPtr moveHook,locationHook,foregroundHook,zoomKeyHook,zoomMouseHook,observedOwner,movingWindow;
 InputHookCallback zoomKeyCallback,zoomMouseCallback;
 WindowRect previousOwnerRect;
 DispatcherTimer layoutTimer;
 long LayoutNow(){return layoutClock.ElapsedMilliseconds;}
 void LayoutChanged(){layoutGate.Changed(LayoutNow());Hide();}
 void ResetLayoutTracking(){movingWindow=IntPtr.Zero;layoutGate.End(LayoutNow());cachedModel=null;lastAnchor=Rect.Empty;lastDiscovery=DateTime.MinValue;observedOwner=IntPtr.Zero;Hide();}
 void InvalidateComposer(){cachedModel=null;lastDiscovery=DateTime.MinValue;}
 public static bool IsZoomShortcut(int key,bool control,bool alt){return control&&!alt&&(key==0xBB||key==0xBD||key==0x6B||key==0x6D||key==0x30||key==0x60);}
 bool ZoomTargetActive(){return settings.uiMode=="initial"&&!modelPickerOpen&&owner!=IntPtr.Zero&&GetForegroundWindow()==owner;}
 void StartLayoutMonitor(){
  layoutCallback=(hook,evt,window,objectId,childId,thread,time)=>{
   if(window==IntPtr.Zero)return;
   if(evt==0x800B&&(objectId!=0||childId!=0))return;
   Action change=()=>{
    if(evt==0x3){if(IsCodex(window)){if(window!=owner)ResetLayoutTracking();else InvalidateComposer();}return;}
    // End belongs to the window that began moving, even after a mode/owner change.
    if(evt==0xB){if(window==movingWindow){movingWindow=IntPtr.Zero;layoutGate.End(LayoutNow());Hide();}return;}
    if(window!=owner||settings.uiMode!="initial")return;
    if(evt==0xA){movingWindow=window;layoutGate.Begin(LayoutNow());}else layoutGate.Changed(LayoutNow());Hide();
   };
   if(Dispatcher.CheckAccess())change();else Dispatcher.BeginInvoke(DispatcherPriority.Send,change);
  };
  moveHook=SetWinEventHook(0xA,0xB,IntPtr.Zero,layoutCallback,0,0,0);
  locationHook=SetWinEventHook(0x800B,0x800B,IntPtr.Zero,layoutCallback,0,0,0);
  foregroundHook=SetWinEventHook(0x3,0x3,IntPtr.Zero,layoutCallback,0,0,0);
  // Observe actual zoom inputs without consuming keys or clicks. Ctrl alone does nothing.
  zoomKeyCallback=(code,message,data)=>{if(code>=0&&message.ToInt64()==0x100&&ZoomTargetActive()&&IsZoomShortcut(Marshal.ReadInt32(data),GetAsyncKeyState(0x11)<0,GetAsyncKeyState(0x12)<0))LayoutChanged();return CallNextHookEx(IntPtr.Zero,code,message,data);};
  zoomMouseCallback=(code,message,data)=>{if(code>=0&&ZoomTargetActive()){if(message.ToInt64()==0x20A&&GetAsyncKeyState(0x11)<0&&GetAsyncKeyState(0x12)>=0)LayoutChanged();else if(message.ToInt64()==0x202)InvalidateComposer();}return CallNextHookEx(IntPtr.Zero,code,message,data);};
  zoomKeyHook=SetWindowsHookEx(13,zoomKeyCallback,GetModuleHandle(null),0);
  zoomMouseHook=SetWindowsHookEx(14,zoomMouseCallback,GetModuleHandle(null),0);
  layoutTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};
  layoutTimer.Tick+=(o,e)=>{
   if(settings.uiMode!="initial"||modelPickerOpen||owner==IntPtr.Zero)return;
   WindowRect rect;if(!GetWindowRect(owner,out rect))return;
   if(observedOwner!=owner){bool movingCurrent=movingWindow==owner;ResetLayoutTracking();observedOwner=owner;previousOwnerRect=rect;if(movingCurrent){movingWindow=owner;layoutGate.Begin(LayoutNow());}return;}
   if(rect.Left!=previousOwnerRect.Left||rect.Top!=previousOwnerRect.Top||rect.Right!=previousOwnerRect.Right||rect.Bottom!=previousOwnerRect.Bottom){previousOwnerRect=rect;LayoutChanged();}
  };
  layoutTimer.Start();
 }
 void StopLayoutMonitor(){if(layoutTimer!=null)layoutTimer.Stop();if(moveHook!=IntPtr.Zero)UnhookWinEvent(moveHook);if(locationHook!=IntPtr.Zero)UnhookWinEvent(locationHook);if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);if(zoomKeyHook!=IntPtr.Zero)UnhookWindowsHookEx(zoomKeyHook);if(zoomMouseHook!=IntPtr.Zero)UnhookWindowsHookEx(zoomMouseHook);moveHook=locationHook=foregroundHook=zoomKeyHook=zoomMouseHook=IntPtr.Zero;}
}
}
