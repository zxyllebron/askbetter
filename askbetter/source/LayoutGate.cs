namespace AskBetter {
public sealed class LayoutGate {
 public int Generation {get;private set;}
 public bool Pending {get;private set;}
 public bool Interacting {get;private set;}
 long lastChange;
 public const long QuietMilliseconds=300;
 public void Changed(long now){Pending=true;lastChange=now;Generation++;}
 public void Begin(long now){Interacting=true;Changed(now);}
 public void End(long now){Interacting=false;Changed(now);}
 public bool Settled(long now){return !Interacting&&(!Pending||now-lastChange>=QuietMilliseconds);}
 public void Applied(){Pending=false;}
}
}
