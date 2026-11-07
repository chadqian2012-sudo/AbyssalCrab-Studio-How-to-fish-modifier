using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchSpinPose
{
    private static string Body(MethodDef method)
    {
        if (!method.HasBody) return "<no body>";
        string Operand(object value) => value switch {
            null => "",
            Instruction target => "@" + method.Body.Instructions.IndexOf(target),
            Instruction[] targets => string.Join(",", targets.Select(t => method.Body.Instructions.IndexOf(t))),
            IMethod called => called.FullName,
            IField field => field.FullName,
            ITypeDefOrRef type => type.FullName,
            Local local => "local:" + local.Index,
            Parameter parameter => "arg:" + parameter.Index,
            _ => value.ToString()
        };
        return string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Name + " " + Operand(i.Operand))) +
            "\nlocals:" + string.Join(",", method.Body.Variables.Select(v => v.Type.FullName)) +
            "\nhandlers:" + string.Join(";", method.Body.ExceptionHandlers.Select(h =>
                h.HandlerType + ":" + h.CatchType?.FullName + ":" + Operand(h.TryStart) + ":" + Operand(h.TryEnd) +
                ":" + Operand(h.HandlerStart) + ":" + Operand(h.HandlerEnd) + ":" + Operand(h.FilterStart)));
    }

    private static int VerifyRetained(string baselinePath, string outputPath, string excludedType)
    {
        using var baseline = ModuleDefMD.Load(baselinePath);
        using var output = ModuleDefMD.Load(outputPath);
        var actual = output.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        int count = 0;
        foreach (var method in baseline.GetTypes().Where(t => t.FullName != excludedType).SelectMany(t => t.Methods))
        {
            if (!actual.TryGetValue(method.FullName, out var same) || Body(method) != Body(same))
                throw new InvalidOperationException("Unexpected change outside spin: " + method.FullName);
            count++;
        }
        if (output.GetAssemblyRefs().Any(a => a.Name == "SpinPose" || a.Name == "SpinRegistry"))
            throw new InvalidOperationException("Standalone build helper leaked into runtime dependencies");
        return count;
    }
    private static void Replace(ModuleDef module, string helper, params string[] names)
    {
        foreach (string name in names)
        {
            var previous = module.GetTypes().SingleOrDefault(t => t.FullName == name);
            if (previous != null) module.Types.Remove(previous);
            var replacement = PatchSpin.Clone(module, helper, name);
            if (previous == null) continue;
            var methods = replacement.Methods.ToDictionary(m => m.FullName);
            foreach (var type in module.GetTypes())
            foreach (var method in type.Methods.Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is IMethod called && called.DeclaringType.FullName == name)
                    instruction.Operand = methods.TryGetValue(called.FullName, out var target) ? target :
                        throw new InvalidOperationException("Missing replacement: " + called.FullName);
            }
        }
    }

    public static void Main(string[] args)
    {
        string root = args[0], work = Path.Combine(root, "build", "spin-pose");
        using var runtime = ModuleDefMD.Load(Path.Combine(work, "baseline-runtime.dll"));
        string[] retained = runtime.GetTypes().Select(t => t.FullName).Where(n => n != "AbyssalCrab.Runtime.SpinRuntime").ToArray();
        Replace(runtime, Path.Combine(work, "SpinPose.dll"), "AbyssalCrab.Runtime.SpinHeldVisualRuntime", "AbyssalCrab.Runtime.SpinPosePolicy",
            "AbyssalCrab.Runtime.SpinPoseRuntime", "AbyssalCrab.Runtime.SpinRuntime");
        if (retained.Any(n => runtime.GetTypes().All(t => t.FullName != n))) throw new Exception("An existing runtime type was removed");
        var identity = SHA256.HashData(File.ReadAllBytes(Path.Combine(work, "SpinPose.dll"))
            .Concat(File.ReadAllBytes(Path.Combine(work, "baseline-runtime.dll"))).ToArray());
        runtime.Assembly.Name = "HowToFishTrainer.Runtime.V1_1_37.SpinPose." + Convert.ToHexString(identity).Substring(0, 16).ToLowerInvariant();
        runtime.Mvid = new Guid(identity.AsSpan(0, 16));
        string output = Path.Combine(root, "patched", "HowToFishTrainer.Runtime.dll");
        string stagedRuntime = Path.Combine(work, "staged-runtime.dll");
        string stagedCore = Path.Combine(work, "staged-core.dll");
        runtime.Write(stagedRuntime);
        using var core = ModuleDefMD.Load(Path.Combine(work, "baseline-core.dll"));
        Replace(core, Path.Combine(work, "SpinRegistry.dll"), "AbyssalCrab.Runtime.SpinRegistry");
        core.Write(stagedCore);
        int unchangedRuntimeMethods = VerifyRetained(Path.Combine(work, "baseline-runtime.dll"), stagedRuntime,
            "AbyssalCrab.Runtime.SpinRuntime");
        int unchangedCoreMethods = VerifyRetained(Path.Combine(work, "baseline-core.dll"),
            stagedCore, "AbyssalCrab.Runtime.SpinRegistry");
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        // Publish only after every runtime/Core comparison and dependency check has succeeded.
        File.Copy(stagedRuntime, output, true);
        File.Copy(stagedRuntime, Path.Combine(root, "source", "HowToFishTrainer", "HowToFishTrainer.Runtime.dll"), true);
        File.Copy(stagedCore, Path.Combine(root, "patched", "HowToFishTrainer.Core.dll"), true);
        File.WriteAllText(Path.Combine(work, "patch-audit.json"), JsonSerializer.Serialize(new {
            featureId = "player.spin.lock", baselineRuntimeSha256 = Hash(Path.Combine(work, "baseline-runtime.dll")),
            baselineCoreSha256 = Hash(Path.Combine(work, "baseline-core.dll")),
            runtimeSha256 = Hash(output), coreSha256 = Hash(Path.Combine(root, "patched", "HowToFishTrainer.Core.dll")),
            runtimeIdentity = runtime.Assembly.Name.ToString(), retainedTypes = retained.Length,
            unchangedRuntimeMethods, unchangedCoreMethods,
            nativeGameplayMethodsModified = false, networkPositionModified = false,
            optionalArgument = "autoJump", defaultAutoJump = false,
            cameraWritten = false, realJumpCommandSent = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Replaced only spin helpers; retained all other integrated runtime modules.");
    }
}
