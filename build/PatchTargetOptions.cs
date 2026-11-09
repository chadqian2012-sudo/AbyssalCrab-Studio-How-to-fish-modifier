using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchTargetOptions
{
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static string Body(MethodDef method)
    {
        if (!method.HasBody) return "none";
        string Operand(object o) => o switch {
            null => "", Instruction i => "@" + method.Body.Instructions.IndexOf(i),
            Instruction[] a => string.Join(",", a.Select(i => method.Body.Instructions.IndexOf(i))),
            IMethod m => m.FullName, IField f => f.FullName, ITypeDefOrRef t => t.FullName,
            Local l => "local:" + l.Index, Parameter p => "arg:" + p.Index, _ => o.ToString()
        };
        return string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Name + " " + Operand(i.Operand))) + "\n" +
            string.Join(",", method.Body.Variables.Select(v => v.Type.FullName)) + "\n" +
            string.Join(";", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.CatchType?.FullName + ":" + Operand(h.TryStart) + ":" + Operand(h.TryEnd) + ":" + Operand(h.HandlerStart) + ":" + Operand(h.HandlerEnd) + ":" + Operand(h.FilterStart)));
    }
    public static void Main(string[] args)
    {
        string baseline = args[0], helper = args[1], output = args[2], game = args[3];
        const string parentHash = "36d5f71f02f9dd21ffcd37aaf7f0266732c6783c84b86802d6186ffc37db56ee";
        if (Hash(baseline) != parentHash) throw new Exception("Unexpected r23 Runtime");
        if (Hash(game) != "e3ec0befacc5269efdecb279f0ae72155fcde3ebaeed07e361aa15e6f7fc84b1") throw new Exception("Unexpected game build");
        using var module = ModuleDefMD.Load(baseline);
        using var source = ModuleDefMD.Load(helper);
        var before = module.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
        const string prefix = "AbyssalCrab.Runtime.";
        string[] newTypes = { "GullChallengeRound" };
        string[] replacements = { "PredictiveAimRuntime", "GameplayRuntime", "CombatFeedbackRuntime", "CombatBridgeRuntime", "MagicRangeRenderer" };
        foreach (string shortName in newTypes.Concat(replacements))
        {
            string name = prefix + shortName;
            var old = module.GetTypes().SingleOrDefault(t => t.FullName == name);
            var fields = old?.Fields.ToDictionary(f => f.Name.String, f => f.FieldSig.Type.FullName);
            if (old != null) module.Types.Remove(old);
            var added = PatchSpin.Clone(module, helper, name);
            var original = source.GetTypes().Single(t => t.FullName == name);
            added.BaseType = original.IsValueType ? module.CorLibTypes.GetTypeRef("System", "ValueType") : new Importer(module).Import(original.BaseType);
            if (fields != null) foreach (var field in fields)
                if (added.Fields.Single(f => f.Name == field.Key).FieldSig.Type.FullName != field.Value)
                    throw new Exception("Existing field contract changed: " + name + "." + field.Key);
        }
        var all = newTypes.Concat(replacements).Select(n => prefix + n).ToHashSet();
        var methods = module.GetTypes().Where(t => all.Contains(t.FullName)).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var fieldsMap = module.GetTypes().Where(t => all.Contains(t.FullName)).SelectMany(t => t.Fields).ToDictionary(f => f.FullName);
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is IField field && field.FieldSig != null && all.Contains(field.DeclaringType.FullName)) instruction.Operand = fieldsMap[field.FullName];
                else if (instruction.Operand is IMethod called && called.MethodSig != null && all.Contains(called.DeclaringType.FullName)) instruction.Operand = methods[called.FullName];
        var host = module.GetTypes().Single(t => t.FullName == "HowToFishTrainer.Runtime.TrainerBridgeHost");
        var receipt = host.Methods.Single(m => m.Name == "FunReceipt" && m.MethodSig.HasThis && m.MethodSig.Params.Count == 1
            && m.MethodSig.Params[0].TypeName == "TrackedFunWave" && m.MethodSig.RetType.FullName == "Newtonsoft.Json.Linq.JObject");
        var attach = module.GetTypes().Single(t => t.FullName == prefix + "CombatBridgeRuntime").Methods.Single(m => m.Name == "ChallengeReceipt");
        var ret = receipt.Body.Instructions.Single(i => i.OpCode == OpCodes.Ret);
        // Branches to the original return must also pass through the score receipt.
        ret.OpCode = OpCodes.Ldarg_0;
        int next = receipt.Body.Instructions.IndexOf(ret) + 1;
        receipt.Body.Instructions.Insert(next++, Instruction.Create(OpCodes.Ldarg_1));
        receipt.Body.Instructions.Insert(next++, Instruction.Create(OpCodes.Call, attach));
        receipt.Body.Instructions.Insert(next, Instruction.Create(OpCodes.Ret));
        var rollback = host.Methods.Single(m => m.Name == "RollbackFunWave" && m.MethodSig.HasThis && m.MethodSig.Params.Count == 1
            && m.MethodSig.Params[0].TypeName == "FunWaveJob");
        var waveId = host.NestedTypes.Single(t => t.Name == "FunWaveJob").Methods.Single(m => m.Name == "get_WaveId");
        var cancel = attach.DeclaringType.Methods.Single(m => m.Name == "ChallengeRollback");
        rollback.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
        rollback.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Ldarg_1));
        rollback.Body.Instructions.Insert(2, Instruction.Create(OpCodes.Callvirt, waveId));
        rollback.Body.Instructions.Insert(3, Instruction.Create(OpCodes.Call, cancel));
        var identity = SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).Concat(File.ReadAllBytes(args[4])).ToArray());
        module.Assembly.Name = "HowToFishTrainer.Runtime.V1_1_37.CombatChallenge." + Convert.ToHexString(identity)[..16].ToLowerInvariant();
        module.Mvid = new Guid(identity.AsSpan(0, 16));
        module.Write(output);
        using var final = ModuleDefMD.Load(output);
        var after = final.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
        var changed = new List<string>();
        foreach (var method in before)
        {
            if (!after.TryGetValue(method.Key, out string body)) throw new Exception("Existing method removed: " + method.Key);
            if (body != method.Value) changed.Add(method.Key);
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal) {
            "TrainerBridgeHost.FunReceipt", "TrainerBridgeHost.RollbackFunWave", "CombatBridgeRuntime.TryHandle", "CombatBridgeRuntime.Build",
            "CombatFeedbackRuntime.Configure", "CombatFeedbackRuntime.LocalPrefix", "CombatFeedbackRuntime.DamagePrefix",
            "CombatFeedbackRuntime.DamagePostfix", "CombatFeedbackRuntime.Tick", "CombatFeedbackRuntime.Describe", "CombatFeedbackRuntime.Reset",
            "MagicRangeRenderer.OnGUI", "MagicRangeRenderer.DrawEntities", "GameplayRuntime.RedirectShot",
            "GameplayRuntime.RegisterLaunch", "GameplayRuntime.Track", "GameplayRuntime.ClearProjectiles"
        };
        var originals = final.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        string ShortName(string signature) { var m = originals[signature]; return m.DeclaringType.Name + "." + m.Name; }
        var unreviewed = changed.Select(ShortName).Where(n => !allowed.Contains(n)).ToArray();
        if (unreviewed.Length > 0) throw new Exception("Unreviewed existing methods changed: " + string.Join(", ", unreviewed));
        var addedMethods = after.Keys.Except(before.Keys).ToArray();
        foreach (var name in addedMethods)
            if (!all.Contains(originals[name].DeclaringType.FullName)) throw new Exception("Unexpected added method: " + name);
        if (final.GetAssemblyRefs().Any(r => r.Name == "EntityOverlayRuntime")) throw new Exception("Helper DLL dependency leaked");
        if (final.GetTypes().Single(t => t.FullName == prefix + "MagicRangeRenderer").BaseType.FullName != "UnityEngine.MonoBehaviour" ||
            !final.GetTypes().Single(t => t.FullName == prefix + "EntityAimFrame").IsValueType) throw new Exception("Required base type was not preserved");
        foreach (var pair in new[] { ("GameplayTargeting", "Point"), ("PredictiveAimRuntime", "Alive") })
        {
            var gate = final.GetTypes().Single(t => t.FullName == prefix + pair.Item1).Methods.Single(m => m.Name == pair.Item2);
            if (!gate.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "_targetTeammates")) throw new Exception("Teammate targeting gate removed");
        }
        var report = new { passed = true, parentRuntimeSha256 = parentHash, runtimeSha256 = Hash(output), gameAssemblySha256 = Hash(game),
            retainedMethods = before.Count - changed.Count, changedMethods = changed, addedMethods, noHelperDependency = true, gameInjected = false, gameAssetsWritten = false };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "runtime-audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Combat challenge audited: {changed.Count} changed, {before.Count - changed.Count} retained, {addedMethods.Length} new methods; {Hash(output)}");
    }
}
