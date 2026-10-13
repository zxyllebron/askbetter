using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class FakeCodex {
 static JavaScriptSerializer j=new JavaScriptSerializer();
 static Dictionary<string,object> threadSettings;
 static void Send(object x){Console.WriteLine(j.Serialize(x));}
 static int Main(){Console.SetIn(new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true),true));Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});string line;while((line=Console.ReadLine())!=null){var m=j.Deserialize<Dictionary<string,object>>(line);if(!m.ContainsKey("id"))continue;string method=(string)m["method"];var p=(Dictionary<string,object>)m["params"];
 if(method=="initialize")Send(new {id=m["id"],result=new {userAgent="test"}});
 else if(method=="model/list"){if(Environment.GetEnvironmentVariable("ASKBETTER_FAKE_HANG_MODELS")=="1")continue;Send(new {id=m["id"],result=new {data=new[]{new {id="luna",model="gpt-6-luna"},new {id="sol",model="gpt-6.1-sol"}},nextCursor=(string)null}});}
 else if(method=="thread/start"){threadSettings=p;Send(new {id=m["id"],result=new {thread=new {id="test-thread"},model=p["model"],serviceTier=p["serviceTier"]}});}
 else if(method=="turn/start"){
 Send(new {id=m["id"],result=new {turn=new {id="test-turn",status="inProgress"}}});
 var list=(System.Collections.IList)p["input"];var item=(Dictionary<string,object>)list[0];var input=j.Deserialize<Dictionary<string,object>>((string)item["text"]);string draft=(string)input["draft"];
 if(draft=="timeout")continue;
 Send(new {method="item/completed",@params=new {threadId="test-thread",item=new {type="agentMessage",phase="commentary",text="must not return this"}}});
 string reply=draft=="capture-settings"?j.Serialize(new {instructions=threadSettings["baseInstructions"],model=p["model"],effort=p["effort"],tier=p["serviceTier"]}):"优化："+draft;
 if(draft!="failed")Send(new {method="item/completed",@params=new {threadId="test-thread",item=new {type="agentMessage",phase="final_answer",text=j.Serialize(new {prompt=draft=="empty"?"":reply})}}});
 Send(new {method="turn/completed",@params=new {threadId="test-thread",turn=new {status=draft=="failed"?"failed":"completed",error=draft=="failed"?"fixture failure":null}}});
 }else if(method=="thread/unsubscribe")Send(new {id=m["id"],result=new {}});
 }return 0;}
}
