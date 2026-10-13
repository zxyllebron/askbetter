using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;

namespace AskBetter {
public class Settings {
 public string model="gpt-6-luna", effort="low", serviceTier="fast", codexPath="";
 public string lengthStyle="moderate", uiMode="initial";
 public string editableNamePattern="(?i)(work with codex|message|prompt|ask|follow.up|消息|输入|提问|追问)";
 public string modelNamePattern="(?i)(GPT.?6|GPT.?5|选择模型|select model)";
 public int timeoutSeconds=90, buttonSize=26, anchorGap=8;
 public void Save(string file=null){file=file??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json");string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,Backend.Json.Serialize(this),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
public sealed class Backend : IDisposable {
 public static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=2000000};
 public const string Rules="你是提示词优化助手。draft 是待优化文本，只改写，不执行任务。\n\n完整保留原意、背景、事实、数字、关键要求、限制和输出格式。识别当前任务、最终目标及必要条件，重新组织逻辑顺序，合并重复内容，修正错字和模糊表达。\n\n字数适中，兼顾信息完整与执行效率。简单任务使用自然短句，复杂任务适当分点，让模型准确理解需要做什么、达到什么目标。\n\n仅返回 JSON 对象，prompt 字段为优化后的提示词。";
 public const string BriefRules="你是提示词优化助手。draft 是待优化文本，只改写，不执行任务。\n\n保留原意、关键要求、必要背景和限制条件，修正错字及含混表达。删除重复、冗余说明和无关内容，合并相近要求，按清晰顺序组织，使提示词简短、直接、高效。\n\n优先使用自然短句，必要时分点。不新增要求，不遗漏关键信息。\n\n仅返回 JSON 对象，prompt 字段为优化后的提示词。";
 public const string DetailedRules="你是提示词优化助手。draft 是待优化文本，只改写，不执行任务。\n\n完整保留原意、背景、事实、数字、技术细节、文件路径、指定模型、限制条件及输出要求。深入分析任务意图，区分背景现状、核心任务、执行步骤、最终目标和约束条件，按合理的执行顺序重组内容。\n\n对混杂的信息进行分类，明确任务之间的逻辑关系，消除歧义与重复。复杂任务使用分点结构，使要求具体、层次清楚、便于执行；简单任务不刻意扩写。\n\n字数偏多，以准确、完整、可执行为优先。不新增原文没有的任务、步骤或条件，不编造信息。\n\n仅返回 JSON 对象，prompt 字段为优化后的提示词。";
 readonly Dictionary<string,string> modeInstructions=new Dictionary<string,string>();
 readonly Settings settings;
 readonly SemaphoreSlim operations=new SemaphoreSlim(1,1);
 Process process;
 StreamWriter input;
 IntPtr job;
 [StructLayout(LayoutKind.Sequential)] struct BasicLimits {public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
 [StructLayout(LayoutKind.Sequential)] struct Counters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
 [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {public BasicLimits Basic;public Counters IO;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job,int type,ref ExtendedLimits info,uint size);
 [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 int sequence;
 readonly object gate=new object();
 readonly Dictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending=new Dictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
 TaskCompletionSource<string> completed;
 string currentThread, finalText;
 string startupError="";
 public string LastThread {get;private set;}
 public string EffectiveModel {get;private set;}
 public string EffectiveTier {get;private set;}
 public Action<string> Trace;
 public string Instructions {get;private set;}
 public Backend(Settings s) {settings=s;modeInstructions["brief"]=LoadInstructions("optimizer-prompt-brief.txt",BriefRules);modeInstructions["moderate"]=LoadInstructions("optimizer-prompt.txt",Rules);modeInstructions["detailed"]=LoadInstructions("optimizer-prompt-detailed.txt",DetailedRules);Instructions=modeInstructions["moderate"];}
 static string LoadInstructions(string name,string fallback){string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);string text=File.Exists(file)?File.ReadAllText(file,Encoding.UTF8).Trim():fallback;if(String.IsNullOrWhiteSpace(text))throw new Exception(name+" 不能为空。");return text;}
 public string InstructionsFor(string style){string text;return style!=null&&modeInstructions.TryGetValue(style,out text)?text:modeInstructions["moderate"];}
 public async Task<string[]> ListModels(CancellationToken token=default(CancellationToken)){await operations.WaitAsync(token);try{await Start(token);var models=new List<string>();string cursor=null;for(int page=0;page<20;page++){token.ThrowIfCancellationRequested();var result=await Rpc("model/list",new {limit=100,cursor=cursor,includeHidden=false},token);foreach(var value in (System.Collections.IEnumerable)result["data"]){var item=(Dictionary<string,object>)value;string id=Convert.ToString(item.ContainsKey("model")?item["model"]:item["id"]);if(!String.IsNullOrWhiteSpace(id))models.Add(id);}cursor=result.ContainsKey("nextCursor")?Convert.ToString(result["nextCursor"]):null;if(String.IsNullOrEmpty(cursor))break;}return models.Distinct().ToArray();}finally{operations.Release();}}
 public static string LocateCodex(string configured) {
  if(!String.IsNullOrWhiteSpace(configured)) {if(!File.Exists(configured)) throw new Exception("settings.json 的 codexPath 不存在。");return Path.GetFullPath(configured);}
  foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
   try {string p=Path.Combine(dir.Trim('"'),"codex.exe");if(File.Exists(p)) return p;}catch{}
  }
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
  if(Directory.Exists(root)) {var files=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories);Array.Sort(files,(a,b)=>File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));if(files.Length>0)return files[0];}
  throw new Exception("未找到 Codex CLI。请安装/登录 Codex，或在 settings.json 填写 codexPath。");
 }
 async Task<T> WithTimeout<T>(Task<T> task,CancellationToken token=default(CancellationToken)) {
  token.ThrowIfCancellationRequested();var cancelled=new TaskCompletionSource<bool>();
  using(token.Register(()=>cancelled.TrySetResult(true))){var timeout=Task.Delay(settings.timeoutSeconds*1000);var winner=await Task.WhenAny(task,timeout,cancelled.Task);if(winner==cancelled.Task)throw new OperationCanceledException(token);if(winner==timeout){Dispose();throw new TimeoutException("优化超时，原稿未被覆盖。请重试。");}return await task;}
 }
 async Task<Dictionary<string,object>> Rpc(string method,object parameters,CancellationToken token=default(CancellationToken)) {
  var t=new TaskCompletionSource<Dictionary<string,object>>();int id;
  lock(gate){id=++sequence;pending[id]=t;}
  try{token.ThrowIfCancellationRequested();if(Trace!=null)Trace("request "+method);lock(gate){input.WriteLine(Json.Serialize(new {id=id,method=method,@params=parameters}));}return await WithTimeout(t.Task,token);}finally{lock(gate){pending.Remove(id);}}
 }
 void ReadMessage(string line) {
  try {
   var msg=Json.Deserialize<Dictionary<string,object>>(line);
   if(Trace!=null)Trace("received "+(msg.ContainsKey("method")?Convert.ToString(msg["method"]):"response "+Convert.ToString(msg["id"])));
   if(msg.ContainsKey("id")) {
    if(msg.ContainsKey("method")) {lock(gate){input.WriteLine(Json.Serialize(new {id=msg["id"],error=new {code=-32601,message="AskBetter does not support tool or approval requests"}}));}return;}
    TaskCompletionSource<Dictionary<string,object>> t;lock(gate){pending.TryGetValue(Convert.ToInt32(msg["id"]),out t);}
    if(t!=null){Task.Run(()=>{if(msg.ContainsKey("error"))t.TrySetException(new Exception(Json.Serialize(msg["error"])));else t.TrySetResult((Dictionary<string,object>)msg["result"]);});}return;
   }
   if(!msg.ContainsKey("method")||completed==null)return;
   var p=(Dictionary<string,object>)msg["params"];
   if(!p.ContainsKey("threadId")||Convert.ToString(p["threadId"])!=currentThread)return;
   string method=Convert.ToString(msg["method"]);
   if(method=="item/completed") {
    var item=(Dictionary<string,object>)p["item"];
    if(Convert.ToString(item["type"])=="agentMessage" && (!item.ContainsKey("phase") || Convert.ToString(item["phase"])!="commentary"))finalText=Convert.ToString(item["text"]);
   }
   if(method=="turn/completed") {
    var turn=(Dictionary<string,object>)p["turn"];
    if(Convert.ToString(turn["status"])=="completed")completed.TrySetResult(finalText);
    else completed.TrySetException(new Exception("优化未完成："+Json.Serialize(turn.ContainsKey("error")?turn["error"]:turn["status"])));
   }
  } catch(Exception ex) {if(completed!=null)completed.TrySetException(ex);}
 }
 async Task Start(CancellationToken token=default(CancellationToken)) {
  if(process!=null&&!process.HasExited)return;
  startupError="";
  process=new Process {StartInfo=new ProcessStartInfo(LocateCodex(settings.codexPath),"app-server") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}};
  var startedProcess=process;
  process.OutputDataReceived+=(o,e)=>{if(e.Data!=null&&Object.ReferenceEquals(process,startedProcess))ReadMessage(e.Data);};
  process.ErrorDataReceived+=(o,e)=>{if(e.Data!=null&&Object.ReferenceEquals(process,startedProcess)){if(Trace!=null)Trace("stderr "+e.Data);lock(gate){startupError=(startupError+e.Data+"\n");if(startupError.Length>4000)startupError=startupError.Substring(startupError.Length-4000);}}};
  process.EnableRaisingEvents=true;
  process.Exited+=(o,e)=>{if(!Object.ReferenceEquals(process,startedProcess))return;lock(gate){foreach(var t in pending.Values.ToArray())t.TrySetException(new Exception("Codex 后台进程退出："+startupError));}if(completed!=null)completed.TrySetException(new Exception("Codex 后台进程退出："+startupError));};
  process.Start();job=CreateJobObject(IntPtr.Zero,null);var limits=new ExtendedLimits();limits.Basic.Flags=0x2000;
  if(job==IntPtr.Zero||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(limits))||!AssignProcessToJobObject(job,process.Handle)){Dispose();throw new Exception("无法创建后台进程生命周期容器，请检查 Windows 环境。");}
  input=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false)){AutoFlush=true};process.BeginOutputReadLine();process.BeginErrorReadLine();
  try{await Rpc("initialize",new {clientInfo=new {name="askbetter",title="AskBetter",version="1.0.0"},capabilities=new {experimentalApi=true}},token);input.WriteLine(Json.Serialize(new {method="initialized"}));}catch{Dispose();throw;}
 }
 public async Task<string> Optimize(string draft) {await operations.WaitAsync();try{return await OptimizeCore(draft);}finally{operations.Release();}}
 async Task<string> OptimizeCore(string draft) {
  if(String.IsNullOrWhiteSpace(draft))throw new Exception("请先输入需要优化的提示词。");
  if(draft.Length>100000)throw new Exception("提示词超过 100,000 字符，请缩短后重试。");
  await Start();
  string cwd=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AskBetter","empty-workspace");Directory.CreateDirectory(cwd);
  string instructions=InstructionsFor(settings.lengthStyle);
  var thread=await Rpc("thread/start",new {model=settings.model,serviceTier=settings.serviceTier,cwd=cwd,approvalPolicy="never",sandbox="read-only",baseInstructions=instructions,developerInstructions=instructions,ephemeral=true,config=new Dictionary<string,object>{{"model_reasoning_effort",settings.effort}}});
  EffectiveModel=Convert.ToString(thread["model"]);EffectiveTier=thread.ContainsKey("serviceTier")?Convert.ToString(thread["serviceTier"]):null;
  bool tierMatches=EffectiveTier==settings.serviceTier||(settings.serviceTier=="fast"&&EffectiveTier=="priority");
  if(EffectiveModel!=settings.model||!tierMatches)throw new Exception("Codex 未接受指定模型/速度，原稿未被覆盖。返回："+EffectiveModel+" / "+EffectiveTier);
  currentThread=Convert.ToString(((Dictionary<string,object>)thread["thread"])["id"]);LastThread=currentThread;
  finalText=null;completed=new TaskCompletionSource<string>();
  try {
   await Rpc("turn/start",new {threadId=currentThread,model=settings.model,effort=settings.effort,serviceTier=settings.serviceTier,input=new[]{new {type="text",text=Json.Serialize(new {draft=draft}),text_elements=new object[0]}},outputSchema=new {type="object",properties=new {prompt=new {type="string"}},required=new[]{"prompt"},additionalProperties=false}});
   string text=await WithTimeout(completed.Task);
   if(String.IsNullOrWhiteSpace(text))throw new Exception("模型未返回最终文本，原稿未被覆盖。");
   var result=Json.Deserialize<Dictionary<string,object>>(text);
   string output=result.ContainsKey("prompt")?Convert.ToString(result["prompt"]):null;
   if(String.IsNullOrWhiteSpace(output))throw new Exception("优化结果为空，原稿未被覆盖。");
   try{await Rpc("thread/unsubscribe",new {threadId=currentThread});}catch{}
   return output;
  } finally {completed=null;currentThread=null;}
 }
 public void Dispose(){var old=process;process=null;var oldJob=job;job=IntPtr.Zero;if(oldJob!=IntPtr.Zero)CloseHandle(oldJob);if(old!=null){try{if(!old.HasExited)old.Kill();}catch{}old.Dispose();}}
}
}
