using System;using System.Reflection;using System.Collections.Generic;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object o)=>o!=null;}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 zero=>new(0,0);}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero=>new(0,0,0);public static Vector3 up=>new(0,1,0);public static Vector3 down=>new(0,-1,0);public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);public static float Angle(Vector3 a,Vector3 b)=>(float)(Math.Acos(Math.Clamp((a.x*b.x+a.y*b.y+a.z*b.z)/Math.Sqrt((a.x*a.x+a.y*a.y+a.z*a.z)*(b.x*b.x+b.y*b.y+b.z*b.z)),-1,1))*180/Math.PI);}
 public class Transform:Object {} public class CapsuleCollider:Object {public float radius=.5f;public Vector3 center;}
 public class Rigidbody:Object {public Vector3 linearVelocity;}
 public struct RaycastHit {public Vector3 normal;public Transform transform;}
 public static class Physics {public static bool result=true;public static int casts;public static RaycastHit hit=new(){normal=new(-.6f,.8f,0),transform=new()};public static bool SphereCast(Vector3 a,float b,Vector3 c,out RaycastHit h,float dist,int layer){casts++;h=hit;return result;}}
 public static class Time {public static float unscaledTime=2f;} public static class Debug {public static void LogWarning(object x){}}
}
namespace HarmonyLib {
 public class HarmonyMethod {public MethodInfo method;public HarmonyMethod(MethodInfo m){method=m;}}
 public class Patch {public MethodInfo PatchMethod;} public class Patches {public List<Patch> Postfixes=new();}
 public class Harmony {public static Dictionary<MethodInfo,Patches> all=new();public static string Fail;public static Patches GetPatchInfo(MethodInfo m)=>all.TryGetValue(m,out var p)?p:null;
  public void Patch(MethodInfo m,HarmonyMethod postfix){if(!all.TryGetValue(m,out var p))all[m]=p=new();p.Postfixes.Add(new(){PatchMethod=postfix.method});if(m.Name==Fail)throw new Exception("injected error after patch");}
  public void Unpatch(MethodInfo m,MethodInfo h){if(all.TryGetValue(m,out var p))p.Postfixes.RemoveAll(x=>x.PatchMethod==h);}}
 public static class AccessTools {public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);}
}
namespace HowToFishTrainer.Runtime { public class Bootstrap {private static TrainerBridgeHost _bridge; public static void Set(TrainerBridgeHost b)=>_bridge=b;}
 public class TrainerBridgeHost {private bool _stopped,_restoreRequested,_channelRejected;public bool speed=true,moveEnabled=true,sprintEnabled=true;public float moveMultiplier=1,sprintMultiplier=1;public void Stop()=>_stopped=true;public void State(bool stopped,bool restoring,bool rejected){_stopped=stopped;_restoreRequested=restoring;_channelRejected=rejected;}public bool IsLockEnabled(string id)=>speed&&(id=="player.move.multiplier"||id=="player.sprint.multiplier");public bool TryGetLockFloat(string id,out float value){value=id=="player.sprint.multiplier"?sprintMultiplier:moveMultiplier;return speed&&(id=="player.sprint.multiplier"?sprintEnabled:moveEnabled);}} }
public class GameInfo {public static int CanJumpOnLayers=1;}
public class Item:UnityEngine.Object {} public class Map:Item {} public class Player:UnityEngine.Object {public static Player LocalPlayer; public PlayerHolding Holding;public PlayerMovement Movement;public PlayerToolMovement ToolMovement;}
public class PlayerToolMovement:UnityEngine.Object {private UnityEngine.Vector2 _bobPos;public UnityEngine.Vector2 BobPos=>_bobPos;public void SetBob(UnityEngine.Vector2 value)=>_bobPos=value;public float Tilt=3;public UnityEngine.Vector3 Recoil=new(1,2,3);private void Bob(){}}
public class PlayerHolding:UnityEngine.Object {private UnityEngine.Vector3 _bobOffset;public UnityEngine.Vector3 Bob=>_bobOffset;public Item HeldItem;public void SetBob(UnityEngine.Vector3 v)=>_bobOffset=v;private void GetBobOffset(){}}
public class PlayerMovement:UnityEngine.Object {private UnityEngine.CapsuleCollider _col=new();private UnityEngine.Rigidbody _rig=new();private bool _hasJumped,_isSwimming;private float _groundCheckDist=1f;public bool Grounded=true,OnBoat,Sprinting;public float SlipAngle=50f;public UnityEngine.Vector3 CurFeetWorldPos,Velocity;public UnityEngine.Rigidbody Rig=>_rig;public void SetJump(bool v)=>_hasJumped=v;public void SetSwim(bool v)=>_isSwimming=v;private void Move(){}}
