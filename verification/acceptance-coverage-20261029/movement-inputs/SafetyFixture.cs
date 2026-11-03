using System;using HarmonyLib;using UnityEngine;using AbyssalCrab.Runtime;using HowToFishTrainer.Runtime;
static class SafetyFixture {
 static int n;static void C(bool ok,string s){if(!ok)throw new Exception(s);n++;} static void Main(){
 var harmony=new Harmony();Harmony.Fail="Move";MovementSafetyRuntime.Install(harmony,"Attachments:get_Damage");
 foreach(var p in Harmony.all.Values)C(p.Postfixes.Count==0,"partial install must roll back");
 Harmony.Fail=null;MovementSafetyRuntime.Install(harmony,"Attachments:get_Damage");C(Harmony.all.Count==2,"two hooks discovered");foreach(var p in Harmony.all.Values)C(p.Postfixes.Count==1,"both hooks installed exactly once");MovementSafetyRuntime.Install(harmony,"Attachments:get_Damage");foreach(var p in Harmony.all.Values)C(p.Postfixes.Count==1,"idempotent install");
 var bridge=new TrainerBridgeHost();Bootstrap.Set(bridge);var player=new Player{Holding=new(),Movement=new()};Player.LocalPlayer=player;var h=player.Holding;var m=player.Movement;h.HeldItem=new Map();h.SetBob(new(2,3,4));m.Sprinting=true;MovementSafetyRuntime.AfterRadarBob(h);C(h.Bob.x==0&&h.Bob.y==0,"radar sprint bob removed");
 h.HeldItem=new Item();h.SetBob(new(2,3,4));MovementSafetyRuntime.AfterRadarBob(h);C(h.Bob.x==2,"other item untouched");
 h.HeldItem=new Map();m.Sprinting=false;m.Velocity=new(1,0,0);h.SetBob(new(2,3,4));MovementSafetyRuntime.AfterRadarBob(h);C(h.Bob.x==2,"slow radar unchanged");
 Physics.casts=0;m.Rig.linearVelocity=new(12,3,0);MovementSafetyRuntime.AfterMovement(m);C(Physics.casts==0&&m.Rig.linearVelocity.x==12&&m.Rig.linearVelocity.y==3,"threshold velocity skips probe unchanged");
 m.Rig.linearVelocity=new(40,3,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==12&&m.Rig.linearVelocity.y==3,"grounded uphill limited, vertical retained");
 C(Physics.casts==1,"high speed still probes ground");
 m.SetJump(true);m.Rig.linearVelocity=new(40,7,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40&&m.Rig.linearVelocity.y==7,"jump untouched");m.SetJump(false);
 m.Grounded=false;m.Rig.linearVelocity=new(40,7,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40,"air untouched");m.Grounded=true;
 m.OnBoat=true;m.Rig.linearVelocity=new(40,0,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40,"boat untouched");m.OnBoat=false;
 bridge.Stop();h.SetBob(new(2,3,4));m.Sprinting=true;MovementSafetyRuntime.AfterRadarBob(h);C(h.Bob.x==2,"stopped bridge leaves radar native");m.Rig.linearVelocity=new(40,0,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40,"stopped bridge leaves movement native");
 Console.WriteLine("Movement safety fixture: "+n+" checks passed"); }
}
