using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

// Existing WPF fixtures cannot execute the renderer's near-plane and viewport clipping.
// This fixture runs the final packaged geometry IL and audits the changed runtime owner.
static class EntityOverlayVerification
{
    static readonly List<object> Checks = new();
    static int Failures, PassedChecks;
    static MethodInfo ClipLine, ClipDepth;
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static void Check(bool passed, string name, string detail = null)
    {
        Checks.Add(new { passed, name, detail });
        if (passed) PassedChecks++;
        if (!passed) { Failures++; Console.WriteLine("FAIL: " + name + (detail == null ? "" : " -- " + detail)); }
    }
    static bool Near(float a, float b, float tolerance = .001f) => Math.Abs(a - b) <= tolerance;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static (bool accepted, float x0, float y0, float x1, float y1) Line(float x0, float y0, float x1, float y1,
        float left = 100, float bottom = 50, float right = 900, float top = 650)
    {
        object[] args = { x0, y0, x1, y1, left, bottom, right, top };
        bool accepted = (bool)ClipLine.Invoke(null, args);
        return (accepted, (float)args[0], (float)args[1], (float)args[2], (float)args[3]);
    }
    static (bool accepted, float start, float end) Depth(float a, float b, float near = .3f)
    {
        object[] args = { a, b, near, 0f, 0f };
        bool accepted = (bool)ClipDepth.Invoke(null, args);
        return (accepted, (float)args[3], (float)args[4]);
    }
    static void Geometry(string candidate)
    {
        // The geometry owner uses only System.Single. No Unity type or native call is invoked.
        var context = new AssemblyLoadContext("entity-overlay-geometry", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(candidate));
        var type = assembly.GetType("AbyssalCrab.Runtime.EntityOverlayGeometry", throwOnError: true);
        ClipLine = type.GetMethod("ClipLine", BindingFlags.Public | BindingFlags.Static);
        ClipDepth = type.GetMethod("ClipDepth", BindingFlags.Public | BindingFlags.Static);
        Check(ClipLine != null && ClipDepth != null, "Final runtime exports both pure geometry methods");
        if (ClipLine == null || ClipDepth == null) return;

        var l = Line(200, 100, 800, 600);
        Check(l.accepted && Near(l.x0, 200) && Near(l.y0, 100) && Near(l.x1, 800) && Near(l.y1, 600), "Interior line retains its endpoints");
        l = Line(-100, 300, 1100, 300);
        Check(l.accepted && Near(l.x0, 100) && Near(l.x1, 900) && Near(l.y0, 300) && Near(l.y1, 300), "Horizontal crossing clips to the offset camera viewport");
        l = Line(500, -100, 500, 900);
        Check(l.accepted && Near(l.y0, 50) && Near(l.y1, 650), "Vertical crossing clips both edges without dividing by zero");
        l = Line(-100, -100, 1100, 800);
        Check(l.accepted && Near(l.x0, 100) && Near(l.y0, 50) && Near(l.x1, 900) && Near(l.y1, 650), "Diagonal crossing preserves the segment at both viewport corners");
        l = Line(50, 200, 50, 500);
        Check(!l.accepted, "Parallel line entirely outside the viewport is rejected");
        l = Line(100, 650, 900, 650);
        Check(l.accepted && Near(l.y0, 650) && Near(l.y1, 650), "A line on the viewport boundary remains visible");
        l = Line(-100, -50, -10, 40);
        Check(!l.accepted, "A line with no viewport intersection is rejected");
        l = Line(-1000000000, 300, 1000000000, 300);
        Check(l.accepted && Finite(l.x0) && Finite(l.x1) && Near(l.x0, 100, .1f) && Near(l.x1, 900, .1f), "Huge projected coordinates clip to the viewport without a giant line",
            $"actual x endpoints: {l.x0}, {l.x1}");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = !Line(invalid, 100, 800, 600).accepted && !Line(200, invalid, 800, 600).accepted
                && !Line(200, 100, invalid, 600).accepted && !Line(200, 100, 800, invalid).accepted;
            Check(rejected, "Non-finite projected endpoints are rejected: " + invalid);
            Check(!Line(200, 100, 800, 600, invalid, 50, 900, 650).accepted
                && !Line(200, 100, 800, 600, 100, invalid, 900, 650).accepted
                && !Line(200, 100, 800, 600, 100, 50, invalid, 650).accepted
                && !Line(200, 100, 800, 600, 100, 50, 900, invalid).accepted,
                "Non-finite viewport boundaries are rejected: " + invalid);
        }
        Check(!Line(200, 100, 800, 600, 100, 50, 100, 650).accepted
            && !Line(200, 100, 800, 600, 900, 50, 100, 650).accepted
            && !Line(200, 100, 800, 600, 100, 650, 900, 50).accepted,
            "Empty and reversed viewports cannot create a red frame");

        var d = Depth(1, 2);
        Check(d.accepted && Near(d.start, 0) && Near(d.end, 1), "A segment in front of the near plane retains the full interval");
        d = Depth(-2, -1);
        Check(!d.accepted, "A segment behind the camera is rejected");
        d = Depth(.1f, .2f);
        Check(!d.accepted, "A segment entirely before the near plane is rejected");
        d = Depth(-1, 1);
        Check(d.accepted && Near(d.start, .65f) && Near(d.end, 1), "A segment crossing the near plane clips its first endpoint");
        d = Depth(1, -1);
        Check(d.accepted && Near(d.start, 0) && Near(d.end, .35f), "Reversing a crossing segment clips the opposite endpoint");
        d = Depth(.3f, .3f);
        Check(d.accepted && Near(d.start, 0) && Near(d.end, 1), "A segment lying on the near plane has no division by zero");
        Check(!Depth(1, 2, 0).accepted && !Depth(1, 2, -.3f).accepted, "A nonpositive near plane is rejected");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(!Depth(invalid, 2).accepted && !Depth(1, invalid).accepted && !Depth(1, 2, invalid).accepted,
                "Non-finite depth or near plane is rejected: " + invalid);

        // Independent entity segments must not share a selection or carry previous-frame coordinates.
        var frameA = new[] { Line(200, 100, 400, 300), Line(600, 200, 800, 500), Line(20, 100, 40, 300) };
        var frameB = new[] { Line(300, 200, 500, 400), Line(650, 250, 850, 550), Line(120, 100, 140, 300) };
        Check(frameA.Count(x => x.accepted) == 2 && frameB.Count(x => x.accepted) == 3,
            "Multiple entity segments are independently recomputed when a target enters the viewport");
        Check(Near(frameA[0].x0, 200) && Near(frameB[0].x0, 300) && Near(frameB[1].x0, 650),
            "Moving one entity does not reuse another entity or an old frame's endpoint");
        context.Unload();
    }

    static string Body(MethodDef method)
    {
        if (!method.HasBody) return "none";
        string Operand(object value) => value switch {
            null => "", Instruction i => "@" + method.Body.Instructions.IndexOf(i),
            Instruction[] all => string.Join(",", all.Select(i => method.Body.Instructions.IndexOf(i))),
            IMethod m => m.FullName, IField f => f.FullName, ITypeDefOrRef t => t.FullName,
            Local l => "local:" + l.Index, Parameter p => "arg:" + p.Index, _ => value.ToString()
        };
        return string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Name + " " + Operand(i.Operand))) + "\n"
            + string.Join(",", method.Body.Variables.Select(v => v.Type.FullName)) + "\n"
            + string.Join(";", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.CatchType?.FullName + ":"
                + Operand(h.TryStart) + ":" + Operand(h.TryEnd) + ":" + Operand(h.HandlerStart) + ":" + Operand(h.HandlerEnd) + ":" + Operand(h.FilterStart)));
    }
    static IEnumerable<IMethod> Calls(MethodDef method) => method.HasBody
        ? method.Body.Instructions.Select(i => i.Operand).OfType<IMethod>() : Enumerable.Empty<IMethod>();
    static bool StringLiteral(MethodDef method, string value) => method.HasBody && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, value));
    static object Audit(string baseline, string candidate)
    {
        using var before = ModuleDefMD.Load(baseline);
        using var after = ModuleDefMD.Load(candidate);
        var originals = before.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var final = after.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var removed = originals.Keys.Except(final.Keys).ToArray();
        var changed = originals.Where(p => final.ContainsKey(p.Key) && Body(p.Value) != Body(final[p.Key])).Select(p => p.Key).ToArray();
        Check(removed.Length == 0, "Every baseline runtime method remains present", string.Join("; ", removed));
        string prefix = "AbyssalCrab.Runtime.";
        var allowed = new HashSet<string> {
            prefix + "GameplayTargeting::Point", prefix + "GameplayTargeting::Reset", prefix + "GameplayTargeting::Refresh", prefix + "GameplayTargeting::.cctor",
            prefix + "GameplayRuntime::Validate", prefix + "GameplayRuntime::Configured", prefix + "GameplayRuntime::Restore",
            prefix + "PredictiveAimRuntime::Validate", prefix + "PredictiveAimRuntime::Configure",
            prefix + "CombatBridgeRuntime::TryHandle",
            prefix + "MagicRangeRenderer::Configure", prefix + "MagicRangeRenderer::OnDestroy", prefix + "MagicRangeRenderer::OnGUI",
            prefix + "MagicRangeRenderer::.ctor", prefix + "MagicRangeRenderer::.cctor",
            "HowToFishTrainer.Runtime.TrainerBridgeHost::ReadAimProjection"
        };
        var unexpected = changed.Where(key => {
            var method = final[key];
            return !allowed.Contains(method.DeclaringType.FullName + "::" + method.Name);
        }).ToArray();
        Check(unexpected.Length == 0 && changed.Length == allowed.Count, "Exactly the reviewed 16 baseline methods change; every other original method is retained", string.Join("; ", unexpected));
        var projection = final.Values.Single(m => m.DeclaringType.FullName == "HowToFishTrainer.Runtime.TrainerBridgeHost" && m.Name == "ReadAimProjection");
        var projectionInstructions = projection.Body.Instructions;
        Check(projectionInstructions.Count == 3 && projectionInstructions[0].OpCode == OpCodes.Ldarg_0
            && projectionInstructions[1].OpCode == OpCodes.Call && projectionInstructions[1].Operand is IMethod projectionCall
            && projectionCall.DeclaringType.FullName == prefix + "EntityOverlayProjection" && projectionCall.Name == "Read"
            && projectionInstructions[2].OpCode == OpCodes.Ret,
            "Existing crosshair read delegates directly to the shared projection owner without added actions");
        foreach (string name in new[] { "Select", "AimPoint", "LaunchDirection" })
        {
            var method = final.Values.Single(m => m.DeclaringType.FullName == prefix + "GameplayTargeting" && m.Name == name);
            Check(originals.TryGetValue(method.FullName, out var old) && Body(old) == Body(method), "Magic targeting behavior remains unchanged: " + name);
        }
        foreach (string name in new[] { "Prefix", "Select", "Valid", "Alive" })
        {
            var method = final.Values.Single(m => m.DeclaringType.FullName == prefix + "PredictiveAimRuntime" && m.Name == name);
            Check(originals.TryGetValue(method.FullName, out var old) && Body(old) == Body(method), "Aim targeting behavior remains unchanged: " + name);
        }
        foreach (string typeName in new[] { "GameplayTargeting", "PredictiveAimRuntime" })
        {
            var gate = final.Values.Single(m => m.DeclaringType.FullName == prefix + typeName && m.Name == (typeName == "GameplayTargeting" ? "Point" : "Alive"));
            Check(gate.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "_targetTeammates")
                && !Calls(gate).Any(c => c.Name == "get_UseFriendlyFire"), "Existing teammate targeting gate remains independent: " + typeName);
        }
        var renderer = after.GetTypes().Single(t => t.FullName == prefix + "MagicRangeRenderer");
        Check(renderer.BaseType.FullName == "UnityEngine.MonoBehaviour", "Final renderer retains its Unity component base");
        Check(after.GetTypes().Any(t => t.FullName == prefix + "EntityOverlayGeometry"), "Final runtime contains the tested geometry owner");
        var enabled = final.Values.Single(m => m.DeclaringType.FullName == prefix + "PredictiveAimRuntime" && m.Name == "get_Enabled");
        var active = enabled.Body.Instructions;
        bool FalseBranch(int index) => (active[index].OpCode == OpCodes.Brfalse || active[index].OpCode == OpCodes.Brfalse_S)
            && ReferenceEquals(active[index].Operand, active[8]);
        Check(active.Count == 10 && active[0].OpCode == OpCodes.Ldsfld && active[0].Operand is IField enabledField && enabledField.Name == "_enabled"
            && FalseBranch(1) && active[2].OpCode == OpCodes.Ldsfld && active[2].Operand is IField hostField && hostField.Name == "_host"
            && FalseBranch(3) && active[4].OpCode == OpCodes.Ldsfld && active[4].Operand is IField checkedHost && checkedHost.FullName == hostField.FullName
            && active[5].OpCode == OpCodes.Ldstr && Equals(active[5].Operand, "combat.aim_assist")
            && active[6].OpCode == OpCodes.Callvirt && active[6].Operand is IMethod lockRead && lockRead.Name == "IsLockEnabled"
            && active[7].OpCode == OpCodes.Ret && active[8].OpCode == OpCodes.Ldc_I4_0 && active[9].OpCode == OpCodes.Ret,
            "An old enabled flag cannot keep the overlay active after the host removes the aim lock");
        foreach (string name in new[] { "Refresh", "OnGUI" })
        {
            var gate = renderer.Methods.Single(m => m.Name == name);
            Check(StringLiteral(gate, "combat.silent_aim") && Calls(gate).Any(c => c.Name == "get_Enabled" && c.DeclaringType.FullName == prefix + "PredictiveAimRuntime"),
                "Either combat feature independently keeps the renderer active: " + name);
        }
        var draw = renderer.Methods.Single(m => m.Name == "DrawEntities");
        Check(Calls(draw).Any(c => c.Name == "get_DisplayTargets") && Calls(draw).Any(c => c.Name == "DisplayPoint")
            && !Calls(draw).Any(c => c.Name == "Select" || c.Name == "SelectHeadSilent"),
            "Multi-entity drawing consumes the shared catalog rather than a selected target");
        var displayPoint = final.Values.Single(m => m.DeclaringType.FullName == prefix + "GameplayTargeting" && m.Name == "DisplayPoint");
        Check(!displayPoint.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "_targetTeammates")
            && !Calls(displayPoint).Any(c => c.Name == "get_UseFriendlyFire"),
            "Displaying teammates is independent of both targeting and native friendly-fire settings");
        var point = final.Values.Single(m => m.DeclaringType == displayPoint.DeclaringType && m.Name == "Point");
        var lookup = displayPoint.FullName;
        bool UsesColliderRule(MethodDef caller, OpCode rule)
        {
            var il = caller.Body.Instructions;
            var sites = Enumerable.Range(1, il.Count - 1).Where(i => il[i].Operand is IMethod call && call.FullName == lookup).ToArray();
            return sites.Length > 0 && sites.All(i => il[i-1].OpCode == rule);
        }
        Check(UsesColliderRule(point, OpCodes.Ldc_I4_1) && UsesColliderRule(draw, OpCodes.Ldc_I4_0),
            "Targeting requires the remote collider; display can fall back to a live teammate's position");
        var visual = after.GetTypes().Single(t => t.FullName == prefix + "EntityOverlayVisual");
        var refresh = visual.Methods.Single(m => m.Name == "Refresh");
        Check(Calls(refresh).Any(c => c.Name == "get_sharedMesh") && Calls(refresh).Any(c => c.Name == "get_rootBone")
            && refresh.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "_meshes")
            && refresh.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "_roots"),
            "Skeleton cache tracks mesh and root replacements on reused renderer components");
        Check(Calls(visual.Methods.Single(m => m.Name == "TryBounds")).Count(c => c.Name == "ValidBounds") >= 2,
            "Both renderer and collider bounds are checked before the position-only fallback");
        var distance = renderer.Methods.Single(m => m.Name == "ConfigureDistance");
        var reached = new HashSet<MethodDef>();
        void Visit(MethodDef method)
        {
            if (!reached.Add(method)) return;
            foreach (var call in Calls(method)) if (final.TryGetValue(call.FullName, out var local)) Visit(local);
        }
        Visit(distance);
        var writes = reached.SelectMany(Calls).Where(c => c.Name == "SetLock" || c.Name == "ApplyLock" || c.Name == "ApplyFeature"
            || c.Name == "EnsureBeforeLock" || c.Name == "RestoreAll" || c.Name == "ClearProjectiles" || c.Name == "RedirectShot").Select(c => c.FullName).Distinct().ToArray();
        Check(writes.Length == 0, "Display-distance configuration does not reach combat-lock, restore, or projectile actions", string.Join("; ", writes));
        var handlers = final.Values.Where(m => StringLiteral(m, "view.entity_overlay.configure")).ToArray();
        Check(handlers.Length == 1, "Independent display command has one runtime dispatch owner", string.Join("; ", handlers.Select(m => m.FullName)));
        if (handlers.Length == 1)
        {
            var handler = handlers[0];
            var instructions = handler.Body.Instructions;
            int literal = instructions.ToList().FindIndex(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "view.entity_overlay.configure"));
            int branch = instructions.ToList().FindIndex(literal + 1, i => i.OpCode.FlowControl == FlowControl.Cond_Branch);
            int end = branch < 0 || !(instructions[branch].Operand is Instruction destination) ? -1 : instructions.IndexOf(destination);
            bool isolated = branch > literal && end > branch && (instructions[branch].OpCode == OpCodes.Brfalse || instructions[branch].OpCode == OpCodes.Brfalse_S);
            Check(isolated, "Independent command has an isolated true branch before the next command");
            if (isolated)
            {
                var block = instructions.Skip(branch + 1).Take(end - branch - 1).ToArray();
                var ownedCalls = block.Select(i => i.Operand).OfType<IMethod>().Where(c => c.DeclaringType.FullName.StartsWith(prefix)).ToArray();
                string[] permitted = { prefix + "CombatBridgeRuntime::Call", prefix + "MagicRangePolicy::CheckedDistance", prefix + "MagicRangeRenderer::ConfigureDistance" };
                bool safeCalls = ownedCalls.All(c => permitted.Contains(c.DeclaringType.FullName + "::" + c.Name));
                var literals = block.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
                Check(safeCalls && literals.Contains("ValidateAuthority") && literals.Contains("Reply")
                    && !literals.Any(s => s.Contains("Lock") || s.StartsWith("combat."))
                    && !block.Any(i => i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Stsfld),
                    "Independent command only validates authority, checks distance, configures display, and replies",
                    string.Join("; ", ownedCalls.Select(c => c.FullName)));
            }
        }
        Check(!after.GetAssemblyRefs().Any(r => r.Name.String.Contains("EntityOverlayRuntime") || r.Name == "TargetOptionsRuntime"),
            "Final runtime has no leaked temporary helper assembly dependency");
        return new { originalMethods = originals.Count, retainedMethods = originals.Count - changed.Length - removed.Length, changedMethods = changed, removedMethods = removed,
            addedMethods = final.Keys.Except(originals.Keys).ToArray(), distanceReachableMethods = reached.Select(m => m.FullName).OrderBy(x => x).ToArray() };
    }

    static int Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("baseline-runtime candidate-runtime renderer-source output-directory");
        Directory.CreateDirectory(args[3]);
        object audit = null;
        string fatal = null;
        try { Geometry(args[1]); audit = Audit(args[0], args[1]); }
        catch (Exception error) { Failures++; fatal = error.ToString(); Console.WriteLine(fatal); }
        var report = new { passed = Failures == 0, checks = Checks.Count, failures = Failures,
            baselineRuntimeSha256 = Hash(args[0]), runtimeSha256 = Hash(args[1]), rendererSourceSha256 = Hash(args[2]),
            geometryExecution = "actual final runtime IL through reflection; pure Single methods only",
            unityNativeRuntimeExecuted = false, gameStarted = false, gameInjected = false, stubUsed = false,
            limitations = "Geometry execution verifies clipping math. dnlib checks are static; they do not establish Unity rendering, multiplayer behavior, or performance.",
            audit, results = Checks, fatal };
        File.WriteAllText(Path.Combine(args[3], "entity-overlay-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Entity overlay: {PassedChecks}/{Checks.Count} checks passed; {Failures} failures");
        return Failures == 0 ? 0 : 1;
    }
}
