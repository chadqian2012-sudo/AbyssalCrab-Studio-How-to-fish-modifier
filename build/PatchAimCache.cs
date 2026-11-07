using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchAimCache
{
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
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static void Main(string[] args)
    {
        string baseline = args[0], helper = args[1], output = args[2], gamePath = args[3], patchSource = args[4];
        if (Hash(baseline) != "b8d32abe9a31b36c5b1b0dac214daba1f6dfc8506b4f3ca09249d86aa3f32580") throw new Exception("Unexpected baseline");
        using var game = ModuleDefMD.Load(gamePath);
        var nativePlayer = game.GetTypes().Single(t => t.FullName == "Player");
        var nativeOther = game.GetTypes().Single(t => t.FullName == "OtherPlayer");
        if (nativeOther.Fields.Single(f => f.Name == "_col").FieldSig.Type.FullName != "UnityEngine.CapsuleCollider") throw new Exception("Native capsule contract changed");
        if (nativePlayer.Properties.Single(p => p.Name == "Other").PropertySig.RetType.FullName != "OtherPlayer") throw new Exception("Native Other contract changed");
        if (nativePlayer.Properties.Single(p => p.Name == "Transform").PropertySig.RetType.FullName != "UnityEngine.Transform") throw new Exception("Native Transform contract changed");
        using var runtime = ModuleDefMD.Load(baseline);
        const string typeName = "AbyssalCrab.Runtime.PredictiveAimRuntime";
        const string targetingName = "AbyssalCrab.Runtime.GameplayTargeting";
        string[] replacedTypes = { typeName, targetingName };
        var originalMethods = runtime.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
        foreach (string name in replacedTypes)
        {
            var old = runtime.GetTypes().Single(t => t.FullName == name);
            var oldFields = old.Fields.ToDictionary(f => f.Name.String, f => f.FieldSig.Type.FullName);
            runtime.Types.Remove(old);
            var added = PatchSpin.Clone(runtime, helper, name);
            foreach (var field in oldFields)
                if (added.Fields.Single(f => f.Name == field.Key).FieldSig.Type.FullName != field.Value) throw new Exception("Existing field changed: " + name + "." + field.Key);
        }
        var replacements = runtime.GetTypes().Where(t => replacedTypes.Contains(t.FullName)).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var fieldReplacements = runtime.GetTypes().Where(t => replacedTypes.Contains(t.FullName)).SelectMany(t => t.Fields).ToDictionary(f => f.FullName);
        foreach (var method in runtime.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var i in method.Body.Instructions)
                if (i.Operand is IField field && field.FieldSig != null && replacedTypes.Contains(field.DeclaringType.FullName)) i.Operand = fieldReplacements[field.FullName];
                else if (i.Operand is IMethod call && call.MethodSig != null && replacedTypes.Contains(call.DeclaringType.FullName)) i.Operand = replacements[call.FullName];
        var identity = SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).Concat(File.ReadAllBytes(patchSource)).ToArray());
        runtime.Assembly.Name = "HowToFishTrainer.Runtime.V1_1_37.AimCache." + Convert.ToHexString(identity)[..16].ToLowerInvariant(); runtime.Mvid = new Guid(identity.AsSpan(0, 16));
        runtime.Write(output);
        using var final = ModuleDefMD.Load(output);
        var finalMethods = final.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
        var differences = new List<string>();
        foreach (var pair in originalMethods)
        {
            if (!finalMethods.TryGetValue(pair.Key, out var body)) throw new Exception("Method removed: " + pair.Key);
            if (body != pair.Value) differences.Add(pair.Key);
        }
        string[] expected = { typeName + "::Bind(", typeName + "::RefreshCatalog(", typeName + "::SilentVelocity(", typeName + "::Alive(", targetingName + "::Point(" };
        if (differences.Count != expected.Length || expected.Any(n => !differences.Any(m => m.Contains(n))))
            throw new Exception("Unexpected method delta: " + string.Join("; ", differences));
        var newMethods = finalMethods.Keys.Except(originalMethods.Keys).ToArray();
        if (newMethods.Length != 1 || !newMethods[0].Contains(typeName + "::TrackPlayer(Player)")) throw new Exception("Unexpected new methods");
        if (final.GetAssemblyRefs().Any(r => r.Name == "AimCacheRuntime")) throw new Exception("Helper dependency leaked");
        var report = new { passed = true, baselineRuntimeSha256 = Hash(baseline), runtimeSha256 = Hash(output), gameAssemblySha256 = Hash(gamePath),
            retainedMethods = originalMethods.Count - differences.Count, changedMethods = differences, addedMethods = newMethods,
            newRuntimeIdentity = final.Assembly.Name.String, nativePlayerContracts = 3, gameInjected = false, gameAssetsWritten = false, experimentalRadarSlopeIncluded = false };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "runtime-audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Runtime delta: {differences.Count} changed, {newMethods.Length} added, {originalMethods.Count - differences.Count} retained; {Hash(output)}");
    }
}
