using System;using System.Linq;using dnlib.DotNet;using dnlib.DotNet.Emit;
internal static class ContractAudit {
 static int count;static void Check(bool b,string n){if(!b)throw new Exception(n);count++;}
 static string Body(MethodDef m)=>m.HasBody?string.Join("\n",m.Body.Instructions.Select(x=>x.ToString())):"";
 static void Main(string[] a){using var before=ModuleDefMD.Load(a[0]);using var after=ModuleDefMD.Load(a[1]);using var game=ModuleDefMD.Load(a[2]);
  var host=before.GetTypes().Single(t=>t.FullName=="HowToFishTrainer.Runtime.TrainerBridgeHost");
  foreach(var x in new[]{"_stopped","_restoreRequested","_channelRejected"})Check(host.Fields.Any(f=>f.Name==x && f.FieldType.FullName.Contains("System.Boolean")),"bridge field "+x);
  foreach(var x in new[]{"_col","_rig","_hasJumped","_isSwimming","_groundCheckDist"})Check(game.GetTypes().Single(t=>t.Name=="PlayerMovement").Fields.Any(f=>f.Name==x),"native field "+x);
  Check(game.GetTypes().Single(t=>t.Name=="PlayerHolding").Fields.Any(f=>f.Name=="_bobOffset"),"native bob field");
  foreach(var pair in new[]{("PlayerHolding","GetBobOffset"),("PlayerMovement","Move")}){var methods=game.GetTypes().Single(t=>t.Name==pair.Item1).Methods.Where(m=>m.Name==pair.Item2).ToArray();Check(methods.Length==1&&!methods[0].IsStatic&&methods[0].Parameters.Count==1,"native hook "+pair);}
  Check(game.GetTypes().Single(t=>t.Name=="Map").BaseType.FullName=="Item","Map is Item");
  var helper=after.GetTypes().Single(t=>t.FullName=="AbyssalCrab.Runtime.MovementSafetyRuntime"); Check(helper.Methods.Any(m=>m.Name=="Install")&&helper.Methods.Any(m=>m.Name=="AfterRadarBob")&&helper.Methods.Any(m=>m.Name=="AfterMovement"),"handlers embedded");
  Check(!after.GetAssemblyRefs().Any(r=>r.Name=="MovementSafetyRuntime"),"no helper runtime dependency"); foreach(var r in after.GetAssemblyRefs()) if(!before.GetAssemblyRefs().Any(b=>b.FullName==r.FullName)) Console.WriteLine("Added reference: "+r.FullName);
  var changed=0;var total=0;foreach(var t in before.GetTypes()){var tt=after.GetTypes().Single(x=>x.FullName==t.FullName);foreach(var m in t.Methods){var mm=tt.Methods.Single(x=>x.FullName==m.FullName);total++;if(Body(m)!=Body(mm)){changed++;Check(t.FullName=="HowToFishTrainer.Runtime.Bootstrap"&&m.Name=="PatchPostfix","unexpected changed method "+m.FullName);}}}
  Check(changed==1,"one existing method changed"); Console.WriteLine("Contracts: "+count+" checks; "+total+" existing methods compared; "+changed+" changed"); }
}
