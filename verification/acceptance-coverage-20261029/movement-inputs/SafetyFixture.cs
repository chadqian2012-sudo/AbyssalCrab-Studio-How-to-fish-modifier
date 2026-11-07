using System;using HarmonyLib;using UnityEngine;using AbyssalCrab.Runtime;using HowToFishTrainer.Runtime;
static class SafetyFixture {
 static int n;static void C(bool ok,string s){if(!ok)throw new Exception(s);n++;} static void Main(){
 var harmony=new Harmony();Harmony.Fail="Move";MovementSafetyRuntime.Install(harmony,"Attachments:get_Damage");
 foreach(var p in Harmony.all.Values)C(p.Postfixes.Count==0,"partial install must roll back");
 Harmony.Fail=null;MovementSafetyRuntime.Install(harmony,"Attachments:get_Damage");C(Harmony.all.Count==3,"three hooks discovered");foreach(var p in Harmony.all.Values)C(p.Postfixes.Count==1,"three hooks installed exactly once");MovementSafetyRuntime.Install(harmony,"Attachments:get_Damage");foreach(var p in Harmony.all.Values)C(p.Postfixes.Count==1,"idempotent install");
 var bridge=new TrainerBridgeHost();Bootstrap.Set(bridge);var player=new Player{Holding=new(),Movement=new(),ToolMovement=new()};Player.LocalPlayer=player;var h=player.Holding;var m=player.Movement;var tool=player.ToolMovement;
 void Bob(bool stable,string why){h.SetBob(new(2,3,4));tool.SetBob(new(2,3));MovementSafetyRuntime.AfterHeldItemBob(h);MovementSafetyRuntime.AfterToolBob(tool);C(h.Bob.x==(stable?0:2)&&h.Bob.y==(stable?0:3)&&tool.BobPos.x==(stable?0:2)&&tool.BobPos.y==(stable?0:3),why);C(tool.Tilt==3&&tool.Recoil.y==2,"tilt and recoil retained "+why);}
 h.HeldItem=new Map();m.Sprinting=false;Bob(false,"native 1x walk");m.Sprinting=true;Bob(false,"native 1x sprint no longer uses old map threshold");
 bridge.moveMultiplier=1.25f;m.Sprinting=false;Bob(true,"enhanced walk map");h.HeldItem=new Item();Bob(true,"enhanced walk other item");m.Sprinting=true;Bob(false,"walk modifier does not affect native sprint");
 bridge.moveMultiplier=1;bridge.sprintMultiplier=2;m.Sprinting=false;Bob(false,"sprint modifier does not affect native walk");m.Sprinting=true;Bob(true,"enhanced sprint");
 bridge.sprintEnabled=false;Bob(false,"disabled lock ignored");bridge.sprintEnabled=true;
 foreach(float multiplier in new[]{.25f,1f,1.0001f,1.25f,5f,50f}){bridge.sprintMultiplier=multiplier;Bob(multiplier>1,"sprint boundary "+multiplier);}
 var remoteH=new PlayerHolding{HeldItem=new Map()};remoteH.SetBob(new(2,3,4));MovementSafetyRuntime.AfterHeldItemBob(remoteH);C(remoteH.Bob.x==2,"remote item untouched");var remoteTool=new PlayerToolMovement();remoteTool.SetBob(new(2,3));MovementSafetyRuntime.AfterToolBob(remoteTool);C(remoteTool.BobPos.x==2,"remote tool untouched");
 bridge.State(false,true,false);Bob(false,"restore requested");bridge.State(false,false,true);Bob(false,"channel rejected");bridge.State(false,false,false);Bootstrap.Set(null);Bob(false,"bridge disconnected");Bootstrap.Set(bridge);
 Physics.casts=0;m.Rig.linearVelocity=new(12,3,0);MovementSafetyRuntime.AfterMovement(m);C(Physics.casts==0&&m.Rig.linearVelocity.x==12&&m.Rig.linearVelocity.y==3,"threshold velocity skips probe unchanged");
 m.Rig.linearVelocity=new(40,3,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==12&&m.Rig.linearVelocity.y==3,"grounded uphill limited, vertical retained");
 C(Physics.casts==1,"high speed still probes ground");
 m.SetJump(true);m.Rig.linearVelocity=new(40,7,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40&&m.Rig.linearVelocity.y==7,"jump untouched");m.SetJump(false);
 m.Grounded=false;m.Rig.linearVelocity=new(40,7,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40,"air untouched");m.Grounded=true;
 m.OnBoat=true;m.Rig.linearVelocity=new(40,0,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40,"boat untouched");m.OnBoat=false;
 bridge.Stop();m.Sprinting=true;Bob(false,"stopped bridge leaves bob native");m.Rig.linearVelocity=new(40,0,0);MovementSafetyRuntime.AfterMovement(m);C(m.Rig.linearVelocity.x==40,"stopped bridge leaves movement native");
 Console.WriteLine("Held motion and retained slope fixture: "+n+" checks passed"); }
}
