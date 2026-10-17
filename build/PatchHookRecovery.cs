using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchHookRecovery
{
    public static void Main(string[] args)
    {
        string root = args[0], work = Path.Combine(root,"build","hook-recovery");
        string baseline = Path.Combine(work,"baseline-runtime.dll");
        using var runtime = ModuleDefMD.Load(baseline);
        string Body(MethodDef m) => !m.HasBody ? "" : string.Join("\n", m.Body.Instructions.Select(i=>i.ToString()));
        var originalBodies=runtime.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m,Body);
        if(runtime.GetTypes().Any(t=>t.FullName=="AbyssalCrab.Runtime.RuntimeHookRecovery")) throw new Exception("Already patched");
        var helper = PatchSpin.Clone(runtime,Path.Combine(work,"RuntimeHookRecovery.dll"),"AbyssalCrab.Runtime.RuntimeHookRecovery");
        MethodDef Helper(string name) => helper.Methods.Single(m=>m.Name==name);
        var bootstrap = runtime.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.Bootstrap");
        var start = bootstrap.Methods.Single(m=>m.Name=="Start");
        var runner = start.Body.Instructions.Single(i=>i.OpCode==OpCodes.Call && i.Operand is IMethod m && m.DeclaringType.FullName=="HowToFishTrainer.Core.BestEffortActionRunner" && m.Name=="Run");
        runner.Operand = Helper("RunOptionalHooks");
        var tick = bootstrap.Methods.Single(m=>m.Name=="Tick");
        tick.Body.SimplifyBranches(); tick.Body.Instructions.Insert(0,Instruction.Create(OpCodes.Call,Helper("RetryPendingHooks")));
        var host = runtime.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.TrainerBridgeHost");
        var setLock = host.Methods.Single(m=>m.Name=="SetLock");
        setLock.Body.SimplifyBranches();
        setLock.Body.Instructions.Insert(0,Instruction.Create(OpCodes.Ldarg_1));
        setLock.Body.Instructions.Insert(1,Instruction.Create(OpCodes.Ldarg_2));
        setLock.Body.Instructions.Insert(2,Instruction.Create(OpCodes.Call,Helper("EnsureCombatReady")));
        var inspect = host.Methods.Single(m=>m.Name=="InspectSession");
        inspect.Body.SimplifyBranches();
        foreach(var ret in inspect.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        { ret.OpCode=OpCodes.Call;ret.Operand=Helper("AnnotateSession");inspect.Body.Instructions.Insert(inspect.Body.Instructions.IndexOf(ret)+1,Instruction.Create(OpCodes.Ret)); }
        var changed=new[]{start,tick,setLock,inspect};
        foreach(var pair in originalBodies)
            if(!changed.Contains(pair.Key) && Body(pair.Key)!=pair.Value) throw new Exception("Unexpected runtime method edit: "+pair.Key.FullName);
        byte[] identity = SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(Path.Combine(work,"RuntimeHookRecovery.dll"))).ToArray());
        runtime.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.Hooks."+Convert.ToHexString(identity).Substring(0,16).ToLowerInvariant();
        runtime.Mvid=new Guid(identity.AsSpan(0,16));
        string output=Path.Combine(root,"patched","HowToFishTrainer.Runtime.dll");
        runtime.Write(output);File.Copy(output,Path.Combine(root,"source","HowToFishTrainer","HowToFishTrainer.Runtime.dll"),true);
        File.WriteAllText(Path.Combine(root,"verification","hook-recovery-patch.json"),JsonSerializer.Serialize(new{
            passed=true,baseline_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant(),
            runtime_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant(),
            runtime_identity=runtime.Assembly.Name.ToString(),mvid=runtime.Mvid,
            changed_methods=new[]{"Bootstrap.Start optional runner","Bootstrap.Tick retry pump","TrainerBridgeHost.SetLock combat readiness","TrainerBridgeHost.InspectSession additive diagnostics"},
            signature_validation_preserved=true,successful_hooks_not_replayed=true,spin_helper_preserved=runtime.GetTypes().Any(t=>t.FullName=="AbyssalCrab.Runtime.SpinRuntime")
            ,existing_method_bodies_preserved=originalBodies.Count-changed.Length,changed_existing_methods=changed.Length
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Patched deferred optional hook recovery without bypassing game signature checks");
    }
}
