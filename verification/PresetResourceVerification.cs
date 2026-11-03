using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;

static class PresetResourceVerification
{
    static string Sha(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static Dictionary<string,string> Baml(ModuleDef module)
    {
        var resource=module.Resources.OfType<EmbeddedResource>().Single(r=>r.Name.String.EndsWith(".g.resources"));
        using var stream=new MemoryStream(resource.CreateReader().ToArray());using var reader=new ResourceReader(stream);
        var result=new Dictionary<string,string>();
        foreach(DictionaryEntry entry in reader)
        {
            using var bytes=new MemoryStream();((Stream)entry.Value!).CopyTo(bytes);
            result.Add((string)entry.Key,Sha(bytes.ToArray()));
        }
        return result;
    }
    public static void Main(string[] a)
    {
        using var old=ModuleDefMD.Load(a[0]);using var app=ModuleDefMD.Load(a[1]);using var compressed=ModuleDefMD.Load(a[2]);
        var before=old.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name.String,r=>Sha(r.CreateReader().ToArray()));
        var after=app.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name.String,r=>Sha(r.CreateReader().ToArray()));
        var zipped=compressed.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name.String,r=>Sha(r.CreateReader().ToArray()));
        if(before.Count!=after.Count||after.Count!=zipped.Count||before.Any(p=>!p.Key.EndsWith(".g.resources")&&(!after.TryGetValue(p.Key,out var hash)||hash!=p.Value))||after.Any(p=>!zipped.TryGetValue(p.Key,out var hash)||hash!=p.Value))throw new Exception("Unexpected embedded resource change");
        var oldBaml=Baml(old);var newBaml=Baml(app);
        var changed=oldBaml.Keys.Union(newBaml.Keys).Where(k=>!oldBaml.TryGetValue(k,out var x)||!newBaml.TryGetValue(k,out var y)||x!=y).ToArray();
        if(changed.Length!=1||changed[0]!="howtofishtrainer/app/experiencebar.baml")throw new Exception("UI changed outside the restore-button owner: "+string.Join(",",changed));
        if(after["HowToFishTrainer.Runtime.dll"]!="8dfbd831c6434cbbfa1c338922e184875f0c48ec70d4e3a0d246a20c2dfa7327")throw new Exception("Unexpected Runtime");
        string Connect(ModuleDef m)=>string.Join("\n",m.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.App.MainWindow").Methods.Single(x=>x.Name.EndsWith(".Connect")).Body.Instructions.Select(i=>i.ToString()));
        if(Connect(old)!=Connect(app))throw new Exception("MainWindow connection mapping changed");
        if(!old.GetAssemblyRefs().Select(x=>x.FullName).SequenceEqual(app.GetAssemblyRefs().Select(x=>x.FullName)))throw new Exception("Assembly references changed");
        File.WriteAllText(a[3],JsonSerializer.Serialize(new{passed=true,resourceCount=after.Count,resourceHashes=after,mainWindowConnectionsUnchanged=true,assemblyReferencesUnchanged=true,changedUiEntries=changed,runtimeByteIdentical=true,gameInjected=false,manualUiVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Only restore-button BAML changed among {newBaml.Count} UI entries; Runtime, Harmony and MainWindow connections unchanged.");
    }
}
