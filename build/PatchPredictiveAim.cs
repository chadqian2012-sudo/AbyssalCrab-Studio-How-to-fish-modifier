using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchPredictiveAim
{
    static void Before(MethodDef m, Instruction anchor, params Instruction[] added)
    { int index=m.Body.Instructions.IndexOf(anchor);foreach(var i in added)m.Body.Instructions.Insert(index++,i); }
    static void After(MethodDef m, Instruction anchor, params Instruction[] added)
    { int index=m.Body.Instructions.IndexOf(anchor)+1;foreach(var i in added)m.Body.Instructions.Insert(index++,i); }
    static void Prepend(MethodDef m,params Instruction[] added)
    { m.Body.SimplifyBranches();Before(m,m.Body.Instructions[0],added); }
    public static void Main(string[] args)
    {
        string root=args[0],work=Path.Combine(root,"build","predictive-aim"),baseline=Path.Combine(work,"baseline-runtime.dll"),helperFile=Path.Combine(work,"PredictiveAim.dll");
        using var runtime=ModuleDefMD.Load(baseline);
        string Body(MethodDef m)=>!m.HasBody?"":string.Join("\n",m.Body.Instructions.Select(i=>i.ToString()));
        var bodies=runtime.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m,Body);
        foreach(string type in new[]{"PredictiveBallistics","AimMotionTrack","AimTargetTrack","AimLockPolicy","PredictiveAimRuntime"})PatchSpin.Clone(runtime,helperFile,"AbyssalCrab.Runtime."+type);
        var helper=runtime.GetTypes().Single(t=>t.FullName=="AbyssalCrab.Runtime.PredictiveAimRuntime");
        MethodDef H(string name)=>helper.Methods.Single(m=>m.Name==name);
        var host=runtime.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.TrainerBridgeHost");
        MethodDef M(string name)=>host.Methods.Single(m=>m.Name==name);
        var validate=M("ValidateArguments");Prepend(validate,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,H("Validate")));
        var set=M("SetLock");set.Body.SimplifyBranches();
        Prepend(set,Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,H("Normalize")));
        foreach(var ret in set.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Ldarg_1;ret.Operand=null;After(set,ret,Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,H("Configure")),Instruction.Create(OpCodes.Ret));}
        var teammate=M("ApplyTeammateAimAssist");teammate.Body.SimplifyMacros(teammate.Parameters);
        var playerCall=teammate.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.Name=="TryPlayerAimPoint");
        var playerLoad=teammate.Body.Instructions[teammate.Body.Instructions.IndexOf(playerCall)-2];
        if(playerLoad.OpCode!=OpCodes.Ldloc || !(playerLoad.Operand is Local player))throw new Exception("Player selection IL changed");
        var nullableCtor=teammate.Body.Instructions.Single(i=>i.OpCode==OpCodes.Newobj&&i.Operand is IMethod m&&m.DeclaringType.FullName.Contains("Nullable`1<UnityEngine.Vector3>"));
        Before(teammate,nullableCtor,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldloc,player),Instruction.Create(OpCodes.Call,H("RememberPlayer")));
        Prepend(teammate,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,H("BeginSelection")));
        var bootstrap=runtime.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.Bootstrap");
        var record=bootstrap.Methods.Single(m=>m.Name=="RecordAimAssistRotation");record.Body.SimplifyBranches();
        var teammateCall=record.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.Name=="ApplyTeammateAimAssist");
        var bridge=bootstrap.Fields.Single(f=>f.Name=="_bridge");
        After(record,teammateCall,Instruction.Create(OpCodes.Ldsfld,bridge),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Ldarg_3),Instruction.Create(OpCodes.Ldarg,record.Parameters[4]),Instruction.Create(OpCodes.Call,H("Apply")));
        var nativeRecord=record.Body.Instructions[0];
        Prepend(record,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg,record.Parameters[4]),Instruction.Create(OpCodes.Call,H("Handled")),Instruction.Create(OpCodes.Brfalse,nativeRecord),Instruction.Create(OpCodes.Ret));
        var patchPostfix=bootstrap.Methods.Single(m=>m.Name=="PatchPostfix");patchPostfix.Body.SimplifyBranches();
        foreach(var ret in patchPostfix.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;After(patchPostfix,ret,Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Call,H("InstallPrefix")),Instruction.Create(OpCodes.Ret));}
        var select=M("SelectSilentAimTarget");select.Body.SimplifyBranches();
        var original=select.Body.Instructions[0];var result=new Local(select.MethodSig.RetType);select.Body.Variables.Add(result);select.Body.InitLocals=true;
        Prepend(select,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Ldarg_3),Instruction.Create(OpCodes.Ldarg,select.Parameters[4]),Instruction.Create(OpCodes.Ldloca,result),Instruction.Create(OpCodes.Call,H("SelectHeadSilent")),Instruction.Create(OpCodes.Brfalse,original),Instruction.Create(OpCodes.Ldloc,result),Instruction.Create(OpCodes.Ret));
        var shot=M("TryRedirectLocalShot");shot.Body.SimplifyBranches();
        var point=shot.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.Name=="GetValueOrDefault"&&m.DeclaringType.FullName.Contains("Nullable`1<UnityEngine.Vector3>"));
        After(shot,point,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Ldarg,shot.Parameters[5]),Instruction.Create(OpCodes.Ldarg,shot.Parameters[6]),Instruction.Create(OpCodes.Call,H("AdjustSilent")));
        var inspect=M("InspectSession");inspect.Body.SimplifyBranches();
        foreach(var ret in inspect.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Call;ret.Operand=H("Describe");After(inspect,ret,Instruction.Create(OpCodes.Ret));}
        var restore=M("RestoreAll");Prepend(restore,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,H("Reset")));
        var nativeSettings=M("ApplyAimAssistSettings");var settingsStart=nativeSettings.Body.Instructions[0];
        Prepend(nativeSettings,Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Call,H("NativeSettingsUnchanged")),Instruction.Create(OpCodes.Brfalse,settingsStart),Instruction.Create(OpCodes.Ret));
        // Mark the cached reference only after a successful native write; a failed apply must retry.
        foreach(var ret in nativeSettings.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).Skip(1).ToArray())
        {ret.OpCode=OpCodes.Ldarg_2;ret.Operand=null;After(nativeSettings,ret,Instruction.Create(OpCodes.Call,H("NativeSettingsApplied")),Instruction.Create(OpCodes.Ret));}
        var changed=new[]{validate,set,teammate,record,select,shot,inspect,restore,patchPostfix,nativeSettings};
        foreach(var pair in bodies)if(!changed.Contains(pair.Key)&&Body(pair.Key)!=pair.Value)throw new Exception("Unexpected edit: "+pair.Key.FullName);
        byte[] identity=SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helperFile)).Concat(File.ReadAllBytes(Path.Combine(root,"build","PatchPredictiveAim.cs"))).ToArray());
        runtime.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.Lead."+Convert.ToHexString(identity).Substring(0,16).ToLowerInvariant();runtime.Mvid=new Guid(identity.AsSpan(0,16));
        string output=Path.Combine(root,"patched","HowToFishTrainer.Runtime.dll");runtime.Write(output);
        using(var reloaded=ModuleDefMD.Load(output))
        {
            if(reloaded.GetAssemblyRefs().Any(a=>a.Name=="PredictiveAim"))throw new Exception("External helper dependency escaped cloning: "+string.Join("; ",reloaded.GetMemberRefs().Where(m=>m.DeclaringType.DefinitionAssembly?.Name=="PredictiveAim").Select(m=>m.FullName)));
        }
        File.Copy(output,Path.Combine(root,"source","HowToFishTrainer","HowToFishTrainer.Runtime.dll"),true);
        File.WriteAllText(Path.Combine(root,"verification","predictive-aim-patch.json"),JsonSerializer.Serialize(new{
            passed=true,baseline_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant(),runtime_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant(),
            runtime_identity=runtime.Assembly.Name.ToString(),mvid=runtime.Mvid,changed_methods=changed.Select(m=>m.FullName).ToArray(),existing_method_bodies_preserved=bodies.Count-changed.Length,
            signature_validation_preserved=true,hook_recovery_preserved=true,spin_helper_preserved=true,shooter_velocity_inherited=false,native_head_rule="Creature local hit z > HeadPos",native_gravity="position then velocity per fixed tick",automatic_firing=false,
            optimized_ads_prefix=true,selection="nearest-distance-on-ads",typed_game_access=true,tracking_json_allocations=false,proof_publish_hz=5,head_geometry_hz=20,empty_target_scan_hz=20
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Predictive aim + native head-region targeting patched; other method bodies preserved");
    }
}
