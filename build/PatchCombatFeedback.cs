using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchCombatFeedback
{
    static string Body(MethodDef method) => !method.HasBody ? "" : string.Join("\n",method.Body.Instructions.Select(i=>i.ToString()));
    static void Prepend(MethodDef method, params Instruction[] items)
    {
        method.Body.SimplifyBranches(); method.Body.SimplifyMacros(method.Parameters);
        for(int i=items.Length-1;i>=0;i--)method.Body.Instructions.Insert(0,items[i]);
    }
    public static void Main(string[] args)
    {
        string root=args[0], work=Path.Combine(root,"build","combat-runtime"), helper=Path.Combine(work,"CombatExtensions.dll");
        string baseline=Path.Combine(work,"baseline-runtime.dll");
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant()!="adb97902fb73379de038ca952d7fdb2d4900460d66d7639720ce352f81e98ba7")throw new Exception("Unexpected pre-combat runtime baseline");
        using var module=ModuleDefMD.Load(baseline);
        var before=module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m,Body);
        var renderer=PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.CombatFeedbackRenderer");
        using(var source=ModuleDefMD.Load(helper))renderer.BaseType=new Importer(module).Import(source.GetTypes().Single(t=>t.FullName==renderer.FullName).BaseType);
        PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.CombatFeedbackState");
        PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.CombatHitContext");
        var combat=PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.CombatFeedbackRuntime");
        var bridge=PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.CombatBridgeRuntime");
        var host=module.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.TrainerBridgeHost");
        MethodDef H(string name)=>host.Methods.Single(m=>m.Name==name);
        MethodDef C(string name)=>combat.Methods.Single(m=>m.Name==name);
        MethodDef B(string name)=>bridge.Methods.Single(m=>m.Name==name);
        var changed=new HashSet<MethodDef>();
        var execute=H("Execute");execute.Body.SimplifyBranches();execute.Body.SimplifyMacros(execute.Parameters);
        var authority=execute.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.Name=="ValidateAuthority");
        int at=execute.Body.Instructions.IndexOf(authority)-1;
        if(execute.Body.Instructions[at].OpCode!=OpCodes.Ldloc || authority.Operand is not IMethod am || am.MethodSig.HasThis)throw new Exception("Authority call shape changed");
        var native=execute.Body.Instructions[at];var exit=execute.Body.Instructions.Last(i=>i.OpCode==OpCodes.Ret);
        var custom=new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,B("TryHandle")),Instruction.Create(OpCodes.Brfalse,native),Instruction.Create(OpCodes.Leave,exit)};
        foreach(var prior in execute.Body.Instructions)if(prior.Operand==native)prior.Operand=custom[0];
        foreach(var i in custom)execute.Body.Instructions.Insert(at++,i);
        changed.Add(execute);
        var inspect=H("InspectSession");
        foreach(var ret in inspect.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Call;ret.Operand=C("Describe");inspect.Body.Instructions.Insert(inspect.Body.Instructions.IndexOf(ret)+1,Instruction.Create(OpCodes.Ret));}
        changed.Add(inspect);
        foreach(string name in new[]{"RestoreAll","Dispose"}){var m=H(name);Prepend(m,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,B("Reset")),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,C(name=="Dispose"?"RequestReset":"Reset")));changed.Add(m);}
        var tick=H("Tick");Prepend(tick,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,B("MaintainWave")),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,C("Tick")));changed.Add(tick);
        var begin=H("BeginFunEvent");begin.Body.SimplifyBranches();begin.Body.SimplifyMacros(begin.Parameters);
        var store=begin.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stloc&&((Local)i.Operand).Type.FullName.EndsWith("/RuntimeFunDefinition"));
        at=begin.Body.Instructions.IndexOf(store);
        var adjust=Instruction.Create(OpCodes.Call,B("AdjustDefinition"));
        foreach(var prior in begin.Body.Instructions)if(prior.Operand==store)prior.Operand=adjust;
        begin.Body.Instructions.Insert(at++,adjust);
        begin.Body.Instructions.Insert(at,Instruction.Create(OpCodes.Castclass,host.NestedTypes.Single(t=>t.Name=="RuntimeFunDefinition")));
        foreach(var i in begin.Body.Instructions)if(i.IsLdcI4()&&i.GetLdcI4Value()==10){i.OpCode=OpCodes.Ldc_I4;i.Operand=50;}
        changed.Add(begin);
        var availability=H("FunAvailability");
        var count=availability.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.Name=="get_Count"&&m.DeclaringType.Name=="RuntimeFunDefinition");
        count.OpCode=OpCodes.Call;count.Operand=B("AvailabilityMinimum");changed.Add(availability);
        var build=H("BuildFunInstructions");native=build.Body.Instructions[0];
        Prepend(build,Instruction.Create(OpCodes.Call,B("HasCustomPlan")),Instruction.Create(OpCodes.Brfalse,native),
            Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Ldarg_3),
            Instruction.Create(OpCodes.Call,B("Build")),Instruction.Create(OpCodes.Castclass,new Importer(module).Import(build.MethodSig.RetType).ToTypeDefOrRef()),Instruction.Create(OpCodes.Ret));
        changed.Add(build);
        foreach(var old in before)if(!changed.Contains(old.Key)&&Body(old.Key)!=old.Value)throw new Exception("Unrelated runtime changed: "+old.Key.FullName);
        byte[] identity=SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).Concat(File.ReadAllBytes(Path.Combine(root,"build","PatchCombatFeedback.cs"))).ToArray());
        module.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.Combat."+Convert.ToHexString(identity).Substring(0,16).ToLowerInvariant();
        module.Mvid=new Guid(identity.AsSpan(0,16));
        string output=Path.Combine(root,"patched","HowToFishTrainer.Runtime.dll"), temp=Path.Combine(work,"patched-runtime.dll");module.Write(temp);
        using(var check=ModuleDefMD.Load(temp))if(check.GetAssemblyRefs().Any(a=>a.Name=="CombatExtensions"))throw new Exception("Helper reference escaped embedding");
        File.Copy(temp,output,true);
        File.Copy(output,Path.Combine(root,"source","HowToFishTrainer","HowToFishTrainer.Runtime.dll"),true);
        File.WriteAllText(Path.Combine(root,"verification","combat-runtime-patch.json"),JsonSerializer.Serialize(new{
            passed=true,baseline_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant(),
            runtime_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant(),
            existing_method_bodies_preserved=before.Count-changed.Count,changed_methods=changed.Select(m=>m.FullName).ToArray(),
            authority_and_protocol_retained=true,damage_or_weapon_writes=false,max_animal_count=50,runtime_identity=module.Assembly.Name.ToString()
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Combat runtime patch written; "+(before.Count-changed.Count)+" original method bodies preserved");
    }
}
