using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Automation;

namespace AskBetter {
public sealed partial class Desktop : Window {
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,int flags);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 readonly Settings settings;readonly Backend backend;readonly Button button;readonly FrameworkElement wandIcon;
 AutomationElement editor;IntPtr owner;int[] editorId;string ownerTitle;
 bool busy, scanning, applying;string original,optimized,undoTitle;AutomationElement undoEditor;IntPtr undoOwner;int[] undoId;bool hotkeysReady=true;
 Window panel;TextBox panelInput,panelOutput;TextBlock panelStatus;
 AutomationElement cachedModel;
 DateTime lastDiscovery=DateTime.MinValue;
 Rect lastAnchor=Rect.Empty;
 IntPtr overlayHandle;
 volatile bool closing;
 DispatcherTimer trackingTimer;
 public Desktop(Settings s) {
  settings=s;backend=new Backend(s);
  Width=Height=s.buttonSize;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Title="AskBetter";
  wandIcon=CreateWandIcon();
  button=new Button {Content=wandIcon,FontSize=14,Foreground=new SolidColorBrush(Color.FromRgb(220,220,220)),Background=new SolidColorBrush(Color.FromRgb(48,48,48)),BorderBrush=new SolidColorBrush(Color.FromRgb(84,84,84)),BorderThickness=new Thickness(1),UseLayoutRounding=true,SnapsToDevicePixels=true,ToolTip="优化提示词 · GPT-6.1 Sol / Low / Fast\n右键：撤销、手动输入、退出"};
  AutomationProperties.SetName(button,"优化提示词");
  button.MouseEnter+=(o,e)=>button.Background=new SolidColorBrush(Color.FromRgb(65,65,65));
  button.MouseLeave+=(o,e)=>button.Background=new SolidColorBrush(Color.FromRgb(48,48,48));
  var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(s.buttonSize/2.0));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});border.SetValue(Border.BorderThicknessProperty,new Thickness(1));var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;button.Template=template;Content=button;
  button.Click+=async(o,e)=>await Run();
  if(!new[]{"brief","moderate","detailed"}.Contains(settings.lengthStyle))settings.lengthStyle="moderate";
  if(!new[]{"initial","standalone"}.Contains(settings.uiMode))settings.uiMode="initial";
  button.ContextMenu=CreateOptionsMenu(false);
  SourceInitialized+=(o,e)=>{var h=overlayHandle=new WindowInteropHelper(this).Handle;var src=HwndSource.FromHwnd(h);src.AddHook(Hook);bool a=RegisterHotKey(h,1,0x4003,0x42),c=RegisterHotKey(h,3,0x4003,0x5A),d=RegisterHotKey(h,4,0x4003,0x51);hotkeysReady=a&&c&&d;StartLayoutMonitor();};
  Closed+=(o,e)=>{closing=true;trackingTimer.Stop();StopLayoutMonitor();backend.Dispose();UnregisterHotKey(overlayHandle,1);UnregisterHotKey(overlayHandle,3);UnregisterHotKey(overlayHandle,4);};
  trackingTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};trackingTimer.Tick+=async(o,e)=>await Track();trackingTimer.Start();
  Loaded+=(o,e)=>{Hide();if(settings.uiMode=="standalone")ShowPanel();};
 }
 static void AddMenu(ContextMenu menu,string text,Action action){var item=new MenuItem {Header=text};item.Click+=(o,e)=>action();menu.Items.Add(item);}
 static FrameworkElement CreateWandIcon(){var canvas=new Canvas{Width=24,Height=24};var path=new System.Windows.Shapes.Path{Data=Geometry.Parse("M16.5,3.5 L20.5,7.5 L7.5,20.5 L3.5,16.5 Z M13.5,6.5 L17.5,10.5 M5,2 L5,6 M3,4 L7,4 M20,16 L20,20 M18,18 L22,18"),Stroke=new SolidColorBrush(Color.FromRgb(220,220,220)),StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};canvas.Children.Add(path);return new Viewbox{Width=16,Height=16,Stretch=Stretch.Uniform,Child=canvas};}
 static void Info(string text){MessageBox.Show(text,"AskBetter",MessageBoxButton.OK,MessageBoxImage.Information);}
 IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x312){handled=true;if(w.ToInt32()==1){BindFocused(true);}if(w.ToInt32()==3)Undo();if(w.ToInt32()==4)ExitAssistant();}return IntPtr.Zero;}
 bool IsCodex(IntPtr h){try{uint pid;GetWindowThreadProcessId(h,out pid);string name=Process.GetProcessById((int)pid).ProcessName;return Regex.IsMatch(name,"^(Codex|ChatGPT)$",RegexOptions.IgnoreCase);}catch{return false;}}
 static string Read(AutomationElement e){object pattern;if(e.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))return ((ValuePattern)pattern).Current.Value;if(e.TryGetCurrentPattern(TextPattern.Pattern,out pattern))return ((TextPattern)pattern).DocumentRange.GetText(-1).TrimEnd('\r','\n');throw new Exception("这个输入框未提供可读文本接口。");}
 bool Editable(AutomationElement e,bool explicitBind){try{var c=e.Current;if(c.IsOffscreen||!c.IsEnabled||!c.IsKeyboardFocusable)return false;object p;if(e.TryGetCurrentPattern(ValuePattern.Pattern,out p))return !((ValuePattern)p).Current.IsReadOnly&&(explicitBind||c.ControlType==ControlType.Edit||Regex.IsMatch(c.Name??"",settings.editableNamePattern));return c.ControlType==ControlType.Edit||(explicitBind&&c.ControlType==ControlType.Document&&e.TryGetCurrentPattern(TextPattern.Pattern,out p));}catch{return false;}}
 static string ContextTitle(AutomationElement root){var doc=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"RootWebArea"));return root.Current.Name+"|"+(doc==null?"":doc.Current.Name);}
 public static bool ComposerNearModel(Rect input,Rect model){bool beside=model.Left>=input.Right&&model.Left-input.Right<=32&&model.Top<input.Bottom&&model.Bottom>input.Top;return !input.IsEmpty&&!model.IsEmpty&&input.Width>0&&input.Height>0&&model.Width>0&&model.Height>0&&model.Top>=input.Top-8&&model.Top<=input.Bottom+160&&((model.Left<input.Right&&model.Right>input.Left)||beside);}
 public static bool IsModelLabel(string name){return Regex.IsMatch(name??"","GPT[ .-]?[0-9]|(?:select|choose|change).*model|选择.*模型|切换.*模型|^(?:Auto|Automatic|Instant|Thinking|Pro|Fast|自动|快速|思考|ChatGPT|Codex)$",RegexOptions.IgnoreCase);}
 public static bool IsComposerLabel(string name){return Regex.IsMatch(name??"","^(?:Work with (?:Codex|ChatGPT)|Ask (?:Codex|ChatGPT|anything)|Message(?: (?:Codex|ChatGPT))?|Send a message|Type a message|输入(?:消息|提示词)|发送消息|向.*发送消息|询问.*|有什么可以帮忙的)[. …。]*$",RegexOptions.IgnoreCase);}
 public static bool ExcludedEditor(string name){return Regex.IsMatch(name??"","search|find|reply|response|answer|rename|title|搜索|查找|回复|回答|重命名|标题",RegexOptions.IgnoreCase);}
 public static Rect ComposerPlacement(Rect input,Rect model,Rect window,double size,double gap){if(!model.IsEmpty){if(model.Left>=input.Right&&model.Top<input.Bottom)return new Rect(model.Left-gap-size,input.Top-gap-size,size,size);return new Rect(model.Left-gap-size,model.Top+(model.Height-size)/2,size,size);}if(input.IsEmpty||window.IsEmpty||input.Width<size+2*gap)return Rect.Empty;double y=input.Bottom+gap+size+gap<=window.Bottom?input.Bottom+gap:input.Top-gap-size;return y>=window.Top&&y+size<=window.Bottom?new Rect(input.Right-gap-size,y,size,size):Rect.Empty;}
 AutomationElementCollection ModelControls(AutomationElement root){return root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ComboBox)));}
 bool ModelForEditor(AutomationElement model,Rect input){try{var c=model.Current;return !c.IsOffscreen&&(IsModelLabel(c.Name)||IsModelLabel(c.HelpText)||Regex.IsMatch(c.Name??"",settings.modelNamePattern))&&ComposerNearModel(input,c.BoundingRectangle);}catch{return false;}}
 AutomationElement FindComposer(AutomationElement root){AutomationElement ignored;return FindComposer(root,out ignored);}
 AutomationElement FindComposer(AutomationElement root,out AutomationElement model){
  var edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));var models=ModelControls(root);AutomationElement best=null,fallback=null;model=null;double bottom=Double.MinValue,distance=Double.MaxValue,fallbackBottom=Double.MinValue;bool bestSemantic=false;
  foreach(AutomationElement edit in edits){if(!Editable(edit,false)||ExcludedEditor(edit.Current.Name))continue;var rect=edit.Current.BoundingRectangle;if(rect.IsEmpty||!rect.IntersectsWith(root.Current.BoundingRectangle))continue;bool semantic=IsComposerLabel(edit.Current.Name)||IsComposerLabel(edit.Current.HelpText);
   if(semantic&&rect.Bottom>fallbackBottom&&!ComposerPlacement(rect,Rect.Empty,root.Current.BoundingRectangle,settings.buttonSize,settings.anchorGap).IsEmpty){fallback=edit;fallbackBottom=rect.Bottom;}
   foreach(AutomationElement candidate in models){if(!ModelForEditor(candidate,rect))continue;var anchor=candidate.Current.BoundingRectangle;double gap=Math.Abs(anchor.Top-rect.Bottom);if(best==null||(semantic&&!bestSemantic)||(semantic==bestSemantic&&(anchor.Bottom>bottom||(anchor.Bottom==bottom&&gap<distance)))){bottom=anchor.Bottom;distance=gap;best=edit;model=candidate;bestSemantic=semantic;}}
  }
  if(!bestSemantic&&fallback!=null){model=null;return fallback;}return best;
 }
 void BindFocused(bool explicitBind){try{IntPtr h=GetForegroundWindow();if(!IsCodex(h)){if(explicitBind)Info("请先在 Codex 窗口中聚焦提示词输入框。");return;}var root=AutomationElement.FromHandle(h);var e=AutomationElement.FocusedElement;if(!Editable(e,explicitBind))e=FindComposer(root);if(e==null){if(explicitBind)Info("尚未识别提示词输入框。");return;}editor=e;owner=h;editorId=e.GetRuntimeId();ownerTitle=ContextTitle(root);ResetLayoutTracking();}catch(Exception ex){if(explicitBind)Info(ex.Message);}}
 async Task Track(){if(closing)return;if(settings.uiMode=="standalone"){Hide();return;}var active=GetForegroundWindow();if((active==overlayHandle&&!OwnerUsable(owner))||(active!=overlayHandle&&!IsCodex(active))){SuppressOwner(owner!=IntPtr.Zero&&!IsWindow(owner));return;}if(!layoutGate.Settled(LayoutNow())){Hide();return;}if(scanning||applying||modelPickerOpen)return;scanning=true;int generation=layoutGate.Generation;var selfHandle=overlayHandle;try{
  bool rebound=false;IntPtr trackedOwner=IntPtr.Zero;var tracked=await Task.Run(()=>{
   var h=GetForegroundWindow();bool stale=editor==null||!Editable(editor,true);if(!stale&&cachedModel!=null)stale=!ModelForEditor(cachedModel,editor.Current.BoundingRectangle);
   if(IsCodex(h)&&(h!=owner||stale||(DateTime.UtcNow-lastDiscovery).TotalSeconds>=2)){var windowRoot=AutomationElement.FromHandle(h);AutomationElement model;var composer=FindComposer(windowRoot,out model);if(composer==null){editor=null;cachedModel=null;lastDiscovery=DateTime.MinValue;return (Rect?)null;}rebound=h!=owner||editor==null||!Same(composer,editorId);editor=composer;owner=h;editorId=composer.GetRuntimeId();ownerTitle=ContextTitle(windowRoot);cachedModel=model;lastDiscovery=DateTime.UtcNow;}
   if(editor==null||owner==IntPtr.Zero)return (Rect?)null;
   trackedOwner=owner;
   if(h!=owner && h!=selfHandle)return (Rect?)null;
   var root=AutomationElement.FromHandle(owner);var er=editor.Current.BoundingRectangle;
   if(er.IsEmpty||editor.Current.IsOffscreen)return (Rect?)null;
   Rect best=Rect.Empty;
   if(cachedModel!=null){if(ModelForEditor(cachedModel,er))best=cachedModel.Current.BoundingRectangle;else cachedModel=null;}
   if(best.IsEmpty){var nodes=ModelControls(root);double distance=Double.MaxValue;
    foreach(AutomationElement n in nodes){if(!ModelForEditor(n,er))continue;var r=n.Current.BoundingRectangle;double d=Math.Abs(r.Top-er.Bottom);if(d<distance){distance=d;best=r;cachedModel=n;}}
   }
   if(best.IsEmpty&&!IsComposerLabel(editor.Current.Name)&&!IsComposerLabel(editor.Current.HelpText))return (Rect?)null;
   double scale=GetDpiForWindow(owner)/96.0;double size=settings.buttonSize*scale;
   var placement=ComposerPlacement(er,best,root.Current.BoundingRectangle,size,settings.anchorGap*scale);return placement.IsEmpty?(Rect?)null:placement;
  });
  if(closing)return;if(settings.uiMode=="standalone"){Hide();return;}if(modelPickerOpen)return;
  if(trackedOwner!=owner||!OwnerActive(trackedOwner)){Hide();return;}
  if(rebound){ResetLayoutTracking();return;}
  if(generation!=layoutGate.Generation||!layoutGate.Settled(LayoutNow())){Hide();return;}
  if(tracked.HasValue){if(!lastAnchor.IsEmpty&&tracked.Value!=lastAnchor&&!layoutGate.Pending){lastAnchor=tracked.Value;LayoutChanged();return;}lastAnchor=tracked.Value;var src=PresentationSource.FromVisual(this);var transform=src==null?Matrix.Identity:src.CompositionTarget.TransformFromDevice;var pt=transform.Transform(tracked.Value.TopLeft);Left=pt.X;Top=pt.Y;if(!IsVisible)Show();layoutGate.Applied();}
  else Hide();
 }catch{if(!closing){editor=null;InvalidateComposer();Hide();}}finally{scanning=false;}}
 static bool Same(AutomationElement e,int[] id){try{return e.GetRuntimeId().SequenceEqual(id);}catch{return false;}}
 bool Unchanged(AutomationElement e,IntPtr h,int[] id,string expected,string title){try{return !closing&&OwnerActive(h)&&IsCodex(h)&&Same(e,id)&&!e.Current.IsOffscreen&&ContextTitle(AutomationElement.FromHandle(h))==title&&Read(e)==expected;}catch{return false;}}
 static void Chord(byte key){keybd_event(0x11,0,0,UIntPtr.Zero);keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);keybd_event(0x11,0,2,UIntPtr.Zero);}
 async Task Replace(AutomationElement e,IntPtr h,int[] id,string expected,string result,string title){
  // Provider RPCs must not block the dispatcher that removes the floating UI.
  bool written=await Task.Run(()=>{
   if(!Unchanged(e,h,id,expected,title))throw new Exception("输入框或草稿已变动，结果保留在面板中，未覆盖。");
   object p;if(e.TryGetCurrentPattern(ValuePattern.Pattern,out p)&&!((ValuePattern)p).Current.IsReadOnly){
    if(closing||!OwnerActive(h))throw new Exception("窗口已关闭或切换，未覆盖。");
    ((ValuePattern)p).SetValue(result);if(Read(e)!=result)throw new Exception("输入框未接受优化结果，请从面板复制。");return true;
   }
   return false;
  });
  if(written)return;
  // Contenteditable controls often expose TextPattern but not ValuePattern.
  // Never send Enter. Focus, verify again, paste, and restore the clipboard.
  await Task.Run(()=>{if(closing||!OwnerActive(h))throw new Exception("窗口已关闭或切换，未覆盖。");SetForegroundWindow(h);e.SetFocus();});await Task.Delay(120);
  bool focused=await Task.Run(()=>GetForegroundWindow()==h&&Same(AutomationElement.FocusedElement,id)&&Unchanged(e,h,id,expected,title));
  if(closing||GetForegroundWindow()!=h||!focused)throw new Exception("焦点或草稿发生变化，未覆盖。");
  var saved=Clipboard.GetDataObject();Clipboard.SetText(result);applying=true;
  try {Chord(0x41);Chord(0x56);await Task.Delay(180);if(await Task.Run(()=>Read(e))!=result)throw new Exception("无法确认输入框回填，请检查草稿；结果仍在面板中。");}
  finally {applying=false;try{if(Clipboard.ContainsText()&&Clipboard.GetText()==result){if(saved!=null)Clipboard.SetDataObject(saved,true);else Clipboard.Clear();}}catch{}}
 }
 async Task Run(){if(busy||modelPickerOpen)return;if(editor==null){BindFocused(false);if(editor==null){button.ToolTip="尚未识别输入框，请先点击 Codex 的提示词输入框。";return;}}var target=editor;var h=owner;var id=editorId;string title=ownerTitle;lastNotice=null;
  try {busy=true;RefreshOptions();button.Content="…";string draft=await Task.Run(()=>Read(target));if(closing)return;string result=await backend.Optimize(draft);if(closing)return;SetPanelResult(draft,result,"优化完成 · "+ConfigurationCaption());await Replace(target,h,id,draft,result,title);original=draft;optimized=result;undoEditor=target;undoOwner=h;undoId=id;undoTitle=title;lastNotice="优化完成；Ctrl+Alt+Z 撤销。";}
  catch(Exception ex){if(!closing){EnsurePanel();panelStatus.Text=ex.Message;lastNotice=ex.Message;}}
  finally{busy=false;if(!closing){button.Content=wandIcon;RefreshOptions();}}
 }
 async void Undo(){if(closing)return;if(busy||undoEditor==null){Info("暂无可撤销的回填。");return;}busy=true;try{await Replace(undoEditor,undoOwner,undoId,optimized,original,undoTitle);undoEditor=null;button.ToolTip="已恢复原提示词";}catch(Exception ex){if(!closing)Info(ex.Message);}finally{busy=false;}}
 void SetPanelResult(string input,string output,string status){EnsurePanel();if(String.IsNullOrEmpty(panelInput.Text))panelInput.Text=input;panelOutput.Text=output;panelStatus.Text=status;}
 [STAThread] public static int Main(string[] args){
  string root=AppDomain.CurrentDomain.BaseDirectory;
  try {string settingsFile=Path.Combine(root,"settings.json");if(!File.Exists(settingsFile)){string exampleFile=Path.Combine(root,"settings.example.json");var defaults=File.Exists(exampleFile)?Backend.Json.Deserialize<Settings>(File.ReadAllText(exampleFile,Encoding.UTF8)):new Settings();defaults.Save(settingsFile);}var settings=Backend.Json.Deserialize<Settings>(File.ReadAllText(settingsFile,Encoding.UTF8));
   if(args.Length==3&&args[0]=="--optimize-file") {using(var b=new Backend(settings)){var timer=Stopwatch.StartNew();try{string output=b.Optimize(File.ReadAllText(args[1],Encoding.UTF8)).GetAwaiter().GetResult();File.WriteAllText(args[2],Backend.Json.Serialize(new {ok=true,prompt=output,model=b.EffectiveModel,effort=settings.effort,serviceTier=b.EffectiveTier,elapsedMs=timer.ElapsedMilliseconds}),new UTF8Encoding(false));return 0;}catch(Exception ex){File.WriteAllText(args[2],Backend.Json.Serialize(new {ok=false,error=ex.Message}),new UTF8Encoding(false));return 1;}}}
   if(args.Contains("--standalone"))settings.uiMode="standalone";
   if(args.Contains("--initial"))settings.uiMode="initial";
   bool isNew;using(var mutex=new Mutex(true,"Local\\AskBetter.Desktop",out isNew)){if(!isNew){MessageBox.Show("AskBetter 已运行，请使用正在运行的圆钮或单独对话框。","AskBetter");return 0;}var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var desktop=new Desktop(settings);app.Run(desktop);return 0;}
  }catch(Exception ex){MessageBox.Show(ex.Message,"AskBetter 启动失败");return 1;}
 }
}
}
