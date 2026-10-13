using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AskBetter {
public sealed partial class Desktop {
 readonly List<ContextMenu> optionMenus=new List<ContextMenu>();
 Button panelOptimize;
 bool modelPickerOpen,exiting;
 bool panelComposing;
 string lastNotice;
 static string LengthName(string style){return style=="brief"?"简短":style=="detailed"?"详细":"适中";}
 string ConfigurationCaption(){return settings.model+" · "+LengthName(settings.lengthStyle)+" · "+settings.effort+" · Fast";}
 ContextMenu CreateOptionsMenu(bool inPanel){
  var menu=new ContextMenu();
  foreach(string style in new[]{"brief","moderate","detailed"}){
   string selected=style;var item=new MenuItem{Header=LengthName(style),IsCheckable=true,Tag=style};
   item.Click+=(o,e)=>ChangeLength(selected);menu.Items.Add(item);
  }
  var mode=new MenuItem{Header=inPanel?"初始模式":"单独对话框模式"};
  mode.Click+=(o,e)=>{if(!busy){if(inPanel)ReturnToInitial();else ShowPanel();}};menu.Items.Add(mode);
  var model=new MenuItem{Header="更改模型"};model.Click+=(o,e)=>ShowModelPicker();menu.Items.Add(model);
  menu.Opened+=(o,e)=>RefreshOptions();optionMenus.Add(menu);RefreshOptions();return menu;
 }
 void RefreshOptions(){foreach(var menu in optionMenus){foreach(MenuItem item in menu.Items){item.IsEnabled=!busy&&!modelPickerOpen;if(item.Tag is string)item.IsChecked=(string)item.Tag==settings.lengthStyle;}}button.ToolTip="优化提示词 · "+ConfigurationCaption()+"\n右键：长度、模式、更改模型"+(String.IsNullOrEmpty(lastNotice)?"":"\n"+lastNotice);if(panelOptimize!=null)panelOptimize.Content="优化 · "+LengthName(settings.lengthStyle);}
 void ChangeLength(string style){if(busy||modelPickerOpen)return;string old=settings.lengthStyle;try{settings.lengthStyle=style;settings.Save();RefreshOptions();if(panelStatus!=null)panelStatus.Text="已选择 "+ConfigurationCaption();}catch(Exception ex){settings.lengthStyle=old;RefreshOptions();Info("无法保存长度设置："+ex.Message);}}
 bool SaveMode(string mode){string old=settings.uiMode;try{settings.uiMode=mode;settings.Save();if(old!=mode)ResetLayoutTracking();return true;}catch(Exception ex){settings.uiMode=old;Info("无法保存界面模式："+ex.Message);return false;}}
 void ReturnToInitial(){if(busy||modelPickerOpen||!SaveMode("initial"))return;if(panel!=null)panel.Hide();if(owner!=IntPtr.Zero)SetForegroundWindow(owner);RefreshOptions();}
 void ExitAssistant(){exiting=true;Application.Current.Shutdown();}
 void EnsurePanel(){
  if(panel!=null)return;
  panel=new Window{Title="AskBetter · 单独对话框",Width=600,Height=480,MinWidth=430,MinHeight=340,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(25,25,25)),Foreground=Brushes.White};
  var menu=CreateOptionsMenu(true);panel.ContextMenu=menu;
  var grid=new Grid{Margin=new Thickness(18),ContextMenu=menu};
  grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());
  panelStatus=new TextBlock{Text=ConfigurationCaption(),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),ContextMenu=menu};grid.Children.Add(panelStatus);
  panelInput=Box();panelInput.ContextMenu=menu;Grid.SetRow(panelInput,1);grid.Children.Add(panelInput);
  panelInput.ToolTip="Enter 优化；Shift+Enter 换行";
  TextCompositionManager.AddPreviewTextInputStartHandler(panelInput,(o,e)=>panelComposing=true);
  TextCompositionManager.AddPreviewTextInputUpdateHandler(panelInput,(o,e)=>panelComposing=true);
  TextCompositionManager.AddPreviewTextInputHandler(panelInput,(o,e)=>panelComposing=false);
  panelInput.LostKeyboardFocus+=(o,e)=>panelComposing=false;
  panelInput.PreviewKeyDown+=async(o,e)=>{if(ConsumesPanelEnter(e.Key,Keyboard.Modifiers,panelComposing)){e.Handled=true;if(IsPanelOptimizeEnter(e.Key,Keyboard.Modifiers,panelComposing,e.IsRepeat))await RunPanel();}};
  var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,10),ContextMenu=menu};
  panelOptimize=new Button{Content="优化 · "+LengthName(settings.lengthStyle),Padding=new Thickness(12,6,12,6),ContextMenu=menu};panelOptimize.Click+=async(o,e)=>await RunPanel();controls.Children.Add(panelOptimize);
  var options=new Button{Content="设置",Margin=new Thickness(10,0,0,0),Padding=new Thickness(10,6,10,6),ContextMenu=menu};options.Click+=(o,e)=>{menu.PlacementTarget=options;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};controls.Children.Add(options);
  Grid.SetRow(controls,2);grid.Children.Add(controls);
  panelOutput=Box();panelOutput.IsReadOnly=true;panelOutput.ContextMenu=menu;Grid.SetRow(panelOutput,3);grid.Children.Add(panelOutput);
  panel.Content=grid;panel.Closing+=(o,e)=>{if(exiting)return;e.Cancel=true;ReturnToInitial();};RefreshOptions();
 }
 public static bool ConsumesPanelEnter(Key key,ModifierKeys modifiers,bool composing){return key==Key.Enter&&modifiers==ModifierKeys.None&&!composing;}
 public static bool IsPanelOptimizeEnter(Key key,ModifierKeys modifiers,bool composing,bool repeated){return ConsumesPanelEnter(key,modifiers,composing)&&!repeated;}
 static TextBox Box(){return new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=14,Padding=new Thickness(10),Background=new SolidColorBrush(Color.FromRgb(52,52,52)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(80,80,80))};}
 void ShowPanel(){if(busy||modelPickerOpen||!SaveMode("standalone"))return;EnsurePanel();Hide();panelStatus.Text=ConfigurationCaption();panel.Show();panel.Activate();RefreshOptions();}
 async Task RunPanel(){if(busy||modelPickerOpen)return;EnsurePanel();string draft=panelInput.Text;busy=true;panelOptimize.IsEnabled=false;RefreshOptions();panelStatus.Text="优化中 · "+ConfigurationCaption();try{var result=await backend.Optimize(draft);panelOutput.Text=result;panelStatus.Text=panelInput.Text==draft?"优化完成 · "+ConfigurationCaption():"优化完成，结果对应点击时的原稿；新输入已保留。";}catch(Exception ex){panelStatus.Text=ex.Message;}finally{busy=false;panelOptimize.IsEnabled=true;RefreshOptions();}}
 void ShowModelPicker(){
  if(busy||modelPickerOpen)return;modelPickerOpen=true;RefreshOptions();
  var picker=new Window{Title="更改优化模型",Width=460,Height=230,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Owner=panel!=null&&panel.IsVisible?panel:this};
  var layout=new StackPanel{Margin=new Thickness(20)};
  layout.Children.Add(new TextBlock{Text="模型（保持轻度推理 / Fast）",Margin=new Thickness(0,0,0,10)});
  var combo=new ComboBox{IsEditable=true,Text=settings.model,MinHeight=30};combo.Items.Add(settings.model);layout.Children.Add(combo);
  var status=new TextBlock{Text="正在获取本机 Codex 模型列表…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};layout.Children.Add(status);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
  var cancel=new Button{Content="取消",Padding=new Thickness(16,6,16,6),Margin=new Thickness(0,0,10,0)};cancel.Click+=(o,e)=>picker.Close();actions.Children.Add(cancel);
  var apply=new Button{Content="保存",Padding=new Thickness(16,6,16,6)};apply.Click+=(o,e)=>{
   string id=combo.Text.Trim();if(!Regex.IsMatch(id,"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$")){status.Text="请输入有效的模型标识。";return;}
   string old=settings.model;try{settings.model=id;settings.Save();if(panelStatus!=null)panelStatus.Text="已选择 "+ConfigurationCaption();picker.Close();}catch(Exception ex){settings.model=old;status.Text="保存失败："+ex.Message;}
  };actions.Children.Add(apply);layout.Children.Add(actions);picker.Content=layout;
  var cancellation=new CancellationTokenSource();Task loading=null;picker.Loaded+=(o,e)=>{loading=PopulateModels(picker,combo,status,cancellation.Token);};
  try{picker.ShowDialog();}finally{cancellation.Cancel();if(loading!=null)loading.ContinueWith(t=>cancellation.Dispose());else cancellation.Dispose();modelPickerOpen=false;RefreshOptions();}
 }
 async Task PopulateModels(Window picker,ComboBox combo,TextBlock status,CancellationToken token){try{var models=await backend.ListModels(token);if(!picker.IsVisible||token.IsCancellationRequested)return;string text=combo.Text;combo.Items.Clear();foreach(string id in models)combo.Items.Add(id);combo.Text=text;status.Text="选择或输入模型标识，保存后用于下一次优化。";}catch(OperationCanceledException){}catch(Exception ex){if(picker.IsVisible)status.Text="模型列表暂不可用，可手动输入。"+ex.Message;}}
}
}
