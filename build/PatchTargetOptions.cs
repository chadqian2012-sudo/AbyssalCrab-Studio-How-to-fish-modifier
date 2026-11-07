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
        if (Hash(baseline) != "f4ca19d5a62f6ecfe967d608fbb6255c200e40f6756b2eaba675e44e30669f53") throw new Exception("Unexpected r11 runtime");
        if (Hash(game) != "e3ec0befacc5269efdecb279f0ae72155fcde3ebaeed07e361aa15e6f7fc84b1") throw new Exception("Unexpected game build");
        using var module = ModuleDefMD.Load(baseline);
        var before = module.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
        string prefix = "AbyssalCrab.Runtime.";
        string[] replace = { prefix + "GameplayTargeting", prefix + "PredictiveAimRuntime", prefix + "GameplayRuntime" };
        string[] addedTypes = { prefix + "MagicRangePolicy", prefix + "MagicRangeRenderer" };
        foreach (string name in addedTypes)
        {
            var added = PatchSpin.Clone(module, helper, name);
            // Clone was designed for static helpers and defaults the base to Object.
            // The renderer must retain MonoBehaviour for AddComponent and Unity messages.
            if (name.EndsWith("MagicRangeRenderer"))
                added.BaseType = module.GetTypes().Single(t => t.FullName == prefix + "CombatFeedbackRenderer").BaseType;
        }
        foreach (string name in replace)
        {
            var old = module.GetTypes().Single(t => t.FullName == name);
            var fields = old.Fields.ToDictionary(f => f.Name.String, f => f.FieldSig.Type.FullName);
            module.Types.Remove(old);
            var added = PatchSpin.Clone(module, helper, name);
            foreach (var f in fields)
                if (added.Fields.Single(x => x.Name == f.Key).FieldSig.Type.FullName != f.Value) throw new Exception("Field changed: " + name + "." + f.Key);
        }
        var all = replace.Concat(addedTypes).ToHashSet();
        var methods = module.GetTypes().Where(t => all.Contains(t.FullName)).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var fieldsMap = module.GetTypes().Where(t => all.Contains(t.FullName)).SelectMany(t => t.Fields).ToDictionary(f => f.FullName);
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var i in method.Body.Instructions)
                if (i.Operand is IField f && f.FieldSig != null && all.Contains(f.DeclaringType.FullName)) i.Operand = fieldsMap[f.FullName];
                else if (i.Operand is IMethod m && m.MethodSig != null && all.Contains(m.DeclaringType.FullName)) i.Operand = methods[m.FullName];
        var identity = SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).Concat(File.ReadAllBytes(args[4])).ToArray());
        module.Assembly.Name = "HowToFishTrainer.Runtime.V1_1_37.TargetOptions." + Convert.ToHexString(identity)[..16].ToLowerInvariant();
        module.Mvid = new Guid(identity.AsSpan(0, 16));
        module.Write(output);
        using var final = ModuleDefMD.Load(output);
        var after = final.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
        var changed = new List<string>();
        foreach (var m in before)
        { if (!after.TryGetValue(m.Key, out var body)) throw new Exception("Method removed: " + m.Key); if (body != m.Value) changed.Add(m.Key); }
        string[] allowed = {
            prefix+"PredictiveAimRuntime::Validate(", prefix+"PredictiveAimRuntime::Configure(", prefix+"PredictiveAimRuntime::Reset(", prefix+"PredictiveAimRuntime::Alive(",
            prefix+"GameplayTargeting::Point(", prefix+"GameplayTargeting::Reset(",
            prefix+"GameplayRuntime::Validate(", prefix+"GameplayRuntime::Configured(", prefix+"GameplayRuntime::Restore(", prefix+"GameplayRuntime::Reset(" };
        if (changed.Count != allowed.Length || allowed.Any(a => !changed.Any(c => c.Contains(a)))) throw new Exception("Unexpected method changes: " + string.Join(";", changed));
        var addedMethods = after.Keys.Except(before.Keys).ToArray();
        if (addedMethods.Any(m => !addedTypes.Any(t => m.Contains(t + "::")) && !m.Contains(prefix+"GameplayTargeting::ConfigureTeammates("))) throw new Exception("Unexpected added methods");
        if (final.GetAssemblyRefs().Any(r => r.Name == "TargetOptionsRuntime")) throw new Exception("Helper dependency leaked");
        string rendererBase = final.GetTypes().Single(t => t.FullName == prefix + "MagicRangeRenderer").BaseType.FullName;
        if (rendererBase != "UnityEngine.MonoBehaviour") throw new Exception("Renderer is not a Unity component");
        foreach (var type in new[] { "PredictiveAimRuntime", "GameplayTargeting" })
        {
            var m = final.GetTypes().Single(t => t.FullName == prefix + type).Methods.Single(x => x.Name == (type == "GameplayTargeting" ? "Point" : "Alive"));
            if (!m.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "_targetTeammates") || m.Body.Instructions.Any(i => i.Operand is IMethod c && c.Name == "get_UseFriendlyFire"))
                throw new Exception("Teammate gate missing or tied to friendly fire: " + m.FullName);
        }
        var report = new { passed=true, baselineRuntimeSha256="b8d32abe9a31b36c5b1b0dac214daba1f6dfc8506b4f3ca09249d86aa3f32580", parentRuntimeSha256=Hash(baseline),
            runtimeSha256=Hash(output), gameAssemblySha256=Hash(game), retainedMethods=before.Count-changed.Count, changedMethods=changed, addedMethods,
            rendererBaseType=rendererBase, gameInjected=false, gameAssetsWritten=false, experimentalRadarSlopeIncluded=false };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "runtime-audit.json"), JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Audited: {changed.Count} changed, {before.Count-changed.Count} retained, {addedMethods.Length} added; {Hash(output)}");
    }
}
