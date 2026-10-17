// Historical first lead implementation, retained for before/after reference only.
// Current build compiles AimAssistOptimizedRuntime.cs with the same public helper type.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace AbyssalCrab.Runtime
{
    public static class PredictiveAimRuntime
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static bool _leadEnabled, _headLockEnabled;
        private static TrainerBridgeHost _selectionHost;
        private static object _selectedPlayer, _silentHeadTarget;
        private static readonly Dictionary<int, AimMotionTrack> Motion = new Dictionary<int, AimMotionTrack>();
        private static readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();
        private static JObject _last = new JObject();
        private static int _predictedFrames, _headFrames, _silentShots, _hitscanFrames;

        private static object Read(object target, string name)
        {
            if (target == null) return null;
            Type type = target as Type ?? target.GetType();
            string key = type.AssemblyQualifiedName + "/" + name;
            MemberInfo member;
            if (!Members.TryGetValue(key, out member))
            {
                member = (MemberInfo)AccessTools.Property(type, name) ?? AccessTools.Field(type, name);
                Members[key] = member;
            }
            var property = member as PropertyInfo;
            if (property != null) return property.GetValue(target is Type ? null : target, null);
            var field = member as FieldInfo;
            return field == null ? null : field.GetValue(target is Type ? null : target);
        }

        private static object Call(object target, string name, params object[] arguments)
        {
            if (target == null) return null;
            Type type = target as Type ?? target.GetType();
            string key = type.AssemblyQualifiedName + "/call/" + name + "/" + arguments.Length;
            MemberInfo member;
            if (!Members.TryGetValue(key, out member))
            {
                for (Type current = type; current != null && member == null; current = current.BaseType)
                    foreach (MethodInfo method in current.GetMethods(Flags | BindingFlags.DeclaredOnly))
                        if (method.Name == name && method.GetParameters().Length == arguments.Length) { member = method; break; }
                Members[key] = member;
            }
            var info = member as MethodInfo;
            return info == null ? null : info.Invoke(target is Type ? null : target, arguments);
        }

        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool FiniteVector(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Live(object value) { var component = value as Component; return component != null && component.gameObject.activeInHierarchy; }
        private static object LocalPlayer() => Read(AccessTools.TypeByName("Player"), "LocalPlayer");
        private static IDictionary Items() => Read(AccessTools.TypeByName("ItemManager"), "Items") as IDictionary;

        public static void Validate(string id, JObject args)
        {
            if (id != "combat.aim_assist") return;
            foreach (string key in new[] { "leadEnabled", "headLockEnabled" })
                if (args[key] != null && args[key].Type != JTokenType.Boolean) throw new ArgumentException(key + " 必须是布尔值");
        }

        public static JToken Configure(JToken result, string id, JObject args)
        {
            if (id == "combat.aim_assist")
            {
                _leadEnabled = args.Value<bool?>("leadEnabled") ?? false;
                _headLockEnabled = args.Value<bool?>("headLockEnabled") ?? false;
                Motion.Clear(); _last = new JObject { ["state"] = "configured" };
            }
            return result;
        }

        public static void Reset(TrainerBridgeHost host)
        {
            Motion.Clear(); _selectionHost = null; _selectedPlayer = null; _silentHeadTarget = null;
            _last = new JObject { ["state"] = "off" };
        }

        public static void BeginSelection(TrainerBridgeHost host) { _selectionHost = host; _selectedPlayer = null; }
        public static Vector3 RememberPlayer(Vector3 point, TrainerBridgeHost host, object player)
        { _selectionHost = host; _selectedPlayer = player; return point; }

        private static bool Weapon(out object player, out object weapon, out object info, out Vector3 muzzle, out float speed, out float gravity, out bool hitscan)
        {
            player = LocalPlayer(); weapon = info = null; muzzle = default(Vector3); speed = gravity = 0; hitscan = false;
            if (!Live(player)) return false;
            object held = Read(Read(player, "Holding"), "HeldItem"); weapon = Read(held, "Weapon");
            if (!Live(weapon)) return false;
            object attachments = Read(weapon, "_attachments");
            var firePoint = Read(attachments, "FirePoint") as Transform;
            if (firePoint == null) return false;
            muzzle = firePoint.position; info = Read(weapon, "_weaponInfo");
            var camera = Read(Read(player, "Camera"), "CamTransform") as Transform;
            object aim = Read(weapon, "_aimPercent");
            if (Equals(Read(attachments, "UseSniperUi"), true) && aim != null && Convert.ToSingle(aim) > .9f && camera != null)
                muzzle = camera.position + camera.forward * Convert.ToSingle(Read(weapon, "_modelForwardLength"));
            object value = Call(weapon, "GetProjectileSpeed");
            if (value == null) return false;
            speed = Convert.ToSingle(value); gravity = Convert.ToSingle(Read(info, "ProjectileGravity") ?? 0f);
            object manager = Read(AccessTools.TypeByName("ProjectileManager"), "Instance");
            object projectileType = Read(attachments, "ProjectileType") ?? Read(info, "ProjectileType");
            if (manager == null || projectileType == null) return false;
            object definition = Call(manager, "GetType", Convert.ToByte(projectileType));
            object nativeHitscan = Read(definition, "IsHitScan");
            if (!(nativeHitscan is bool)) return false;
            hitscan = (bool)nativeHitscan;
            return FiniteVector(muzzle) && Finite(gravity) && (hitscan || Finite(speed) && speed > .01f);
        }

        private static bool InputReady(object player, object weapon, string trigger)
        {
            return Equals(Read(player, "BlockInputs"), false) && Equals(Read(Read(player,"Camera"),"MouseLocked"),true)
                && Equals(Read(Read(player,"Dying"),"IsDead"),false) && (trigger != "ads" || Equals(Read(weapon,"IsAds"),true));
        }

        private static object Settings(TrainerBridgeHost host) => Read(host,"_silentAimSettings");
        private static bool Clear(object assist, Vector3 origin, Vector3 point) => Equals(Call(assist,"IsTargetObstructed",origin,point),false);

        private static Vector3 Velocity(object target, Vector3 point)
        {
            var component = target as Component;
            if (component == null) return Vector3.zero;
            var rig = Read(target,"Rig") as Rigidbody;
            if (rig != null && !rig.isKinematic)
            {
                Vector3 value = rig.GetPointVelocity(point);
                if (FiniteVector(value) && value.sqrMagnitude <= 40000) return value;
                return Vector3.zero;
            }
            int key = component.GetInstanceID(); AimMotionTrack sample;
            if (!Motion.TryGetValue(key,out sample) || sample.Target != component)
            {
                if (Motion.Count >= 64) Motion.Clear();
                sample = new AimMotionTrack { Target=component }; Motion[key]=sample;
            }
            float now = Time.unscaledTime;
            if (!sample.Initialized) { sample.Initialized=true; sample.Position=point; sample.SampleTime=now; return Vector3.zero; }
            float dt = now-sample.SampleTime;
            if (dt < .025f) return sample.Velocity;
            Vector3 change=point-sample.Position; sample.Position=point; sample.SampleTime=now;
            if (dt>.5f || change.sqrMagnitude>100f) { sample.Velocity=Vector3.zero; return Vector3.zero; }
            Vector3 measured=change/dt;
            if (!FiniteVector(measured) || measured.sqrMagnitude>40000) measured=Vector3.zero;
            sample.Velocity=Vector3.Lerp(sample.Velocity,measured,1f-Mathf.Exp(-20f*dt));
            return sample.Velocity;
        }

        private static bool Predict(object target, Vector3 point, Vector3 muzzle, float speed, float gravity, bool hitscan, object assist, out Vector3 launch)
        {
            launch = point-muzzle;
            if (!_leadEnabled || hitscan)
            { if(hitscan)_hitscanFrames++;_last = new JObject { ["state"]=hitscan?"hitscan":"direct",["speed"]=speed,["headLock"]=_headLockEnabled }; return true; }
            Vector3 velocity=Velocity(target,point);
            NVector solved; double t;
            if (!PredictiveBallistics.TryIntercept(new NVector(launch.x,launch.y,launch.z),new NVector(velocity.x,velocity.y,velocity.z),speed,gravity,Time.fixedDeltaTime,8,out solved,out t))
            { _last = new JObject { ["state"]="no-intercept",["speed"]=speed }; return false; }
            Vector3 future=point+velocity*(float)t;
            if (!Clear(assist,muzzle,future)) { _last=new JObject { ["state"]="predicted-path-blocked" }; return false; }
            launch=new Vector3(solved.X,solved.Y,solved.Z);
            _predictedFrames++;
            _last=new JObject { ["state"]="predicted",["speed"]=speed,["gravity"]=gravity,["flightTime"]=t,
                ["distance"]=(point-muzzle).magnitude,["targetVelocity"]=VectorJson(velocity),
                ["leadOffset"]=VectorJson(velocity*(float)t),["dropCompensation"]=PredictiveBallistics.Drop(gravity,t,Time.fixedDeltaTime),["headLock"]=_headLockEnabled,
                ["target"]=(target as Component)?.name };
            return FiniteVector(launch);
        }

        private static JObject VectorJson(Vector3 v) => new JObject { ["x"]=v.x,["y"]=v.y,["z"]=v.z };

        private static Collider[] Colliders(object creature)
        {
            var colliders=Read(creature,"_worldColliders") as Collider[];
            return colliders != null && colliders.Length>0 ? colliders : (creature as Component).GetComponentsInChildren<Collider>();
        }

        // The actual game's headshot rule is local hit z > Creature.HeadPos, not a guessed
        // humanoid head bone. Derive a point inside a real non-trigger collider in that region.
        public static bool HeadPoint(object creature, Vector3 origin, out Vector3 point)
        {
            point=default(Vector3);
            var component=creature as Component;
            if(component==null || !component.gameObject.activeInHierarchy || !Equals(Read(creature,"IsDead"),false)) return false;
            object thresholdValue=Read(creature,"HeadPos"); if(thresholdValue==null) return false;
            float threshold=Convert.ToSingle(thresholdValue); if(!Finite(threshold)) return false;
            var colliders=Colliders(creature); bool found=false; float bestZ=float.NegativeInfinity;
            foreach(var collider in colliders)
            {
                if(collider==null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                Bounds bounds=collider.bounds; float minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;
                for(int mask=0;mask<8;mask++)
                {
                    Vector3 corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));
                    float z=component.transform.InverseTransformPoint(corner).z; minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
                }
                if(maxZ<=threshold+.001f) continue;
                Vector3 local=component.transform.InverseTransformPoint(bounds.center);
                local.z=(Math.Max(threshold,minZ)+maxZ)*.5f;
                Vector3 candidate=collider.ClosestPoint(component.transform.TransformPoint(local));
                float candidateZ=component.transform.InverseTransformPoint(candidate).z;
                if(!FiniteVector(candidate) || candidateZ<=threshold+.0001f || candidateZ<=bestZ) continue;
                Vector3 difference=candidate-origin;float distance=difference.magnitude;
                if(distance<.01f)continue;
                float first=float.PositiveInfinity;Vector3 hitPoint=default(Vector3);bool hit=false;
                Ray ray=new Ray(origin,difference/distance);
                foreach(var physical in colliders)
                {
                    if(physical==null || !physical.enabled || physical.isTrigger || !physical.gameObject.activeInHierarchy)continue;
                    RaycastHit result;
                    if(physical.Raycast(ray,out result,distance+.05f) && result.distance<first) {first=result.distance;hitPoint=result.point;hit=true;}
                }
                if(!hit || component.transform.InverseTransformPoint(hitPoint).z<=threshold+.0001f)continue;
                point=candidate;bestZ=candidateZ;found=true;
            }
            return found;
        }

        private static bool PickHead(TrainerBridgeHost host,object assist,Vector3 origin,Vector3 euler,Vector3 muzzle,out object selected,out Vector3 point)
        {
            selected=null;point=default(Vector3);var items=Items();if(items==null)return false;
            object settings=Settings(host);float range=Convert.ToSingle(Read(settings,"MaximumDistance")??35f),angle=Convert.ToSingle(Read(settings,"AcquireAngle")??8f);
            Vector3 forward=Quaternion.Euler(euler)*Vector3.forward;float best=float.PositiveInfinity;
            foreach(object item in items.Values)
            {
                object creature=Read(item,"Creature");Vector3 candidate;
                if(!Live(creature) || !HeadPoint(creature,muzzle,out candidate))continue;
                Vector3 direction=candidate-origin;float distance=direction.magnitude,offset=Vector3.Angle(forward,direction);
                if(distance>range || offset>angle || !Clear(assist,origin,candidate) || !Clear(assist,muzzle,candidate))continue;
                float score=offset+distance/Math.Max(1,range)*.01f;
                if(score>=best)continue;
                best=score;selected=creature;point=candidate;
            }
            return selected!=null;
        }

        public static void Apply(TrainerBridgeHost host,object assist,Vector3 origin,Vector3 euler,float manualLook,ref Vector2 delta)
        {
            if(host==null || !host.ShouldForceAimAssist(assist) || (!_leadEnabled && !_headLockEnabled))return;
            try
            {
                object player,weapon,info;Vector3 muzzle;float speed,gravity;bool hitscan;
                if(!Weapon(out player,out weapon,out info,out muzzle,out speed,out gravity,out hitscan)) { _last=new JObject{["state"]="waiting-for-weapon"};return; }
                string trigger=Read(Settings(host),"TriggerMode") as string??"ads";
                if(!InputReady(player,weapon,trigger)) {delta=Vector2.zero;_last=new JObject{["state"]="input-blocked"};return;}
                object target;Vector3 point;
                if(_headLockEnabled)
                {
                    if(!PickHead(host,assist,origin,euler,muzzle,out target,out point)) {delta=Vector2.zero;_last=new JObject{["state"]="no-visible-head"};return;}
                    _headFrames++;
                }
                else if(ReferenceEquals(_selectionHost,host) && _selectedPlayer!=null)
                {
                    target=_selectedPlayer;object[] args={target,default(Vector3)};
                    var method=typeof(TrainerBridgeHost).GetMethod("TryPlayerAimPoint",Flags);
                    if(!Equals(method.Invoke(host,args),true))return;point=(Vector3)args[1];
                }
                else
                {
                    target=Read(assist,"_target"); if(!Live(target) || !Equals(Call(assist,"IsAliveCreature",target),true))return;
                    object value=Call(assist,"GetTargetPosition",target);if(!(value is Vector3))return;point=(Vector3)value;
                }
                if(!Clear(assist,origin,point) || !Clear(assist,muzzle,point))return;
                Vector3 launch; bool predicted=Predict(target,point,muzzle,speed,gravity,hitscan,assist,out launch);
                if(!predicted && !_headLockEnabled)return;
                if(!predicted)launch=point-muzzle;
                Vector3 desired=Quaternion.LookRotation(launch,Vector3.up).eulerAngles;
                Vector2 correction=new Vector2(Mathf.DeltaAngle(euler.x,desired.x),Mathf.DeltaAngle(euler.y,desired.y));
                float sharpness=Convert.ToSingle(Read(assist,"_trackingSharpness")),maxSpeed=Convert.ToSingle(Read(assist,"_maxRotationSpeed"));
                delta=Vector2.ClampMagnitude(correction*(1f-Mathf.Exp(-sharpness*Time.deltaTime))*(1f-Mathf.Clamp01(manualLook)),maxSpeed*Time.deltaTime);
            }
            catch(Exception e) { _last=new JObject{["state"]="fallback",["reason"]=e.GetType().Name+": "+e.Message}; }
        }

        public static bool SelectHeadSilent(TrainerBridgeHost host,object assist,Transform view,Vector3 muzzle,out string scope,out Vector3? result)
        {
            scope="creature-head";result=null;
            if(!_headLockEnabled)return false;
            object target;Vector3 point;
            if(PickHead(host,assist,view.position,view.eulerAngles,muzzle,out target,out point)) { _silentHeadTarget=target;result=point; }
            else _silentHeadTarget=null;
            return true;
        }

        public static Vector3 AdjustSilent(Vector3 point,TrainerBridgeHost host,object owner,Vector3 muzzle,Vector3[] velocities)
        {
            if(!_leadEnabled)return point;
            try
            {
                object player,weapon,info;Vector3 ignored;float speed,gravity;bool hitscan;
                if(!Weapon(out player,out weapon,out info,out ignored,out speed,out gravity,out hitscan) || hitscan || velocities==null || velocities.Length==0)return point;
                object assist=Read(owner,"AimAssist");object target=_headLockEnabled?_silentHeadTarget:null;
                if(target==null)
                {
                    var items=Items();if(items!=null) foreach(object item in items.Values)
                    {
                        object candidate=Read(item,"Creature");if(!Live(candidate) || !Equals(Call(assist,"IsAliveCreature",candidate),true))continue;
                        object p=Call(assist,"GetTargetPosition",candidate);
                        if(p is Vector3 && ((Vector3)p-point).sqrMagnitude<.00001f){target=candidate;break;}
                    }
                }
                if(target==null)return point;
                Vector3 launch;if(Predict(target,point,muzzle,velocities[0].magnitude,gravity,false,assist,out launch)){_silentShots++;return muzzle+launch;}
            }
            catch(Exception e){_last=new JObject{["state"]="silent-fallback",["reason"]=e.GetType().Name};}
            return point;
        }

        public static JToken Describe(JToken session)
        {
            var obj=session as JObject;if(obj==null)return session;
            var diagnostic=new JObject{["leadEnabled"]=_leadEnabled,["headLockEnabled"]=_headLockEnabled,["last"]=_last.DeepClone(),
                ["predictedFrames"]=_predictedFrames,["headFrames"]=_headFrames,["silentShots"]=_silentShots,["hitscanFrames"]=_hitscanFrames};
            try
            {
                var host=Read(typeof(Bootstrap),"_bridge") as TrainerBridgeHost;
                object player,weapon,info;Vector3 muzzle;float speed,gravity;bool hitscan;
                if(Weapon(out player,out weapon,out info,out muzzle,out speed,out gravity,out hitscan))
                {
                    diagnostic["weapon"]=new JObject{["name"]=(weapon as Component).name,["speed"]=speed,["gravity"]=gravity,["hitscan"]=hitscan,["muzzle"]=VectorJson(muzzle)};
                    var waves=Read(host,"_funWaves") as IDictionary;var counts=new JArray();
                    if(waves!=null)foreach(object wave in waves.Values)
                    {
                        if(!Equals(Read(wave,"EventId"),"seagull-migration"))continue;
                        int alive=0,heads=0;float largestMargin=float.NegativeInfinity;
                        var entities=Read(wave,"Entities") as IEnumerable;
                        if(entities!=null)foreach(object entity in entities)
                        {
                            object creature=Read(entity,"Creature");if(!Live(creature)||!Equals(Read(creature,"IsDead"),false))continue;
                            alive++;Vector3 point;
                            if(HeadPoint(creature,muzzle,out point)){heads++;largestMargin=Math.Max(largestMargin,(creature as Component).transform.InverseTransformPoint(point).z-Convert.ToSingle(Read(creature,"HeadPos")));}
                        }
                        counts.Add(new JObject{["waveId"]=(string)Read(wave,"Id"),["alive"]=alive,["visibleHeadRegions"]=heads,["headRegionMargin"]=heads>0?largestMargin:0});
                    }
                    diagnostic["seagullWaves"]=counts;
                }
            }
            catch(Exception e){diagnostic["weaponUnavailable"]=e.GetType().Name;}
            obj["predictiveAim"]=diagnostic;return session;
        }
    }
}
