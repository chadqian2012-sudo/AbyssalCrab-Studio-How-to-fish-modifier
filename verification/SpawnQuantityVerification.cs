using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;

// Run with the game's Mono and managed assemblies in an offline process.
// The fixture builds instruction lists from plain objects and never spawns an entity.
internal static class SpawnQuantityVerification
{
    const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    sealed class Prefab
    {
        public string name { get; }
        public Prefab(string name) { this.name = name; }
    }

    static void ExpectRejected(Action invoke)
    {
        try { invoke(); }
        catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { return; }
        throw new Exception("Invalid quantity was accepted");
    }
    static object Get(object value, string prop) => value.GetType().GetProperty(prop, Members).GetValue(value);

    public static int Main(string[] args)
    {
        var runtime = Assembly.LoadFrom(args[0]);
        var bridge = runtime.GetType("AbyssalCrab.Runtime.CombatBridgeRuntime", true);
        var host = runtime.GetType("HowToFishTrainer.Runtime.TrainerBridgeHost", true);
        var validate = host.GetMethod("ValidateArguments", Members);
        foreach (var feature in new[] {"generation.item", "generation.fish"})
        {
            var command = new JObject { ["value"] = 1, ["quantity"] = 100, ["distance"] = 3 };
            validate.Invoke(null, new object[] {feature, command});
            foreach (JToken bad in new JToken[] {0, 101, 1.5, "2", JValue.CreateNull()})
            {
                command["quantity"] = bad;
                ExpectRejected(() => validate.Invoke(null, new object[] {feature, command}));
            }
        }
        var count = bridge.GetMethod("CheckedCount", Members);
        if (!Equals(count.Invoke(null, new object[]{new JValue(150), 150}), 150)) throw new Exception("150 rejected");
        foreach (JToken bad in new JToken[]{0, 151, 1.5, "2", JValue.CreateNull()})
            ExpectRejected(() => count.Invoke(null, new object[]{bad, 150}));
        Console.WriteLine("Passed regular and fun quantity boundaries.");
        var hostObject = FormatterServices.GetUninitializedObject(host);
        var catalogType = host.GetNestedType("FunPrefabCatalog", Members);
        var definitionType = host.GetNestedType("RuntimeFunDefinition", Members);
        var catalog = Activator.CreateInstance(catalogType, true);
        foreach (var key in new[]{"Weapons","Ordinary","Birds","Fish","AttackFish","Crabs","Rods"})
            ((IList)Get(catalog, key)).Add(new Prefab(key == "Ordinary" ? "Frisbee" : key == "AttackFish" ? "Piranha" : key));
        var create = host.GetMethod("BuildFunInstructions", Members);
        var requested = bridge.GetField("_count", Members);
        requested.SetValue(null, 150);
        var trigger = bridge.GetMethod("TryHandle", Members);
        var boss = new JObject { ["featureId"] = "fun.event.execute",
            ["arguments"] = new JObject { ["eventId"] = "boss-surprise", ["count"] = 150 } };
        ExpectRejected(() => trigger.Invoke(null, new object[]{hostObject, boss}));
        foreach (var eventId in new[]{"weapon-rain","rod-forest","frisbee-storm","junk-bundle","piranha-rain",
            "seagull-migration","fish-rain","crab-parade","whale-fall","rare-fish-school","seafood-platter",
            "seagull-feast","ocean-parade"})
        {
            var definition = Activator.CreateInstance(definitionType, Members, null, new object[]{eventId,eventId,"light",6}, null);
            var adjusted = bridge.GetMethod("AdjustDefinition", Members).Invoke(null, new object[]{definition});
            var instructions = (IList)create.Invoke(hostObject, new object[]{adjusted, catalog, "offline-quantity"});
            if (instructions.Count != 150) throw new Exception(eventId + " plan length " + instructions.Count);
            var prefab = (Prefab)Get(instructions[0], "Prefab");
            string expected = eventId == "weapon-rain" ? "Weapons" : eventId == "rod-forest" ? "Rods" :
                eventId == "frisbee-storm" || eventId == "junk-bundle" ? "Frisbee" : null;
            if (expected != null && prefab.name != expected) throw new Exception(eventId + " prefab " + prefab.name);
            Console.WriteLine(eventId + " created " + instructions.Count + " instructions.");
        }
        return 0;
    }
}
