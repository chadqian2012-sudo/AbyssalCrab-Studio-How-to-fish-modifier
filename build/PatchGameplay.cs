using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchGameplay
{
    static void Before(MethodDef m,Instruction anchor,params Instruction[] added) { int at=m.Body.Instructions.IndexOf(anchor);foreach(var i in added)m.Body.Instructions.Insert(at++,i); }
    static void After(MethodDef m,Instruction anchor,params Instruction[] added) { int at=m.Body.Instructions.IndexOf(anchor)+1;foreach(var i in added)m.Body.Instructions.Insert(at++,i); }
    static void Prepend(MethodDef m,params Instruction[] added) { m.Body.SimplifyBranches();Before(m,m.Body.Instructions[0],added); }
    static string Body(MethodDef m)
    {
        if(!m.HasBody)return "none";
        string Operand(object x)=>x switch { null=>"", Instruction i=>"@"+m.Body.Instructions.IndexOf(i), Instruction[] a=>string.Join(",",a.Select(i=>m.Body.Instructions.IndexOf(i))), IMethod a=>a.FullName,IField a=>a.FullName,ITypeDefOrRef a=>a.FullName,Local a=>"local:"+a.Index,Parameter a=>"arg:"+a.Index,_=>x.ToString() };
        return string.Join("\n",m.Body.Instructions.Select(i=>i.OpCode.Name+" "+Operand(i.Operand)))+"\n"+string.Join(",",m.Body.Variables.Select(v=>v.Type.FullName))+"\n"+string.Join(";",m.Body.ExceptionHandlers.Select(h=>h.HandlerType+":"+h.CatchType?.FullName+":"+Operand(h.TryStart)+":"+Operand(h.TryEnd)+":"+Operand(h.HandlerStart)+":"+Operand(h.HandlerEnd)+":"+Operand(h.FilterStart)));
    }
    static void Replace(ModuleDef module,string helper,string name)
    {
        var old=module.GetTypes().SingleOrDefault(t=>t.FullName==name);if(old!=null)module.Types.Remove(old);
        var added=PatchSpin.Clone(module,helper,name);var methods=added.Methods.ToDictionary(m=>m.FullName);
        foreach(var type in module.GetTypes())foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var i in method.Body.Instructions)
            if(i.Operand is IMethod call&&call.DeclaringType.FullName==name)i.Operand=methods[call.FullName];
    }
    static int Audit(string baseline,string staged,HashSet<string> changed,HashSet<string> replaced)
    {
        using var a=ModuleDefMD.Load(baseline);using var b=ModuleDefMD.Load(staged);var actual=b.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);int count=0;
        foreach(var m in a.GetTypes().Where(t=>!replaced.Contains(t.FullName)).SelectMany(t=>t.Methods))
        {if(changed.Contains(m.FullName))continue;if(!actual.TryGetValue(m.FullName,out var same)||Body(m)!=Body(same))throw new Exception("Unrelated method changed: "+m.FullName);count++;}
        if(b.GetAssemblyRefs().Any(r=>r.Name=="GameplayRuntime"||r.Name=="GameplayRegistry"))throw new Exception("Standalone helper dependency leaked");
        return count;
    }
    public static void Main(string[] args)
    {
        string root=args[0],work=Path.Combine(root,"build","gameplay-expansion");
        using var runtime=ModuleDefMD.Load(Path.Combine(work,"baseline-runtime.dll"));
        var changed=new HashSet<string>();var replaced=new HashSet<string>{"AbyssalCrab.Runtime.PredictiveAimRuntime"};
        string helper=Path.Combine(work,"GameplayRuntime.dll");
        foreach(string name in new[]{"GameplayPolicy","GameplayLaunch","GameplayFlight","GameplayExplosion","GameplayHook","GameplayHooks","GameplayTargeting","GameplayRuntime","PredictiveAimRuntime"})Replace(runtime,helper,"AbyssalCrab.Runtime."+name);
        var type=runtime.GetTypes().Single(t=>t.FullName=="AbyssalCrab.Runtime.GameplayRuntime");
        MethodDef H(string n)=>type.Methods.Single(m=>m.Name==n);
        var host=runtime.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.TrainerBridgeHost");
        MethodDef M(string n) {var m=host.Methods.Single(m=>m.Name==n);changed.Add(m.FullName);return m;}
        var spawn=M("ResolveSpawnableItem");
        var spawnCall=spawn.Body.Instructions.Single(i=>i.Operand is IMethod method&&method.DeclaringType.FullName==host.FullName&&method.Name=="Invoke");
        spawnCall.Operand=runtime.GetTypes().Single(t=>t.FullName=="AbyssalCrab.Runtime.GameplayTargeting").Methods.Single(m=>m.Name=="Spawnable");
        var validate=M("ValidateArguments");var native=validate.Body.Instructions[0];Prepend(validate,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,H("Validate")),Instruction.Create(OpCodes.Brfalse,native),Instruction.Create(OpCodes.Ret));
        var apply=M("ApplyLock");native=apply.Body.Instructions[0];Prepend(apply,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,H("ApplyExtra")),Instruction.Create(OpCodes.Brfalse,native),Instruction.Create(OpCodes.Ret));
        var configure=M("SetLock");configure.Body.SimplifyBranches();foreach(var ret in configure.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;After(configure,ret,Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,H("Configured")),Instruction.Create(OpCodes.Ret));}
        Prepend(configure,Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,H("EnsureBeforeLock")));
        var restore=M("RestoreFeature");Prepend(restore,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,H("Restore")));
        Prepend(M("RestoreAll"),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,H("Reset")));
        var tick=M("Tick");tick.Body.SimplifyBranches();foreach(var ret in tick.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;After(tick,ret,Instruction.Create(OpCodes.Call,H("Tick")),Instruction.Create(OpCodes.Ret));}
        var execute=M("Execute");var anchor=execute.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.DeclaringType.FullName=="AbyssalCrab.Runtime.CombatBridgeRuntime"&&m.Name=="TryHandle");
        int index=execute.Body.Instructions.IndexOf(anchor);var start=execute.Body.Instructions[index-2];
        Before(execute,start,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,H("TryHandle")),Instruction.Create(OpCodes.Brfalse,start),Instruction.Create(OpCodes.Ret));
        var shot=M("TryRedirectLocalShot");shot.Body=new CilBody();for(int n=0;n<shot.Parameters.Count;n++)shot.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg,shot.Parameters[n]));shot.Body.Instructions.Add(Instruction.Create(OpCodes.Call,H("Redirect")));shot.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Prepend(M("LoadIsland"),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,H("PrepareIsland")));
        var inspect=M("InspectSession");inspect.Body.SimplifyBranches();foreach(var ret in inspect.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Call;ret.Operand=H("Annotate");After(inspect,ret,Instruction.Create(OpCodes.Ret));}
        var bootstrap=runtime.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.Bootstrap");var hook=bootstrap.Methods.Single(m=>m.Name=="PatchPostfix");changed.Add(hook.FullName);hook.Body.SimplifyBranches();
        var weaponDamage=bootstrap.Methods.Single(m=>m.Name=="OverrideOwnedWeaponDamage");changed.Add(weaponDamage.FullName);
        weaponDamage.Body=new CilBody();weaponDamage.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld,bootstrap.Fields.Single(f=>f.Name=="_bridge")));weaponDamage.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));weaponDamage.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));weaponDamage.Body.Instructions.Add(Instruction.Create(OpCodes.Call,H("WeaponDamage")));weaponDamage.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        foreach(var ret in hook.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;After(hook,ret,Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,H("Install")),Instruction.Create(OpCodes.Ret));}
        var hash=SHA256.HashData(File.ReadAllBytes(Path.Combine(work,"baseline-runtime.dll")).Concat(File.ReadAllBytes(helper)).Concat(File.ReadAllBytes(Path.Combine(root,"build/PatchGameplay.cs"))).ToArray());
        runtime.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.Gameplay."+Convert.ToHexString(hash).Substring(0,16).ToLowerInvariant();runtime.Mvid=new Guid(hash.AsSpan(0,16));
        string stagedRuntime=Path.Combine(work,"staged-runtime.dll"),stagedCore=Path.Combine(work,"staged-core.dll");runtime.Write(stagedRuntime);
        using var core=ModuleDefMD.Load(Path.Combine(work,"baseline-core.dll"));string registryHelper=Path.Combine(work,"GameplayRegistry.dll");
        Replace(core,registryHelper,"AbyssalCrab.Runtime.GameplayPolicy");Replace(core,registryHelper,"AbyssalCrab.Runtime.GameplayRegistry");
        var registry=core.GetTypes().Single(t=>t.FullName=="AbyssalCrab.Runtime.GameplayRegistry");MethodDef R(string n)=>registry.Methods.Single(m=>m.Name==n);
        var coreChanged=new HashSet<string>();
        var getter=core.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Core.FeatureRegistry").Methods.Single(m=>m.Name=="get_All");coreChanged.Add(getter.FullName);Before(getter,getter.Body.Instructions.Last(),Instruction.Create(OpCodes.Call,R("Add")));
        var cv=core.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Core.FeatureArgumentValidator").Methods.Single(m=>m.Name=="IsValid");coreChanged.Add(cv.FullName);
        var getId=core.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Core.FeatureDescriptor").Methods.Single(m=>m.Name=="get_Id");native=cv.Body.Instructions[0];
        Prepend(cv,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Callvirt,getId),Instruction.Create(OpCodes.Call,R("Handles")),Instruction.Create(OpCodes.Brfalse,native),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,R("Validate")),Instruction.Create(OpCodes.Ret));
        var support=core.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Core.RuntimeFeatureSupport").Methods.Single(m=>m.Name==".cctor");coreChanged.Add(support.FullName);var store=support.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stsfld);var add=support.Body.Instructions.Select(i=>i.Operand).OfType<IMethod>().First(m=>m.Name=="Add");
        foreach(string id in new[]{"player.air_dash.lock","unlock.islands"})Before(support,store,Instruction.Create(OpCodes.Dup),Instruction.Create(OpCodes.Ldstr,id),Instruction.Create(OpCodes.Callvirt,add),Instruction.Create(OpCodes.Pop));
        core.Write(stagedCore);
        int retainedRuntime=Audit(Path.Combine(work,"baseline-runtime.dll"),stagedRuntime,changed,replaced),retainedCore=Audit(Path.Combine(work,"baseline-core.dll"),stagedCore,coreChanged,new HashSet<string>());
        string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        File.Copy(stagedRuntime,Path.Combine(root,"patched/HowToFishTrainer.Runtime.dll"),true);File.Copy(stagedRuntime,Path.Combine(root,"source/HowToFishTrainer/HowToFishTrainer.Runtime.dll"),true);File.Copy(stagedCore,Path.Combine(root,"patched/HowToFishTrainer.Core.dll"),true);
        File.WriteAllText(Path.Combine(work,"patch-audit.json"),JsonSerializer.Serialize(new {passed=true,runtimeSha256=Hash(stagedRuntime),coreSha256=Hash(stagedCore),baselineRuntimeSha256=Hash(Path.Combine(work,"baseline-runtime.dll")),baselineCoreSha256=Hash(Path.Combine(work,"baseline-core.dll")),retainedRuntime,retainedCore,changedMethods=changed,changedCoreMethods=coreChanged,replacedTypes=replaced,gameAssetsWritten=false,gameInjected=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Gameplay patch audited; retained {retainedRuntime} runtime / {retainedCore} Core methods");
    }
}
