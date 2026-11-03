using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
internal static class PatchMovementSafety {
    public static void Main(string[] args) {
        var baseline=args[0]; var helper=args[1]; var output=args[2];
        using var module=ModuleDefMD.Load(baseline);
        var added=PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.MovementSafetyRuntime");
        var install=added.Methods.Single(m=>m.Name=="Install");
        var bootstrap=module.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.Bootstrap");
        var method=bootstrap.Methods.Single(m=>m.Name=="PatchPostfix");
        if(method.Parameters.Count!=3)throw new Exception("Unexpected bootstrap patch signature");
        method.Body.SimplifyBranches(); method.Body.SimplifyMacros(method.Parameters);
        foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray()) {
            ret.OpCode=OpCodes.Ldarg_0;ret.Operand=null;int at=method.Body.Instructions.IndexOf(ret)+1;
            method.Body.Instructions.Insert(at++,Instruction.Create(OpCodes.Ldarg_1));
            method.Body.Instructions.Insert(at++,Instruction.Create(OpCodes.Call,install));
            method.Body.Instructions.Insert(at,Instruction.Create(OpCodes.Ret));
        }
        byte[] hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).ToArray());
        module.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.MovementSafety."+Convert.ToHexString(hash).Substring(0,16).ToLowerInvariant();
        module.Mvid=new Guid(hash.AsSpan(0,16));
        module.Write(output);
        using var check=ModuleDefMD.Load(output);
        if(check.GetAssemblyRefs().Any(a=>a.Name=="MovementSafetyRuntime")) throw new Exception("External helper reference escaped");
        Console.WriteLine("Patched runtime candidate created: "+output);
    }
}
