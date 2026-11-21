// Runs the candidate projectile owner against a small fake engine, never a game process.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.Json;
using UnityEngine;
using AbyssalCrab.Runtime;
namespace UnityEngine {
 public class Object { public bool Destroyed; public static implicit operator bool(Object o)=>o!=null&&!o.Destroyed; }
 public class Component:Object { public Vector3 Position; public bool Dead; }
 public class Transform:Component { public Vector3 position,forward=new(0,0,1); public Component Target; }
 public class Collider:Component { public Transform transform=new(); }
 public class Rigidbody:Component {public bool isKinematic;public Vector3 Velocity;public Vector3 GetPointVelocity(Vector3 p)=>Velocity;}
 public static class Mathf {public static float Exp(float f)=>MathF.Exp(f);}
 public struct RaycastHit { public Vector3 point; public Collider collider; public Transform transform=>collider.transform; }
 public struct Vector3 {
  public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 zero=>new();public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>MathF.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude==0?zero:this*(1/magnitude);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*t;
  public static Vector3 RotateTowards(Vector3 from,Vector3 to,float angle,float speed)=>to;
 }
 public static class Time { public static float unscaledTime;public static float fixedDeltaTime=.02f; }
 public enum QueryTriggerInteraction { Ignore,Collide }
 public static class Physics { public static Collider[] Hits=Array.Empty<Collider>();public static int Queries;public static Vector3 LastEnd;public static QueryTriggerInteraction Mode;
  public static int OverlapCapsuleNonAlloc(Vector3 from,Vector3 to,float radius,Collider[] buffer,int mask,QueryTriggerInteraction mode){Queries++;LastEnd=to;Mode=mode;int n=Math.Min(Hits.Length,buffer.Length);Array.Copy(Hits,buffer,n);return n;}
 }
 public static class Debug { public static void LogWarning(string s){throw new Exception(s);} }
}
namespace HarmonyLib { public class Harmony {} public static class AccessTools {public delegate ref F FieldRef<T,F>(T obj);} }
namespace FishNet.Object.Synchronizing {public class SyncVar<T>{public T Value;}}
namespace HowToFishTrainer.Runtime {public class TrainerBridgeHost {public bool Enabled=true;public bool IsLockEnabled(string id)=>Enabled;}}
public class Player:Component {public static Player LocalPlayer;public bool IsDeinitializing,BlockInputs; public PlayerDying Dying=new(); public PlayerCamera Camera=new();public PlayerHolding Holding=new();public PlayerVitals Vitals=new();}
public class PlayerDying:UnityEngine.Object {public bool IsDead;public UnityEngine.Object DeadPlayer;public int Deaths;public void ServerDie(Vector3 v){Deaths++;DeadPlayer=new();}}
public class PlayerCamera:UnityEngine.Object {public bool MouseLocked=true;public Transform CamTransform=new();}
public class PlayerHolding:UnityEngine.Object {public Item HeldItem=new(){Weapon=new()};}
public class PlayerVitals {public FishNet.Object.Synchronizing.SyncVar<int> Hp=new(){Value=100};}
public class PlayerMovement {}
public class Weapon:UnityEngine.Object {public bool IsAds=true;}
public class WeaponInfo {public Weapon Weapon;public byte ProjectileType;}
public class Item:UnityEngine.Object {public Weapon Weapon;public Creature Creature;}
public class Creature:Component {public FishNet.Object.Synchronizing.SyncVar<int> Hp=new(){Value=100};}
public class Projectile {public bool IsLocal=true,FromNpc;public Player Owner;public uint Id,CatchingUpToDo;public Vector3 Position,Velocity=new(0,0,10);public float GravityForce;public int Damage=10;}
public class ProjectileType {public bool IsHitScan;public List<Projectile> Projectiles=new(),ProjectilesToRemove=new();}
public class ProjectileManager:UnityEngine.Object {public static ProjectileManager Instance=new();public uint Next;public float CatchUp=1;public ProjectileType Type=new();public ProjectileType GetType(byte id)=>Type;}
public static class IslandManager {public static bool IsLoading;}
public static class ItemManager {public static Item Get(Collider c)=>c.transform.Target is Creature creature?new Item{Creature=creature}:null;}
public static class PlayerManager {public static Player GetPlayerFromBodyPart(Transform t)=>t.Target as Player;}
public class Server:UnityEngine.Object {public static Server Instance=new();public bool IsServerInitialized=true;public Dictionary<Component,int> Damage=new();public void HitCreature(Creature c,Player p,int n,Vector3 at,Vector3 dir)=>Add(c,n);public void HitPlayer(Player c,int n,Vector3 f,Vector3 at,byte type,Player p)=>Add(c,n);void Add(Component c,int n){Damage[c]=Damage.GetValueOrDefault(c)+n;}}
namespace AbyssalCrab.Runtime {
 public class AimTargetTrack {public Rigidbody Rig;public Transform Transform;public bool SampleReady;public Vector3 SamplePosition,SampleVelocity;public float SampleTime;}
 public static class GameplayTargeting {
  public static Component Next;public static int Scans;public static bool Teammates,Visible=true;
  public static bool Point(Component t,out Vector3 point){point=t?t.Position:default;return t&&!t.Dead&&t!=Player.LocalPlayer&&(!(t is Player)||Teammates);}
  public static bool AimPoint(Component t,Vector3 origin,bool head,out Vector3 p)=>Point(t,out p);
  public static bool Clear(Vector3 a,Vector3 b)=>Visible;
  public static Component Select(Vector3 a,Vector3 b,Vector3 c,float d,float angle,bool head,out Vector3 p){Scans++;return Point(Next,out p)?Next:null;}
  public static Vector3 LaunchDirection(Component t,Vector3 p,Vector3 origin,Vector3 velocity,float gravity,bool hitScan)=>p-origin;
 }
 public static class GameplayPolicy { public static bool Trigger(string mode,bool ads)=>mode=="fire"||ads;public static float TurnRadians(float a,float b)=>1; }
}
static class ProjectileR29Verification {
 static readonly List<string> Checks=new();static Type Owner=typeof(GameplayRuntime);
 static object Invoke(string method,params object[] args)=>Owner.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);
 static void Set(string name,object value)=>Owner.GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);
 static T Field<T>(string name)=>(T)Owner.GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static void Check(bool pass,string message){if(!pass)throw new Exception(message);Checks.Add(message);}
 static Collider Col(Component target)=>new(){transform=new(){Target=target}};
 static void Main(string[] args) {
  var host=new HowToFishTrainer.Runtime.TrainerBridgeHost();var local=new Player();Player.LocalPlayer=local;
  Set("_host",host);Set("_readyGroups",4);
  Set("_nextId",(HarmonyLib.AccessTools.FieldRef<ProjectileManager,uint>)((ProjectileManager p)=>ref p.Next));
  Set("_catchUpSpeed",(HarmonyLib.AccessTools.FieldRef<ProjectileManager,float>)((ProjectileManager p)=>ref p.CatchUp));
  Set("_creatureHp",(HarmonyLib.AccessTools.FieldRef<Creature,FishNet.Object.Synchronizing.SyncVar<int>>)((Creature c)=>ref c.Hp));
  Set("_playerHp",(HarmonyLib.AccessTools.FieldRef<PlayerVitals,FishNet.Object.Synchronizing.SyncVar<int>>)((PlayerVitals p)=>ref p.Hp));
  var a=new Creature{Position=new(10,0,5)};var b=new Creature{Position=new(-10,0,5)};var replacement=new Creature{Position=new(0,0,5)};
  var config=new GameplayLaunch{Target=a,Distance=100,Angle=180,Strength=100,Splash=true,Wide=true,SplashRadius=2,WidthRadius=.5f};
  var first=new Projectile{Owner=local,Id=1};var second=new Projectile{Owner=local,Id=2};
  var shots=Field<Dictionary<Projectile,GameplayFlight>>("Shots");
  var fa=(GameplayFlight)Invoke("NewFlight",first,config);var fb=(GameplayFlight)Invoke("NewFlight",second,config);fb.Target=b;
  shots.Add(first,fa);shots.Add(second,fb);GameplayTargeting.Next=replacement;
  GameplayRuntime.TrackProjectile(first);GameplayRuntime.TrackProjectile(second);
  Check(fa.Target==a&&fb.Target==b&&config.Target==a&&GameplayTargeting.Scans==0,"Two projectiles keep independent valid targets without reselection");
  Check(first.Velocity.x>0&&second.Velocity.x<0,"Each projectile steers towards its own target");
  a.Dead=true;GameplayRuntime.TrackProjectile(first);Check(fa.Target==replacement&&fb.Target==b&&GameplayTargeting.Scans==1,"Only the projectile whose target died reacquires");
  var friend=new Player{Position=new(1,0,1)};fa.Target=friend;GameplayTargeting.Teammates=false;GameplayRuntime.TrackProjectile(first);Check(fa.Target==replacement,"Unchecking teammate targeting invalidates an in-flight teammate lock");
  GameplayTargeting.Teammates=true;fa.Target=friend;GameplayRuntime.TrackProjectile(first);Check(fa.Target==friend,"Checked teammate targeting preserves valid teammate lock");
  GameplayTargeting.Teammates=false;Physics.Hits=new[]{Col(b),Col(b),Col(friend),Col(local)};
  var hit=new RaycastHit{point=new(0,0,5),collider=Col(replacement)};
  GameplayRuntime.BeforeProjectileHit(first,hit,out var state);GameplayRuntime.AfterProjectileHit(first,state);
  ProjectileManager.Instance.Type.ProjectilesToRemove.Add(first);GameplayRuntime.AfterProjectileScan(first,ProjectileManager.Instance.Type,state);
  Check(Server.Instance.Damage.GetValueOrDefault(b)==10&&!Server.Instance.Damage.ContainsKey(friend)&&!Server.Instance.Damage.ContainsKey(local),"Splash plus wide path hit each eligible entity once and exclude self and unchecked teammates");
  Check(!Server.Instance.Damage.ContainsKey(replacement),"Native direct hit is not appended a second time");
  Check(!shots.ContainsKey(first),"Removed native projectile releases its tracking state");
  Check(Physics.Mode==QueryTriggerInteraction.Collide,"Boss trigger hitboxes participate in radius queries");
  var lethal=new Projectile{Owner=local,Id=3};var fl=(GameplayFlight)Invoke("NewFlight",lethal,new GameplayLaunch{Lethal=true,Splash=true,SplashRadius=2});shots.Add(lethal,fl);
  var victim=new Creature();Physics.Hits=new[]{Col(victim),Col(friend),Col(local)};GameplayTargeting.Visible=false;GameplayTargeting.Teammates=true;
  GameplayRuntime.BeforeProjectileHit(lethal,hit,out state);GameplayRuntime.AfterProjectileHit(lethal,state);
  Check(victim.Hp.Value==0&&friend.Vitals.Hp.Value==0&&friend.Dying.Deaths==1&&local.Vitals.Hp.Value==100,"Lethal radius ignores sight checks, uses server health, and never kills self");
  GameplayRuntime.AfterProjectileHit(lethal,state);Check(friend.Dying.Deaths==1,"Repeated impact callback does not repeat player death");
  var clientVictim=new Creature();Physics.Hits=new[]{Col(clientVictim)};Server.Instance.IsServerInitialized=false;GameplayRuntime.AfterProjectileHit(lethal,state);
  Check(clientVictim.Hp.Value==100,"Client cannot report a fake host-authoritative lethal result");Server.Instance.IsServerInitialized=true;
  var many=Enumerable.Range(0,257).Select(_=>new Creature()).ToArray();Physics.Hits=many.Select(Col).ToArray();GameplayRuntime.AfterProjectileHit(lethal,state);
  Check(many.All(c=>c.Hp.Value==0),"A full overlap buffer grows instead of dropping entities");
  var manager=ProjectileManager.Instance;var wi=new WeaponInfo{Weapon=local.Holding.HeldItem.Weapon};manager.Next=10;GameplayTargeting.Visible=true;
  GameplayRuntime.BeforeSingle(manager,local,wi,true,false,default,out var outer);var launch=Field<GameplayLaunch>("_launch");
  manager.Next=11;var instant=new Projectile{Owner=local,Id=10};GameplayRuntime.BeforeHitscan(instant,out var instantState);
  Check(instantState!=null&&instantState.Id==10,"Hitscan obtains projectile state before launch postfix");
  GameplayRuntime.BeforeSingle(manager,local,wi,true,true,default,out var nested);Check(Field<GameplayLaunch>("_launch")==null,"Unowned nested launch has no magic state");
  GameplayRuntime.EndLaunch(null,nested);Check(ReferenceEquals(Field<GameplayLaunch>("_launch"),launch),"Nested launch restores outer scope");
  var error=new Exception("native launch failure");Check(GameplayRuntime.EndLaunch(error,outer)==error&&Field<GameplayLaunch>("_launch")==null,"Launch finalizer restores scope and preserves original exception");
  GameplayRuntime.AfterHitscan(instant,5,instantState);manager.Type.IsHitScan=true;manager.Type.Projectiles.Add(instant);
  Invoke("RegisterLaunch",manager,local,wi,launch);Check(!shots.ContainsKey(instant),"Launch postfix does not resurrect a finished hitscan state");
  int oldQueries=Physics.Queries;host.Enabled=false;GameplayRuntime.AfterProjectileHit(lethal,fl);Check(Physics.Queries==oldQueries,"Disable stops area damage for already-fired bullets");
  GameplayRuntime.TrackProjectile(second);Check(shots.Count==0,"Disable clears all projectile locks");
  var motion=new AimTargetTrack{Transform=new()};var otherMotion=new AimTargetTrack{Transform=new()};
  var velocity=typeof(PredictiveAimRuntime).GetMethod("Velocity",BindingFlags.Static|BindingFlags.NonPublic);
  Time.unscaledTime=1;velocity.Invoke(null,new object[]{motion,new Vector3(0,1,0)});velocity.Invoke(null,new object[]{otherMotion,Vector3.zero});
  Time.unscaledTime=1.1f;var v=(Vector3)velocity.Invoke(null,new object[]{motion,new Vector3(0,2,0)});
  Check(v.sqrMagnitude==0,"Switching head/body hit points cannot create false target translation");
  motion.Transform.position=new(1,0,0);Time.unscaledTime=1.2f;v=(Vector3)velocity.Invoke(null,new object[]{motion,new Vector3(0,2,0)});
  Check(v.x>0&&v.y==0,"Kinematic lead follows actual target movement even if the queried hit point did not move");
  v=(Vector3)velocity.Invoke(null,new object[]{otherMotion,new Vector3(5,5,5)});Check(v.sqrMagnitude==0,"Velocity history remains separate for different entities");
  motion.Rig=new(){Velocity=new(2,3,4)};v=(Vector3)velocity.Invoke(null,new object[]{motion,Vector3.zero});Check(v.x==2&&v.y==3&&v.z==4,"Physics-driven targets still use native point velocity");
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{passed=true,checks=Checks.Count,tests=Checks,scope="Actual candidate method bodies with engine stubs; no Unity physics or native damage execution"},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Projectile simulation: "+Checks.Count+" passed");
 }
}
