using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class RebrandRuntime
{
    public static void Main(string[] args)
    {
        string root=args[0], input=args[1];
        byte[] original=File.ReadAllBytes(input);
        var module=ModuleDefMD.Load(original);
        var changed=new List<object>();
        foreach(var type in module.GetTypes()) foreach(var method in type.Methods)
        {
            if(!method.HasBody)continue;
            foreach(var instruction in method.Body.Instructions)
            {
                if(instruction.OpCode!=OpCodes.Ldstr||instruction.Operand is not string text)continue;
                string replacement=text.Replace("LiYu Third Person Camera","AbyssalCrab Studio Third Person Camera").Replace("LiYu Local Avatar","AbyssalCrab Studio Local Avatar");
                if(replacement==text)continue;
                instruction.Operand=replacement;
                changed.Add(new{method=method.FullName,instruction_offset=instruction.Offset});
            }
        }
        if(changed.Count!=2)throw new Exception("Unexpected runtime branding sites: "+changed.Count);
        var identity=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(original))+"AbyssalCrab Studio-v1"));
        string previousIdentity=module.Assembly.Name;
        module.Assembly.Name="HowToFishTrainer.Runtime.V1_1_37.LocalTest."+Convert.ToHexString(identity).Substring(0,16).ToLowerInvariant();
        module.Mvid=new Guid(identity.AsSpan(0,16));
        string output=Path.Combine(root,"patched","HowToFishTrainer.Runtime.dll");
        module.Write(output);
        File.Copy(output,Path.Combine(root,"source","HowToFishTrainer","HowToFishTrainer.Runtime.dll"),true);
        File.WriteAllText(Path.Combine(root,"build","runtime-branding-audit.json"),JsonSerializer.Serialize(new{changed,previous_identity=previousIdentity,new_identity=module.Assembly.Name.ToString(),mvid=module.Mvid,input_sha256=Convert.ToHexString(SHA256.HashData(original)),output_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)))},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Rebranded two runtime object names with a distinct injection identity");
    }
}
