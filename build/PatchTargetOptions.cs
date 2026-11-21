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
        if (args[0] == "--rust-kernels")
        {
            PatchRustKernels(args[1..]);
            return;
        }
        if (args[0] == "--world-drops")
        {
            PatchWorldDrops(args[1..]);
            return;
        }
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

    static void PatchRustKernels(string[] args)
    {
        string baseline = args[0], helper = args[1], output = args[2], game = args[3];
        string parentHash = Hash(baseline), gameHash = Hash(game);
        if (parentHash != "6c99e369c784d76ee76b045ec37590ba52586cae478f2d0f7164d80596fc2efe" ||
            gameHash != "e3ec0befacc5269efdecb279f0ae72155fcde3ebaeed07e361aa15e6f7fc84b1")
            throw new InvalidDataException("The verified r29 Runtime and game assembly are required.");

        using var module = ModuleDefMD.Load(baseline);
        using var source = ModuleDefMD.Load(helper);
        var types = module.GetTypes().ToDictionary(t => t.FullName);
        var fields = types.Values.SelectMany(t => t.Fields).ToDictionary(f => f.FullName, f => f.Attributes);
        var refs = module.GetAssemblyRefs().Select(r => r.FullName).ToHashSet();
        static string Declaration(MethodDef m) =>
            $"{m.Attributes}/{m.ImplAttributes}/{m.ImplMap?.Module?.Name}/{m.ImplMap?.Name}/{m.ImplMap?.Attributes}";
        var before = types.Values.SelectMany(t => t.Methods)
            .ToDictionary(m => m.FullName, m => (Body: Body(m), Declaration: Declaration(m)));

        const string prefix = "AbyssalCrab.Runtime.";
        var delegates = new[] { prefix + "ProjectionDelegate", prefix + "SelectionDelegate" };
        foreach (string name in delegates)
            PatchSpin.Clone(module, source, name, Array.Empty<string>());
        var updates = new Dictionary<string, string[]>
        {
            [prefix + "OverlayBatchRenderer"] = new[] { "Configure" },
            [prefix + "MagicRangeRenderer"] = new[] { ".ctor", "OnGUI", "DrawEntities" },
            [prefix + "PredictiveAimRuntime"] = new[] { "Select" },
            [prefix + "CombatBridgeRuntime"] = new[] { "TryHandle" }
        };
        var expected = new HashSet<string>();
        foreach (var (owner, entries) in updates)
        {
            foreach (string entry in entries)
                expected.Add(types[owner].Methods.Single(m => m.Name == entry).FullName);
            PatchSpin.Clone(module, source, owner, entries, addFields: true);
        }

        var identity = SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper))
            .Concat(File.ReadAllBytes(typeof(PatchTargetOptions).Assembly.Location)).ToArray());
        module.Assembly.Name = "HowToFishTrainer.Runtime.RustKernels." + Convert.ToHexString(identity)[..16];
        module.Mvid = new Guid(identity.AsSpan(0, 16));
        module.Write(output);

        using var final = ModuleDefMD.Load(output);
        var finalTypes = final.GetTypes().ToDictionary(t => t.FullName);
        var after = finalTypes.Values.SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var finalFields = finalTypes.Values.SelectMany(t => t.Fields).ToDictionary(f => f.FullName);
        var changed = new List<string>();
        foreach (var (name, original) in before)
        {
            if (!after.TryGetValue(name, out var method) || original.Declaration != Declaration(method))
                throw new InvalidDataException("Existing method declaration changed: " + name);
            if (Body(method) != original.Body) changed.Add(name);
        }
        if (!expected.SetEquals(changed))
            throw new InvalidDataException("Unexpected method edits: " + string.Join(", ", changed.Except(expected)
                .Concat(expected.Except(changed).Select(name => "missing " + name))));
        if (fields.Any(pair => !finalFields.TryGetValue(pair.Key, out var field) || field.Attributes != pair.Value))
            throw new InvalidDataException("An existing field declaration changed.");
        if (!finalTypes.Keys.ToHashSet().SetEquals(types.Keys.Concat(delegates)))
            throw new InvalidDataException("Unexpected Runtime type changes.");
        var added = after.Keys.Except(before.Keys).ToArray();
        var methodOwners = delegates.Concat(new[] { prefix + "OverlayBatchRenderer", prefix + "MagicRangeRenderer" }).ToHashSet();
        if (added.Any(name => !methodOwners.Contains(after[name].DeclaringType.FullName)))
            throw new InvalidDataException("Unexpected new Runtime method.");
        var addedFields = finalFields.Keys.Except(fields.Keys).ToArray();
        if (addedFields.Any(name => !updates.ContainsKey(finalFields[name].DeclaringType.FullName) ||
            finalFields[name].DeclaringType.FullName == prefix + "CombatBridgeRuntime"))
            throw new InvalidDataException("Unexpected new Runtime field.");
        var newRefs = final.GetAssemblyRefs().Select(r => r.FullName).Except(refs).ToArray();
        if (newRefs.Length != 0)
            throw new InvalidDataException("Unexpected assembly dependencies: " + string.Join(", ", newRefs));

        var report = new
        {
            passed = true, parent = parentHash, runtime = Hash(output), game = gameHash,
            retained = before.Count - changed.Count, changed, added, addedFields, addedTypes = delegates,
            nativeDeclarationsPreserved = true, gameStarted = false
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "runtime-audit.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rust kernels Runtime audited: {changed.Count} changed, {report.retained} retained, {added.Length} new methods.");
    }

    static void PatchWorldDrops(string[] args)
    {
        string stage = args[0], game = args[1];
        if (Hash(game) != "e3ec0befacc5269efdecb279f0ae72155fcde3ebaeed07e361aa15e6f7fc84b1")
            throw new InvalidDataException("Review the updated game before building the cleanup action.");
        string fishNet = Path.Combine(Path.GetDirectoryName(game)!, "FishNet.Runtime.dll");
        const string feature = "world.clear_drops";
        object Patch(string kind, string expected, string owner, string entry, bool core)
        {
            string parent = Path.Combine(stage, $"r27-{kind}.dll");
            string helper = Path.Combine(stage, $"world-drops-{kind}.dll");
            string output = Path.Combine(stage, $"candidate-{kind}.dll");
            if (Hash(parent) != expected) throw new InvalidDataException("Unexpected r27 " + kind);
            using var module = ModuleDefMD.Load(parent);
            var before = module.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Body);
            var refs = module.GetAssemblyRefs().Select(r => r.FullName).ToHashSet();
            var instance = core ? null : PatchSpin.Clone(module, helper, "AbyssalCrab.Runtime.DropInstance");
            var cleanup = core ? null : PatchSpin.Clone(module, helper, "AbyssalCrab.Runtime.DropCleanup");
            string[] changedEntries = core ? new[] { entry } : new[] { entry, "Reset" };
            TypeDef target = PatchSpin.Clone(module, helper, "AbyssalCrab.Runtime." + owner, changedEntries);
            var dispatch = target.Methods.Single(m => m.Name == entry);
            var allowed = new HashSet<string> { dispatch.FullName };
            var registrations = new List<string> { dispatch.FullName };
            if (core)
            {
                PatchSpin.Support(module, feature);
                string constructor = module.Find("HowToFishTrainer.Core.RuntimeFeatureSupport", false)
                    .Methods.Single(m => m.IsStaticConstructor).FullName;
                allowed.Add(constructor);
                registrations.Add(constructor);
            }
            else
            {
                var bridgeCall = target.Methods.Single(m => m.Name == "Call");
                if (!bridgeCall.IsPrivate) throw new InvalidDataException("Unexpected bridge helper access");
                bridgeCall.Attributes = (bridgeCall.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Assembly;
                allowed.Add(target.Methods.Single(m => m.Name == "Reset").FullName);
                var host = module.Find("HowToFishTrainer.Runtime.TrainerBridgeHost", false);
                var method = host.Methods.Single(m => m.Name == "CancelFunEvents" && !m.IsStatic
                    && m.MethodSig.Params.Count == 1 && m.MethodSig.Params[0].ElementType == ElementType.String);
                var handler = cleanup.Methods.Single(m => m.Name == "Cancel" && m.IsStatic
                    && m.ReturnType.ElementType == ElementType.Void && m.MethodSig.Params.Single().FullName == host.FullName);
                method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
                method.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Call, handler));
                allowed.Add(method.FullName);
                var bootstrap = module.Find("HowToFishTrainer.Runtime.Bootstrap", false);
                var initialize = bootstrap.Methods.Single(m => m.Name == "Start");
                var harmony = bootstrap.Fields.Single(f => f.Name == "_harmony" && f.IsStatic && f.FieldType.FullName == "HarmonyLib.Harmony");
                var observe = cleanup.Methods.Single(m => m.Name == "ObserveLifecycle" && m.IsStatic &&
                    m.ReturnType.ElementType == ElementType.Void && m.MethodSig.Params.Single().FullName == harmony.FieldType.FullName);
                var init = initialize.Body.Instructions;
                int hooks = init.IndexOf(init.Single(i => i.Operand is IMethod c && c.Name == "RunOptionalHooks"));
                init.Insert(hooks + 1, Instruction.Create(OpCodes.Ldsfld, harmony));
                init.Insert(hooks + 2, Instruction.Create(OpCodes.Call, observe));
                allowed.Add(initialize.FullName);

                var generated = host.Fields.Single(f => f.Name == "_spawnedByTrainer");
                var record = cleanup.Methods.Single(m => m.Name == "RecordSummon" && m.IsStatic
                    && m.ReturnType.ElementType == ElementType.Void && m.MethodSig.Params.Count == 3
                    && m.MethodSig.Params[0].ElementType == ElementType.Object
                    && m.MethodSig.Params.Skip(1).All(p => p.FullName == generated.FieldType.FullName));
                foreach (var spawn in host.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "SpawnNewItem"))))
                {
                    if (spawn.Name != "ProcessGenerationJobs" && spawn.Name != "SpawnFunInstruction")
                        throw new InvalidDataException("Review new trainer spawn path: " + spawn.FullName);
                    var il = spawn.Body.Instructions;
                    // Read the exact object that this successful trainer request added to its own job.
                    int add = Enumerable.Range(3, il.Count - 3).Single(i =>
                        il[i - 2].Operand is IMethod list && list.Name == "get_Spawned" &&
                        (list.DeclaringType.FullName == host.FullName + "/GenerationJob" ||
                         list.DeclaringType.FullName == host.FullName + "/FunWaveJob") &&
                        il[i - 1].IsLdloc() && il[i].Operand is IMethod called &&
                        called.FullName == "System.Void System.Collections.Generic.List`1<System.Object>::Add(System.Object)");
                    var item = il[add - 1];
                    var job = il[add - 3];
                    var listGetter = il[add - 2];
                    int at = add + 1;
                    il.Insert(at++, new Instruction(item.OpCode, item.Operand));
                    il.Insert(at++, new Instruction(job.OpCode, job.Operand));
                    il.Insert(at++, new Instruction(listGetter.OpCode, listGetter.Operand));
                    if (spawn.Name == "ProcessGenerationJobs")
                    {
                        il.Insert(at++, Instruction.Create(OpCodes.Ldarg_0));
                        il.Insert(at++, Instruction.Create(OpCodes.Ldfld, generated));
                    }
                    else il.Insert(at++, Instruction.Create(OpCodes.Ldnull));
                    il.Insert(at, Instruction.Create(OpCodes.Call, record));
                    allowed.Add(spawn.FullName);
                }

                // The despawn observer and a partially rolled-back wave must share the same list.
                var rollback = host.Methods.Single(m => m.Name == "RollbackFunWave");
                var wave = rollback.Body.Instructions;
                var getSpawned = wave.Select(i => i.Operand).OfType<IMethod>().First(m => m.Name == "get_Spawned");
                var addRange = host.Methods.Single(m => m.Name == "CleanupTrackedFunWave").Body.Instructions
                    .Select(i => i.Operand).OfType<IMethod>().Single(m => m.Name == "AddRange");
                int copy = wave.IndexOf(wave.Single(i => i.Operand is IMethod c && c.Name == "ToList"));
                if (wave[copy - 2].OpCode.Code != Code.Ldloca_S || wave[copy - 1].Operand is not IMethod remainder ||
                    remainder.Name != "get_Remaining" || wave[copy + 1].OpCode != OpCodes.Newobj ||
                    !wave.Take(copy).Any(i => i.Operand is IMethod c && c.Name == "Clear"))
                    throw new InvalidDataException("Unexpected partial-rollback result construction.");
                wave[copy].OpCode = OpCodes.Callvirt;
                wave[copy].Operand = addRange;
                wave.Insert(copy - 2, Instruction.Create(OpCodes.Ldarg_1));
                wave.Insert(copy - 1, Instruction.Create(OpCodes.Callvirt, getSpawned));
                wave.Insert(copy, Instruction.Create(OpCodes.Dup));
                allowed.Add(rollback.FullName);
            }
            var identity = SHA256.HashData(File.ReadAllBytes(parent).Concat(File.ReadAllBytes(helper))
                .Concat(File.ReadAllBytes(args[2])).Concat(File.ReadAllBytes(args[3])).ToArray());
            if (!core) module.Assembly.Name = "HowToFishTrainer.Runtime.V1_1_37.WorldDrops." + Convert.ToHexString(identity)[..16].ToLowerInvariant();
            module.Mvid = new Guid(identity.AsSpan(0, 16));
            module.Write(output);
            using var result = ModuleDefMD.Load(output);
            var methods = result.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
            if (before.Keys.Except(methods.Keys).Any()) throw new InvalidDataException("An existing method was removed.");
            var changed = before.Keys.Where(k => Body(methods[k]) != before[k]).ToArray();
            if (!allowed.SetEquals(changed))
                throw new InvalidDataException("Unexpected method changes: " + string.Join(", ", changed));
            var added = methods.Keys.Except(before.Keys).ToArray();
            if (added.Any(k => core || (methods[k].DeclaringType.FullName != cleanup.FullName && methods[k].DeclaringType.FullName != instance.FullName)))
                throw new InvalidDataException("Unexpected new method: " + string.Join(", ", added));
            if (result.GetAssemblyRefs().Any(r => !refs.Contains(r.FullName)))
                throw new InvalidDataException("Unexpected assembly dependencies: " + string.Join(", ", result.GetAssemblyRefs().Select(r => r.FullName).Except(refs)));
            foreach (var key in registrations)
                if (methods[key].Body.Instructions.Count(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, feature)) != 1)
                    throw new InvalidDataException("Cleanup feature must appear once in " + key);
            if (!core)
            {
                using var native = ModuleDefMD.Load(game);
                var nativeClear = native.Find("ItemManager", false).Methods.Single(m => m.Name == "ClearDictionary");
                var batch = result.Find(cleanup.FullName, false).Methods.Single(m => m.Name == "ProcessBatch");
                string Despawn(MethodDef m) => m.Body.Instructions.Select(i => i.Operand).OfType<IMethod>()
                    .Single(c => c.Name == "Despawn").FullName;
                if (Despawn(batch) != Despawn(nativeClear))
                    throw new InvalidDataException("World cleanup does not call the native network despawn overload.");
                bool Calls(MethodDef from, string fullName) => from.Body.Instructions.Any(i => i.Operand is IMethod c && c.FullName == fullName);
                var cleanupOwner = batch.DeclaringType;
                var start = cleanupOwner.Methods.Single(m => m.Name == "Start");
                var cancel = cleanupOwner.Methods.Single(m => m.Name == "Cancel");
                var record = cleanupOwner.Methods.Single(m => m.Name == "RecordSummon");
                var writers = methods.Values.Where(m => m.HasBody && Calls(m, record.FullName)).Select(m => m.FullName);
                var host = result.Find("HowToFishTrainer.Runtime.TrainerBridgeHost", false);
                if (!writers.ToHashSet().SetEquals(new[] { "ProcessGenerationJobs", "SpawnFunInstruction" }
                    .Select(n => host.Methods.Single(m => m.Name == n).FullName)))
                    throw new InvalidDataException("Trainer summon provenance must have exactly the two verified spawn writers.");
                using var network = ModuleDefMD.Load(fishNet);
                var networkType = network.Find("FishNet.Object.NetworkBehaviour", false);
                var onStop = networkType.Methods.Single(m => m.Name == "OnStopServer" && m.IsVirtual);
                var stop = networkType.Methods.Single(m => m.Name == "OnStopServer_Internal");
                var stopCallbacks = network.Find("FishNet.Object.NetworkObject", false).Methods.Single(m => m.Name == "InvokeStopCallbacks");
                var itemStop = native.Find("Item", false).Methods.Single(m => m.Name == onStop.Name && m.IsVirtual && !m.IsNewSlot);
                var observe = cleanupOwner.Methods.Single(m => m.Name == "ObserveLifecycle");
                if (!Calls(stopCallbacks, stop.FullName) || !Calls(stop, onStop.FullName)
                    || !Calls(result.Find("HowToFishTrainer.Runtime.Bootstrap", false).Methods.Single(m => m.Name == "Start"), observe.FullName)
                    || !observe.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, itemStop.Name.String)))
                    throw new InvalidDataException("Native item lifetime observation is missing.");
                foreach (var name in new[] { "CleanupGenerated", "CleanupTrackedFunWave", "RollbackFunWave", "RollbackGenerationJob" })
                    if (!host.Methods.Single(m => m.Name == name).Body.Instructions.Any(i => i.Operand is IMethod c &&
                        c.FullName == "System.Object[] System.Collections.Generic.List`1<System.Object>::ToArray()"))
                        throw new InvalidDataException("Source removal requires snapshot traversal in " + name);
                if (!Calls(methods[dispatch.FullName], start.FullName) || !Calls(start, batch.FullName)
                    || !Calls(batch, cleanupOwner.Methods.Single(m => m.Name == "IsClearableDrop").FullName)
                    || changed.Where(k => methods[k].Name == "CancelFunEvents" || methods[k].Name == "Reset").Any(k => !Calls(methods[k], cancel.FullName)))
                    throw new InvalidDataException("Cleanup dispatch, eligibility check or cancellation hook is missing.");
            }
            return new { parentSha256 = expected, sha256 = Hash(output), retainedMethods = before.Count - changed.Length, changedMethods = changed, addedMethods = added };
        }
        var core = Patch("core", "a2cac4e5393b41c98c843f37acd8662c29b8b5475662c28e9115bc8874199bfc", "GameplayRegistry", "Add", true);
        var runtime = Patch("runtime", "de14b34c34e894d8e1b992d549918c643a30d1c8cfb4ed3b0ab4b24f8704787a", "CombatBridgeRuntime", "TryHandle", false);
        var report = new { passed = true, runtime, core, gameAssemblySha256 = Hash(game), fishNetSha256 = Hash(fishNet), gameInjected = false, gameAssetsWritten = false };
        File.WriteAllText(Path.Combine(stage, "runtime-audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
