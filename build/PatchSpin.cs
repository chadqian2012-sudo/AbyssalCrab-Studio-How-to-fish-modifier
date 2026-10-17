using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class PatchSpin
{
    private sealed class Mapper : ImportMapper
    {
        private readonly ModuleDef _target;
        public Mapper(ModuleDef target) { _target = target; }
        public override ITypeDefOrRef Map(ITypeDefOrRef type)
        { return _target.GetTypes().FirstOrDefault(t => t.FullName == type.FullName); }
    }

    internal static TypeDef Clone(ModuleDef targetModule, string helper, string typeName)
    {
        using var sourceModule = ModuleDefMD.Load(helper);
        var source = sourceModule.GetTypes().Single(t => t.FullName == typeName);
        if (source.NestedTypes.Count != 0) throw new Exception("Helper must have no nested types");
        var target = new TypeDefUser(source.Namespace, source.Name, targetModule.CorLibTypes.Object.TypeDefOrRef) { Attributes = source.Attributes };
        targetModule.Types.Add(target);
        var importer = new Importer(targetModule, ImporterOptions.TryToUseTypeDefs, default, new Mapper(targetModule));
        var fields = new Dictionary<FieldDef, FieldDef>();
        foreach (var field in source.Fields)
        {
            var copy = new FieldDefUser(field.Name, importer.Import(field.FieldSig), field.Attributes);
            if (field.HasConstant) copy.Constant = new ConstantUser(field.Constant.Value);
            target.Fields.Add(copy); fields.Add(field, copy);
        }
        var methods = new Dictionary<MethodDef, MethodDef>();
        foreach (var method in source.Methods)
        {
            var copy = new MethodDefUser(method.Name, importer.Import(method.MethodSig), method.ImplAttributes, method.Attributes);
            foreach (var param in method.ParamDefs) copy.ParamDefs.Add(new ParamDefUser(param.Name, param.Sequence, param.Attributes));
            target.Methods.Add(copy); methods.Add(method, copy);
        }
        foreach (var pair in methods)
        {
            var old = pair.Key; var copy = pair.Value;
            if (!old.HasBody) continue;
            var body = new CilBody { InitLocals = old.Body.InitLocals }; copy.Body = body;
            var locals = old.Body.Variables.ToDictionary(v => v, v => new Local(importer.Import(v.Type)));
            foreach (var local in old.Body.Variables) body.Variables.Add(locals[local]);
            var instructions = old.Body.Instructions.ToDictionary(i => i, i => new Instruction(i.OpCode));
            foreach (var instruction in old.Body.Instructions)
            {
                object operand = instruction.Operand;
                if (operand is Instruction branch) operand = instructions[branch];
                else if (operand is Instruction[] branches) operand = branches.Select(i => instructions[i]).ToArray();
                else if (operand is Local local) operand = locals[local];
                else if (operand is Parameter parameter) operand = copy.Parameters[parameter.Index];
                else if (operand is IMethod method && method.DeclaringType.FullName == source.FullName)
                    operand = methods.Single(p => p.Key.Name == method.Name && p.Key.MethodSig.Params.Count == method.MethodSig.Params.Count).Value;
                else if (operand is IField field && field.DeclaringType.FullName == source.FullName)
                    operand = fields.Single(p => p.Key.Name == field.Name).Value;
                else if (operand is IMethod externalMethod) operand = importer.Import(externalMethod);
                else if (operand is IField externalField) operand = importer.Import(externalField);
                else if (operand is ITypeDefOrRef type) operand = importer.Import(type);
                instructions[instruction].Operand = operand; body.Instructions.Add(instructions[instruction]);
            }
            foreach (var handler in old.Body.ExceptionHandlers) body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
            {
                TryStart = instructions[handler.TryStart], TryEnd = handler.TryEnd == null ? null : instructions[handler.TryEnd],
                HandlerStart = instructions[handler.HandlerStart], HandlerEnd = handler.HandlerEnd == null ? null : instructions[handler.HandlerEnd],
                FilterStart = handler.FilterStart == null ? null : instructions[handler.FilterStart],
                CatchType = handler.CatchType == null ? null : importer.Import(handler.CatchType)
            });
        }
        return target;
    }

    private static MethodDef Method(ModuleDef module, string type, string name)
    { return module.GetTypes().Single(t => t.FullName == type).Methods.Single(m => m.Name == name); }

    private static void Prepend(MethodDef method, params Instruction[] added)
    {
        method.Body.SimplifyBranches(); method.Body.SimplifyMacros(method.Parameters);
        for (int i = added.Length - 1; i >= 0; i--) method.Body.Instructions.Insert(0, added[i]);
    }

    private static void Support(ModuleDef module)
    {
        var ctor = Method(module, "HowToFishTrainer.Core.RuntimeFeatureSupport", ".cctor");
        var instructions = ctor.Body.Instructions;
        var store = instructions.Single(i => i.OpCode == OpCodes.Stsfld);
        var add = instructions.Select(i => i.Operand).OfType<IMethod>().First(m => m.Name == "Add");
        int at = instructions.IndexOf(store);
        foreach (var i in new[] { Instruction.Create(OpCodes.Dup), Instruction.Create(OpCodes.Ldstr, "player.spin.lock"), Instruction.Create(OpCodes.Callvirt, add), Instruction.Create(OpCodes.Pop) }) instructions.Insert(at++, i);
    }

    public static void Main(string[] args)
    {
        string root = args[0]; string working = Path.Combine(root, "build", "spin");
        using var core = ModuleDefMD.Load(Path.Combine(working, "baseline", "HowToFishTrainer.Core.dll"));
        var registry = Clone(core, Path.Combine(working, "SpinRegistry.dll"), "AbyssalCrab.Runtime.SpinRegistry");
        var getter = Method(core, "HowToFishTrainer.Core.FeatureRegistry", "get_All");
        int ret = getter.Body.Instructions.Count - 1;
        if (getter.Body.Instructions[ret].OpCode != OpCodes.Ret) throw new Exception("Unexpected registry getter");
        getter.Body.Instructions.Insert(ret, Instruction.Create(OpCodes.Call, registry.Methods.Single(m => m.Name == "Add")));
        var coreValidate = Method(core, "HowToFishTrainer.Core.FeatureArgumentValidator", "IsValid");
        var coreId = Method(core, "HowToFishTrainer.Core.FeatureDescriptor", "get_Id");
        var coreEquality = core.GetMemberRefs().First(m => m.Name == "op_Equality" && m.DeclaringType.FullName == "System.String");
        var originalValidate = coreValidate.Body.Instructions[0];
        Prepend(coreValidate, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Callvirt, coreId),
            Instruction.Create(OpCodes.Ldstr, "player.spin.lock"), Instruction.Create(OpCodes.Call, coreEquality),
            Instruction.Create(OpCodes.Brfalse, originalValidate), Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, registry.Methods.Single(m => m.Name == "ValidateSpinArguments")), Instruction.Create(OpCodes.Ret));
        Support(core);
        core.Write(Path.Combine(root, "patched", "HowToFishTrainer.Core.dll"));

        using var runtime = ModuleDefMD.Load(Path.Combine(working, "baseline", "HowToFishTrainer.Runtime.dll"));
        var spin = Clone(runtime, Path.Combine(working, "SpinRuntime.dll"), "AbyssalCrab.Runtime.SpinRuntime");
        const string host = "HowToFishTrainer.Runtime.TrainerBridgeHost";
        var equality = runtime.GetMemberRefs().First(m => m.Name == "op_Equality" && m.DeclaringType.FullName == "System.String");
        var apply = Method(runtime, host, "ApplyLock"); var native = apply.Body.Instructions[0];
        Prepend(apply, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldstr, "player.spin.lock"), Instruction.Create(OpCodes.Call, equality), Instruction.Create(OpCodes.Brfalse, native),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_2), Instruction.Create(OpCodes.Call, spin.Methods.Single(m => m.Name == "Apply")), Instruction.Create(OpCodes.Ret));
        Prepend(Method(runtime, host, "ValidateArguments"), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, spin.Methods.Single(m => m.Name == "ValidateArguments")));
        Prepend(Method(runtime, host, "SetLock"), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldarg_2), Instruction.Create(OpCodes.Call, spin.Methods.Single(m => m.Name == "NormalizeArguments")));
        var hook = Method(runtime, host, "HookOnlyLock"); native = hook.Body.Instructions[0];
        Prepend(hook, Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldstr, "player.spin.lock"), Instruction.Create(OpCodes.Call, equality), Instruction.Create(OpCodes.Brfalse, native), Instruction.Create(OpCodes.Ldc_I4_1), Instruction.Create(OpCodes.Ret));
        Prepend(Method(runtime, host, "RestoreFeature"), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, spin.Methods.Single(m => m.Name == "Restore")));
        Prepend(Method(runtime, host, "RestoreAll"), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, spin.Methods.Single(m => m.Name == "RestoreAll")));
        // A strict boolean check covers malformed direct bridge requests too.
        // The standard .lock route, proof registry, heartbeat and disable-all behavior remain in use.
        var identity = SHA256.HashData(File.ReadAllBytes(Path.Combine(working, "SpinRuntime.dll")));
        runtime.Assembly.Name = "HowToFishTrainer.Runtime.V1_1_37.Spin." + Convert.ToHexString(identity).Substring(0,16).ToLowerInvariant();
        runtime.Mvid = new Guid(identity.AsSpan(0,16));
        string output = Path.Combine(root, "patched", "HowToFishTrainer.Runtime.dll"); runtime.Write(output);
        File.Copy(output, Path.Combine(root, "source", "HowToFishTrainer", "HowToFishTrainer.Runtime.dll"), true);
        File.WriteAllText(Path.Combine(working, "patch-audit.json"), JsonSerializer.Serialize(new { featureId = "player.spin.lock", minimumRotationsPerSecond=2, maximumRotationsPerSecond=15, defaultRotationsPerSecond=4, modified = new[] { "FeatureRegistry.get_All", "FeatureArgumentValidator.IsValid", "RuntimeFeatureSupport..cctor", "ValidateArguments", "SetLock", "ApplyLock", "HookOnlyLock", "RestoreFeature", "RestoreAll" }, runtimeIdentity = runtime.Assembly.Name.ToString(), runtimeMvid = runtime.Mvid, coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"patched","HowToFishTrainer.Core.dll")))), runtimeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))) }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Added player.spin.lock to Core registry and existing runtime lock lifecycle");
    }
}
