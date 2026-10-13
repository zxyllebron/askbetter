using System;
using AskBetter;
class BackendTests {
 static int Main(string[] args){try{return Check(args);}catch(Exception ex){Console.WriteLine(ex.GetType().FullName+": "+ex.Message);return 1;}}
 static int Check(string[] args){int passed=0;var s=new Settings{codexPath=args[0],timeoutSeconds=2};
 using(var b=new Backend(s)){
  b.Trace=Console.WriteLine;
  string[] examples={"保留中文和2026年10月12日","quote \" and newline\n路径 C:\\Example\\project","第二次请求，不带上次内容"};
  foreach(var x in examples){Console.WriteLine("Case: "+x);if(b.Optimize(x).GetAwaiter().GetResult()!="优化："+x)throw new Exception("Unicode/isolation/final answer mismatch");passed++;}
  foreach(var x in new[]{"failed","empty","timeout"}){Console.WriteLine("Case: "+x);try{b.Optimize(x).GetAwaiter().GetResult();throw new Exception("Expected rejection: "+x);}catch(Exception e){if(e.Message.StartsWith("Expected rejection"))throw;}passed++;}
  if(b.Optimize("超时后重新连接").GetAwaiter().GetResult()!="优化：超时后重新连接")throw new Exception("Reconnect failed");passed++;
 }
 Console.WriteLine(passed+" backend integration checks passed.");return 0;}
}
