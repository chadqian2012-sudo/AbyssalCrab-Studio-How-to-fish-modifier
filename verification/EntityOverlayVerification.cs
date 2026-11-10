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
    static void GullChallenge(string candidate, string managed)
    {
        // Load type definitions from the installed game. Only managed score and ID arithmetic
        // run here. Null dictionary values stand for unused bodies; no Unity object is constructed.
        var context = new AssemblyLoadContext("gull-managed-round", isCollectible: true);
        context.Resolving += (loader, name) => {
            string path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? loader.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
        };
        try
        {
            var type = context.LoadFromAssemblyPath(Path.GetFullPath(candidate)).GetType("AbyssalCrab.Runtime.GullChallengeRound", true);
            var score = Activator.CreateInstance(type, new object[] { "fixture-round", null });
            object Read(string name) => type.GetField(name).GetValue(score);
            object Invoke(string method, params object[] args) => type.GetMethod(method).Invoke(score,args);
            var birds = (System.Collections.IDictionary)Read("Birds");
            birds.Add(12,null); birds.Add(24,null);
            void Hit(int target,float time,bool kill) => Invoke("Record",target,time,kill);
            Hit(12,8,true);
            Check((bool)Invoke("get_Pending") && !(bool)Invoke("get_Active") && (int)Read("Hits") == 0,
                "Before native wave registration no damage can score");
            type.GetField("Started").SetValue(score,10f);
            Hit(11,10.1f,true); Hit(12,9f,false); Hit(12,float.NaN,true); Hit(12,70f,true);
            Check((int)Read("Hits") == 0 && (float)Read("FirstHit") < 0,
                "Unregistered targets, pre-start damage and the exact time limit cannot score");
            Hit(12,10.25f,false); Hit(12,11f,true); Hit(12,11f,true); Hit(24,69.9f,false);
            Check((int)Read("Hits") == 3 && (int)Read("Kills") == 1 && Near((float)Read("FirstHit"),.25f),
                "First confirmed hit preserves elapsed time and a repeated kill cannot score twice");
            var ids = (HashSet<uint>)Read("Projectiles");
            Invoke("RememberVolley",20u,0u,3); Invoke("RememberVolley",9u,10u,0);
            Check(ids.Count == 0, "A reset ID counter or mismatched native pellet count creates no attribution");
            Invoke("RememberVolley",uint.MaxValue,2u,3);
            Check(ids.SetEquals(new uint[]{uint.MaxValue,0,1}),"A native three-pellet volley retains its IDs across unsigned wrap");
            Invoke("Finish","fixture finished"); Hit(24,20,true);
            Check(!(bool)Invoke("get_Active") && !(bool)Invoke("get_Pending") && birds.Count == 0 && ids.Count == 0 &&
                (int)Read("Hits") == 3 && (string)Read("Id") == "fixture-round",
                "Finishing releases target and projectile references, preserving the score and round ID");
        }
        finally { context.Unload(); }

        using var game = ModuleDefMD.Load(Path.Combine(managed,"Assembly-CSharp.dll"));
        using var runtime = ModuleDefMD.Load(candidate);
        MethodDef Method(ModuleDef assembly,string type,string method) =>
            assembly.GetTypes().Single(t => t.FullName == type).Methods.Single(m => m.Name == method);
        var shoot = Method(game,"Weapon","Shoot"); var impact = Method(game,"ProjectileManager","Hit");
        var settle = Method(game,"Server","RpcLogic___HitCreature___215526726");
        Check(shoot.MethodSig.Params.Count == 0 && Calls(shoot).Any(c => c.Name == "AddProjectile") && Calls(shoot).Any(c => c.Name == "AddProjectiles") &&
            Calls(shoot).Any(c => c.Name == "LocalHit") && impact.MethodSig.Params.Select(p=>p.FullName).SequenceEqual(new[]{"Projectile","ProjectileType","UnityEngine.RaycastHit"}) &&
            Calls(impact).Any(c => c.Name == "LocalHit") && Calls(settle).Any(c => c.Name == "ServerChangeHp"),
            "Installed game's gun, impact and server health methods expose the checked attribution chain");
        string runtimeType = "AbyssalCrab.Runtime.CombatFeedbackRuntime";
        var fire = Method(runtime,runtimeType,"ShotPrefix"); var hit = Method(runtime,runtimeType,"ImpactPrefix");
        var local = Method(runtime,runtimeType,"LocalPrefix"); var hp = Method(runtime,runtimeType,"DamagePostfix");
        bool Field(MethodDef m,string field) => m.Body.Instructions.Any(i => i.Operand is IField f && f.Name == field);
        Check(Field(fire,"Gun") && Calls(fire).Any(c=>c.Name=="get_Holder") && Field(hit,"IsLocal") && Field(hit,"Owner") &&
            Field(hit,"FromNpc") && Field(hit,"IsArrow") && Calls(hit).Any(c=>c.Name=="Remove") &&
            Field(local,"Shooting") && Field(local,"Impact") && Calls(hp).Any(c=>c.Name=="Consume") && Calls(hp).Any(c=>c.Name=="get_Hp"),
            "Round scores join this gun's local hit evidence to the host's actual HP change");
        var pool = Method(runtime,"AbyssalCrab.Runtime.CombatBridgeRuntime","Build");
        var bind = Method(runtime,"AbyssalCrab.Runtime.GullChallengeRound","Bind");
        Check(game.GetTypes().Single(t => t.FullName=="Bird").BaseType.FullName=="Creature" &&
            Calls(pool).Any(c=>c.FullName.Contains("GetComponent<Bird>")) && !Calls(pool).Any(c=>c.Name=="get_Creature") &&
            Calls(pool).Any(c=>c.FullName.Contains("GetComponent<Albatross>")) && Calls(bind).Any(c=>c.FullName.Contains("GetComponent<Bird>")),
            "Uninstantiated bird prefabs use native components, without relying on Awake-created caches");
        foreach (string method in new[]{"AddProjectile","AddProjectiles"})
        {
            var add=Method(game,"ProjectileManager",method);
            var il=add.Body.Instructions;
            Check(il.Skip(1).Take(12).Any(i=>i.OpCode==OpCodes.Ldfld && i.Operand is IField f && f.Name=="_nextId") &&
                il.Take(16).Any(i=>i.OpCode==OpCodes.Starg_S && i.Operand is Parameter p && p.Name=="id") &&
                il.Any(i=>i.OpCode==OpCodes.Stfld && i.Operand is IField f && f.Name=="_nextId"),
                "Native local ID allocation starts at the pre-shot counter: " + method);
        }
        var rollback=Method(runtime,"AbyssalCrab.Runtime.CombatBridgeRuntime","ChallengeRollback");
        Check(Calls(rollback).Any(c=>c.Name=="RoundFor") && Calls(rollback).Any(c=>c.Name=="get_Pending") &&
            Field(rollback,"WaveId") && Calls(rollback).Any(c=>c.Name=="Finish") && !Calls(rollback).Any(c=>c.Name=="Read" || c.Name=="Call"),
            "Native rollback clears only its own pending wave without invoking reflected game code");
        var bridge=Method(runtime,"AbyssalCrab.Runtime.CombatBridgeRuntime","TryHandle");
        var op=bridge.Body.Instructions;
        int index(string literal) => op.ToList().FindIndex(i => i.OpCode==OpCodes.Ldstr && Equals(i.Operand,literal));
        int same=op.ToList().FindIndex(i=>i.Operand is IField f && f.Name=="Id" && f.DeclaringType.Name=="GullChallengeRound");
        int start=op.ToList().FindIndex(i=>i.Operand is IMethod m && m.Name=="PrepareRound");
        Check(same>=0 && Calls(bridge).Any(c=>c.Name=="TryParseExact") && index("missing")>same && index("missing")<start &&
            op.Skip(same).Take(index("missing")-same).Any(i=>i.OpCode==OpCodes.Ret),
            "Same-round result is returned before a new start, and unknown stop IDs have a distinct missing receipt");
    }

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
        var receipts = changed.Select(key => final[key]).Where(m => m.DeclaringType.FullName == "HowToFishTrainer.Runtime.TrainerBridgeHost").ToArray();
        Check(receipts.Length == 2 && receipts.Any(m => m.Name == "FunReceipt" && Calls(m).Count(c => c.Name == "ChallengeReceipt") == 1)
            && receipts.Any(m => m.Name == "RollbackFunWave" && Calls(m).Count(c => c.Name == "ChallengeRollback") == 1),
            "Native registration and rollback each inform the challenge once; dispatch and spawning retain their code");
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
        var gui = renderer.Methods.Single(m => m.Name == "OnGUI").Body.Instructions.ToList();
        int CallIndex(List<Instruction> il, string name) => il.FindIndex(i => i.Operand is IMethod m && m.Name == name);
        var drawIl = draw.Body.Instructions.ToList();
        int aimRead = CallIndex(drawIl, "TryAimPoint"), hasAim = CallIndex(drawIl, "get_HasValue"), aimValue = CallIndex(drawIl, "get_Value");
        Check(aimRead > 0 && Calls(draw).Count(c => c.Name == "TryAimPoint") == 1 &&
            CallIndex(drawIl, "ProjectBox") < CallIndex(drawIl, "MinMaxRect") &&
            CallIndex(drawIl, "MinMaxRect") < CallIndex(drawIl, "DrawTexture") && CallIndex(drawIl, "DrawTexture") < hasAim &&
            hasAim < aimRead && aimRead < aimValue && drawIl[hasAim + 1].IsBrtrue() &&
            ReferenceEquals(drawIl[hasAim + 1].Operand, drawIl[aimValue - 1]) &&
            ReferenceEquals(drawIl[hasAim - 1].Operand, drawIl[aimValue - 1].Operand) &&
            drawIl.Skip(aimRead + 1).Take(aimValue - aimRead - 1).Any(i => i.Operand is IMethod ctor && ctor.Name == ".ctor" &&
                ctor.DeclaringType.FullName.StartsWith("System.Nullable`1<System.Boolean>")),
            "Crosshair read follows a visible box; subsequent boxes reuse both successful and failed reads");
        int key = CallIndex(gui,"get_rightCtrlKey"), pressed = CallIndex(gui,"get_isPressed");
        int entities = CallIndex(gui,"DrawEntities"), range = CallIndex(gui,"DrawRange");
        Check(key >= 0 && pressed == key + 1 && entities > pressed && range > entities
            && (gui[pressed+1].OpCode == OpCodes.Brtrue || gui[pressed+1].OpCode == OpCodes.Brtrue_S)
            && ReferenceEquals(gui[pressed+1].Operand, gui[entities+1]),
            "Holding right Ctrl skips exactly the entity draw call and still evaluates the original magic range circle");
        MethodDef MarkerOwner(string owner, string name) => final.Values.Single(m => m.DeclaringType.FullName == prefix + owner && m.Name == name);
        var current = MarkerOwner("PredictiveAimRuntime","CurrentLock");
        var currentFields = current.Body.Instructions.Select(i => i.Operand).OfType<IField>().Select(f => f.Name.String).ToHashSet();
        Check(currentFields.IsSupersetOf(new[]{"_target","_wasAds","_handledFrame","_weapon","Target"})
            && Calls(current).Any(m => m.Name == "get_Enabled") && Calls(current).Any(m => m.Name == "get_IsAds")
            && Calls(current).Any(m => m.Name == "get_frameCount") && Calls(current).Any(m => m.Name == "get_HeldItem")
            && !Calls(current).Any(m => m.Name == "Select" || m.Name == "Valid"),
            "ADS corner marker reads the current frame's real selected target and held aiming weapon without rescanning");
        Check(Calls(draw).Any(m => m.FullName == current.FullName) && Calls(draw).Any(m => m.Name == "HighlightedShotTarget")
            && Calls(draw).Count(m => m.Name == "DrawLockCorners") == 1,
            "Both combat owners feed the one corner marker inside the existing per-entity draw");
        var mark = MarkerOwner("GameplayRuntime","MarkGuidance");
        var writers = final.Values.Where(m => Calls(m).Any(c => c.FullName == mark.FullName)).Select(m => m.Name.String).OrderBy(n => n).ToArray();
        var track = MarkerOwner("GameplayRuntime","Track").Body.Instructions.ToList();
        int steer = track.FindIndex(i => i.OpCode == OpCodes.Stfld && i.Operand is IField f && f.Name == "Velocity");
        int volley = track.FindIndex(i => i.OpCode == OpCodes.Ldsfld && i.Operand is IField f && f.Name == "_latestVolley");
        int marked = CallIndex(track,"MarkGuidance");
        Check(writers.SequenceEqual(new[]{"RedirectShot","Track"}) && steer >= 0 && volley > steer && marked > volley + 1
            && (track[volley+1].OpCode == OpCodes.Bne_Un || track[volley+1].OpCode == OpCodes.Bne_Un_S)
            && ReferenceEquals(track[volley+1].Operand, track[marked+1]),
            "A previous volley cannot replace the latest shot marker; only a real launch correction or velocity write can mark it");
        var redirect = MarkerOwner("GameplayRuntime","RedirectShot").Body.Instructions.ToList();
        var shot = MarkerOwner("GameplayRuntime","HighlightedShotTarget");
        int corrected = CallIndex(redirect, "RecordFeatureHit");
        Check(corrected >= 0 && CallIndex(redirect,"MarkGuidance") > corrected && mark.Body.Instructions.Any(i => Equals(i.Operand, .2f))
            && StringLiteral(shot,"combat.silent_aim") && Calls(shot).Any(m => m.Name == "Point") && Calls(shot).Any(m => m.Name == "get_unscaledTime"),
            "A successful shot pulses briefly and eligibility is checked before the marker is drawn");
        var cleared = MarkerOwner("GameplayRuntime","ClearProjectiles").Body.Instructions.Where(i => i.OpCode == OpCodes.Stsfld)
            .Select(i => ((IField)i.Operand).Name.String).ToHashSet();
        Check(cleared.Contains("_latestVolley") && cleared.Contains("_guidedTarget"),
            "Projectiles and their visual selection are cleared together at shutdown and restore");
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
        if (args.Length != 5) throw new ArgumentException("baseline-runtime candidate-runtime renderer-source output-directory game-managed-directory");
        Directory.CreateDirectory(args[3]);
        object audit = null;
        string fatal = null;
        try { Geometry(args[1]); GullChallenge(args[1],args[4]); audit = Audit(args[0], args[1]); }
        catch (Exception error) { Failures++; fatal = error.ToString(); Console.WriteLine(fatal); }
        var report = new { passed = Failures == 0, checks = Checks.Count, failures = Failures,
            baselineRuntimeSha256 = Hash(args[0]), runtimeSha256 = Hash(args[1]), rendererSourceSha256 = Hash(args[2]),
            geometryExecution = "actual runtime IL; pure clipping, round scoring, and projectile-ID arithmetic",
            unityNativeRuntimeExecuted = false, gameStarted = false, gameInjected = false, stubUsed = false,
            limitations = "Geometry execution verifies clipping math. dnlib checks are static; they do not establish Unity rendering, multiplayer behavior, or performance.",
            audit, results = Checks, fatal };
        File.WriteAllText(Path.Combine(args[3], "entity-overlay-verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Entity overlay: {PassedChecks}/{Checks.Count} checks passed; {Failures} failures");
        return Failures == 0 ? 0 : 1;
    }
}
