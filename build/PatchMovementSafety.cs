using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchMovementSafety
{
    const string Owner = "AbyssalCrab.Runtime.MovementSafetyRuntime";
    const string ParentHash = "8dfbd831c6434cbbfa1c338922e184875f0c48ec70d4e3a0d246a20c2dfa7327";
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static string Body(MethodDef method)
    {
        if (!method.HasBody) return "none";
        string Operand(object value) => value switch {
            null => "", Instruction i => "@" + method.Body.Instructions.IndexOf(i),
            Instruction[] a => string.Join(",", a.Select(i => method.Body.Instructions.IndexOf(i))),
            IMethod m => m.FullName, IField f => f.FullName, ITypeDefOrRef t => t.FullName,
            Local l => "local:" + l.Index, Parameter p => "arg:" + p.Index, _ => value.ToString()
        };
        return string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Name + " " + Operand(i.Operand))) + "\n" +
            string.Join(",", method.Body.Variables.Select(v => v.Type.FullName)) + "\n" +
            string.Join(";", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.CatchType?.FullName + ":" + Operand(h.TryStart) + ":" + Operand(h.TryEnd) + ":" + Operand(h.HandlerStart) + ":" + Operand(h.HandlerEnd) + ":" + Operand(h.FilterStart)));
    }
    public static void Main(string[] args)
    {
        string baseline=args[0], helper=args[1], output=args[2], gamePath=args[3];
        if(Hash(baseline)!=ParentHash) throw new Exception("Expected the retained r17c runtime");
        if(Hash(gamePath)!="e3ec0befacc5269efdecb279f0ae72155fcde3ebaeed07e361aa15e6f7fc84b1") throw new Exception("Game assembly changed; inspect the native contract before building");
        using var game=ModuleDefMD.Load(gamePath);
        foreach(var pair in new[]{("PlayerHolding","GetBobOffset","_bobOffset","UnityEngine.Vector3"),("PlayerToolMovement","Bob","_bobPos","UnityEngine.Vector2")})
        {
            var type=game.GetTypes().Single(t=>t.FullName==pair.Item1);
            var method=type.Methods.Single(m=>m.Name==pair.Item2);
            if(method.IsStatic || method.MethodSig.Params.Count!=0 || method.MethodSig.RetType.FullName!="System.Void" ||
               type.Fields.Single(f=>f.Name==pair.Item3).FieldType.FullName!=pair.Item4) throw new Exception("Unexpected native bob contract: "+pair.Item1);
        }
        using var module=ModuleDefMD.Load(baseline);
        var before=module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Body);
        var old=module.GetTypes().Single(t=>t.FullName==Owner);
        module.Types.Remove(old);
        var replacement=PatchSpin.Clone(module,helper,Owner);
        // Keep lifecycle and slope bodies from the shipped parent, avoiding compiler drift.
        foreach(var method in old.Methods.Where(m=>m.Name!="Install" && m.Name!="AfterRadarBob"))
        {
            var target=replacement.Methods.Single(m=>m.FullName==method.FullName);
            target.Body=method.Body;
            foreach(var instruction in target.Body.Instructions)
                if(instruction.Operand is Parameter parameter) instruction.Operand=target.Parameters[parameter.Index];
        }
        var methods=replacement.Methods.ToDictionary(m=>m.FullName);
        var fields=replacement.Fields.ToDictionary(f=>f.FullName);
        foreach(var method in module.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody))
            foreach(var instruction in method.Body.Instructions)
                if(instruction.Operand is IMethod m && m.DeclaringType.FullName==Owner) instruction.Operand=methods[m.FullName];
                else if(instruction.Operand is IField f && f.DeclaringType.FullName==Owner) instruction.Operand=fields[f.FullName];
        var identity=SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).ToArray());
        module.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.HeldMotion."+Convert.ToHexString(identity)[..16].ToLowerInvariant();
        module.Mvid=new Guid(identity.AsSpan(0,16));
        module.Write(output);
        using var final=ModuleDefMD.Load(output);
        var after=final.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Body);
        var changed=before.Keys.Intersect(after.Keys).Where(k=>before[k]!=after[k]).ToArray();
        var removed=before.Keys.Except(after.Keys).ToArray();
        var added=after.Keys.Except(before.Keys).ToArray();
        if(changed.Length!=1 || !changed[0].Contains(Owner+"::Install(")) throw new Exception("Unexpected method changes: "+string.Join(";",changed));
        if(removed.Length!=1 || !removed[0].Contains(Owner+"::AfterRadarBob(")) throw new Exception("Unexpected removed method");
        if(added.Length!=3 || added.Any(n=>!new[]{"EnhancedMovement","AfterHeldItemBob","AfterToolBob"}.Any(m=>n.Contains(Owner+"::"+m+"(")))) throw new Exception("Unexpected added method");
        if(final.GetAssemblyRefs().Any(a=>a.Name=="HeldMotionRuntime")) throw new Exception("External helper reference escaped");
        var embedded=final.GetTypes().Single(t=>t.FullName==Owner);
        foreach(var name in new[]{"AfterHeldItemBob","AfterToolBob"})
        {
            var handler=embedded.Methods.Single(m=>m.Name==name);
            if(handler.Body.Instructions.Any(i=>i.Operand is IMethod m && (m.Name=="set_linearVelocity" || m.Name=="set_position" || m.Name=="set_rotation"))) throw new Exception("Visual handler writes physical motion");
        }
        var report=new {passed=true,parentRuntimeSha256=ParentHash,runtimeSha256=Hash(output),gameAssemblySha256=Hash(gamePath),
            retainedMethods=before.Count-changed.Length-removed.Length,changedMethods=changed,removedMethods=removed,addedMethods=added,
            nativeContracts=2,gameInjected=false,gameAssetsWritten=false,experimentalRadarSlopeIncluded=true};
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!,"runtime-audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Runtime audit: {report.retainedMethods} methods retained, {changed.Length} replaced, {removed.Length} removed, {added.Length} added; {Hash(output)}");
    }
}
