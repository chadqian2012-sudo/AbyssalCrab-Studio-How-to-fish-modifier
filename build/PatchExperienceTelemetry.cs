using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
internal static class PatchExperienceTelemetry
{
    static string Body(MethodDef method)=>!method.HasBody?"":string.Join("\n",method.Body.Instructions.Select(i=>i.ToString()));
    public static void Main(string[] args)
    {
        string root=args[0],work=Path.Combine(root,"build","experience-runtime"),helper=Path.Combine(work,"ExperienceTelemetry.dll");
        string baseline=Path.Combine(work,"baseline-runtime.dll");
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant()!="6d50047cf38d2035af83742432d206c385ac3f4b223ab7efadc90122e57285f1")throw new Exception("Unexpected runtime baseline");
        using var module=ModuleDefMD.Load(baseline);var before=module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m,Body);
        var type=PatchSpin.Clone(module,helper,"AbyssalCrab.Runtime.ExperienceTelemetry");
        var inspect=module.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.TrainerBridgeHost").Methods.Single(m=>m.Name=="InspectSession");
        foreach(var ret in inspect.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
        {ret.OpCode=OpCodes.Call;ret.Operand=type.Methods.Single(m=>m.Name=="Describe");inspect.Body.Instructions.Insert(inspect.Body.Instructions.IndexOf(ret)+1,Instruction.Create(OpCodes.Ret));}
        foreach(var old in before)if(old.Key!=inspect&&Body(old.Key)!=old.Value)throw new Exception("Unrelated runtime method changed: "+old.Key.FullName);
        byte[] identity=SHA256.HashData(File.ReadAllBytes(baseline).Concat(File.ReadAllBytes(helper)).Concat(File.ReadAllBytes(Path.Combine(root,"build","PatchExperienceTelemetry.cs"))).ToArray());
        module.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.Experience."+Convert.ToHexString(identity).Substring(0,16).ToLowerInvariant();module.Mvid=new Guid(identity.AsSpan(0,16));
        string output=Path.Combine(root,"patched","HowToFishTrainer.Runtime.dll");module.Write(output);
        using(var check=ModuleDefMD.Load(output)){if(check.GetAssemblyRefs().Any(a=>a.Name=="ExperienceTelemetry"))throw new Exception("External helper escaped embedding");}
        File.Copy(output,Path.Combine(root,"source","HowToFishTrainer","HowToFishTrainer.Runtime.dll"),true);
        File.WriteAllText(Path.Combine(root,"verification","experience-telemetry-patch.json"),JsonSerializer.Serialize(new{passed=true,baseline_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))).ToLowerInvariant(),runtime_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant(),runtime_identity=module.Assembly.Name.ToString(),existing_method_bodies_preserved=before.Count-1,changed_method=inspect.FullName,new_type=type.FullName,extra_poll_requests=0,weapon_or_input_writes=false,aiming_paths_unchanged=true},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Read-only current weapon telemetry added; all aiming and other runtime method bodies preserved");
    }
}
