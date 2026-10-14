using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AskBetter {
public sealed partial class Desktop {
 [StructLayout(LayoutKind.Sequential)] struct WindowRect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
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
 IntPtr moveHook,locationHook,foregroundHook,lifetimeHook,minimizeHook,zoomKeyHook,zoomMouseHook,observedOwner,movingWindow;
 bool ownerSuppressed;
 InputHookCallback zoomKeyCallback,zoomMouseCallback;
 WindowRect previousOwnerRect;
 DispatcherTimer layoutTimer;
 long LayoutNow(){return layoutClock.ElapsedMilliseconds;}
 void LayoutChanged(){layoutGate.Changed(LayoutNow());Hide();}
 void ResetLayoutTracking(){movingWindow=IntPtr.Zero;layoutGate.End(LayoutNow());cachedModel=null;lastAnchor=Rect.Empty;lastDiscovery=DateTime.MinValue;observedOwner=IntPtr.Zero;Hide();}
 void InvalidateComposer(){cachedModel=null;lastDiscovery=DateTime.MinValue;}
 public static bool IsZoomShortcut(int key,bool control,bool alt){return control&&!alt&&(key==0xBB||key==0xBD||key==0x6B||key==0x6D||key==0x30||key==0x60);}
 bool ZoomTargetActive(){return settings.uiMode=="initial"&&!modelPickerOpen&&owner!=IntPtr.Zero&&GetForegroundWindow()==owner;}
 bool OwnerUsable(IntPtr window){int cloaked;return window!=IntPtr.Zero&&IsWindow(window)&&IsWindowVisible(window)&&!IsIconic(window)&&(DwmGetWindowAttribute(window,14,out cloaked,4)!=0||cloaked==0);}
 bool OwnerActive(IntPtr window){var foreground=GetForegroundWindow();return OwnerUsable(window)&&foreground!=IntPtr.Zero&&(foreground==window||(overlayHandle!=IntPtr.Zero&&foreground==overlayHandle));}
 void SuppressOwner(bool forget=false){if(!ownerSuppressed||IsVisible)ResetLayoutTracking();else Hide();ownerSuppressed=true;if(forget){owner=IntPtr.Zero;editor=null;editorId=null;ownerTitle=null;}}
 void StartLayoutMonitor(){
  layoutCallback=(hook,evt,window,objectId,childId,thread,time)=>{
   if(closing||window==IntPtr.Zero)return;
   if(evt>=0x8000&&(objectId!=0||childId!=0))return;
   Action change=()=>{
    if(closing)return;
    if(evt==0x3){if(!OwnerActive(owner))SuppressOwner(owner!=IntPtr.Zero&&!IsWindow(owner));if(window!=overlayHandle&&IsCodex(window)){if(window!=owner)ResetLayoutTracking();else InvalidateComposer();}return;}
    if(window==owner&&(evt==0x8001||evt==0x8003||evt==0x16)){SuppressOwner(evt==0x8001);return;}
    if(window==owner&&(evt==0x8002||evt==0x17)){ResetLayoutTracking();return;}
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
  lifetimeHook=SetWinEventHook(0x8001,0x8003,IntPtr.Zero,layoutCallback,0,0,0);
  minimizeHook=SetWinEventHook(0x16,0x17,IntPtr.Zero,layoutCallback,0,0,0);
  // Observe actual zoom inputs without consuming keys or clicks. Ctrl alone does nothing.
  zoomKeyCallback=(code,message,data)=>{if(code>=0&&message.ToInt64()==0x100&&ZoomTargetActive()&&IsZoomShortcut(Marshal.ReadInt32(data),GetAsyncKeyState(0x11)<0,GetAsyncKeyState(0x12)<0))LayoutChanged();return CallNextHookEx(IntPtr.Zero,code,message,data);};
  zoomMouseCallback=(code,message,data)=>{if(code>=0&&ZoomTargetActive()){if(message.ToInt64()==0x20A&&GetAsyncKeyState(0x11)<0&&GetAsyncKeyState(0x12)>=0)LayoutChanged();else if(message.ToInt64()==0x202)InvalidateComposer();}return CallNextHookEx(IntPtr.Zero,code,message,data);};
  zoomKeyHook=SetWindowsHookEx(13,zoomKeyCallback,GetModuleHandle(null),0);
  zoomMouseHook=SetWindowsHookEx(14,zoomMouseCallback,GetModuleHandle(null),0);
  layoutTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};
  layoutTimer.Tick+=(o,e)=>{
   if(closing)return;
   // This path never queries UI Automation or waits for the discovery worker.
   if(settings.uiMode!="initial"||modelPickerOpen){if(IsVisible)Hide();return;}
   if(!OwnerActive(owner)){SuppressOwner(owner!=IntPtr.Zero&&!IsWindow(owner));return;}
   ownerSuppressed=false;
   WindowRect rect;if(!GetWindowRect(owner,out rect)){SuppressOwner(true);return;}
   if(observedOwner!=owner){bool movingCurrent=movingWindow==owner;ResetLayoutTracking();observedOwner=owner;previousOwnerRect=rect;if(movingCurrent){movingWindow=owner;layoutGate.Begin(LayoutNow());}return;}
   if(rect.Left!=previousOwnerRect.Left||rect.Top!=previousOwnerRect.Top||rect.Right!=previousOwnerRect.Right||rect.Bottom!=previousOwnerRect.Bottom){previousOwnerRect=rect;LayoutChanged();}
  };
  layoutTimer.Start();
 }
 void StopLayoutMonitor(){if(layoutTimer!=null)layoutTimer.Stop();foreach(var hook in new[]{moveHook,locationHook,foregroundHook,lifetimeHook,minimizeHook})if(hook!=IntPtr.Zero)UnhookWinEvent(hook);if(zoomKeyHook!=IntPtr.Zero)UnhookWindowsHookEx(zoomKeyHook);if(zoomMouseHook!=IntPtr.Zero)UnhookWindowsHookEx(zoomMouseHook);moveHook=locationHook=foregroundHook=lifetimeHook=minimizeHook=zoomKeyHook=zoomMouseHook=IntPtr.Zero;}
}
}
