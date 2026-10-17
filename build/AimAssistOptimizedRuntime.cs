using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace AbyssalCrab.Runtime
{
    // Direct game references and delegates are bound once. There is no reflection, JSON creation,
    // or desktop/pipe round trip on a warmed-up tracking frame.
    public static class PredictiveAimRuntime
    {
        private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        private static TrainerBridgeHost _host;
        private static bool _enabled,_leadEnabled,_headLockEnabled,_bound,_wasAds;
        private static float _strength=60,_range=35,_angle=8,_acquireCos,_trackCos;
        private static float _nextCatalog,_nextScan,_nextProof;
        private static int _catalogCount=-1,_handledFrame=-1,_adsEdgeFrame=-1,_firstLockDelay=-1;
        private static object _handledAssist;
        private static object _nativeAssist;
        private static JObject _nativeArguments;
        private static AimTargetTrack _target,_silentTarget;
        private static readonly List<AimTargetTrack> Targets=new List<AimTargetTrack>(128);
        private static readonly List<AimTargetTrack> Candidates=new List<AimTargetTrack>(128);
        private static readonly Dictionary<Component,AimTargetTrack> Cache=new Dictionary<Component,AimTargetTrack>();
        private static readonly Comparison<AimTargetTrack> Order=new Comparison<AimTargetTrack>(CompareTargets);
        private static readonly Dictionary<Type,Dictionary<string,MemberInfo>> Members=new Dictionary<Type,Dictionary<string,MemberInfo>>();
        private static AccessTools.FieldRef<Item,Collider[]> _colliders;
        private static AccessTools.FieldRef<Weapon,WeaponInfo> _weaponInfo;
        private static AccessTools.FieldRef<Weapon,float> _aimPercent,_forwardLength;
        private static Func<Weapon,float> _projectileSpeed;
        private static Weapon _weapon;
        private static float _speed,_gravity,_flightTime,_drop,_distance,_headMargin,_selectedScanDistance;
        private static Vector3 _muzzle,_velocity,_leadOffset;
        private static bool _hitscan;
        private static bool _measureTracking;
        private static string _state="off",_failure;
        private static long _frames,_predictedFrames,_headFrames,_silentShots,_hitscanFrames,_scans,_catalogRefreshes,_headSolves,_occlusionChecks,_exceptions;
        private static long _adsEdges,_released,_lockedFrames,_outsideAdsFrames,_nativeApplySkipped;
        private static long _automaticSwitches,_trackingMeasureFrames,_trackingTicks,_trackingMaxTicks;
        private static int _itemsInspected,_creaturesFound,_nearbyFound,_aliveRejected,_lastCandidates;
        private static int _componentDisabled,_deinitializing;
        private static float _nearestCenterDistance,_bestAlignment;
        private static long _allocationProbe=-1;
        private static long _ticks,_maxTicks,_allocated,_maxAllocated,_measureStart,_allocationStart;
        private static readonly long[] Histogram=new long[10];
        private static readonly long[] TrackingHistogram=new long[10];
        private static readonly double[] Limits=new double[10];
        static PredictiveAimRuntime()
        {Limits[0]=10;Limits[1]=25;Limits[2]=50;Limits[3]=100;Limits[4]=250;Limits[5]=500;Limits[6]=1000;Limits[7]=2000;Limits[8]=5000;Limits[9]=double.PositiveInfinity;}

        private static object Read(object target,string name)
        {
            if(target==null)return null;Type type=target as Type??target.GetType();Dictionary<string,MemberInfo> cache;
            if(!Members.TryGetValue(type,out cache)){cache=new Dictionary<string,MemberInfo>();Members[type]=cache;}
            MemberInfo member;if(!cache.TryGetValue(name,out member)){member=(MemberInfo)AccessTools.Property(type,name)??AccessTools.Field(type,name);cache[name]=member;}
            var p=member as PropertyInfo;if(p!=null)return p.GetValue(target is Type?null:target,null);
            var f=member as FieldInfo;return f==null?null:f.GetValue(target is Type?null:target);
        }

        private static void Bind()
        {
            if(_bound)return;
            _colliders=AccessTools.FieldRefAccess<Item,Collider[]>("_worldColliders");
            _weaponInfo=AccessTools.FieldRefAccess<Weapon,WeaponInfo>("_weaponInfo");
            _aimPercent=AccessTools.FieldRefAccess<Weapon,float>("_aimPercent");
            _forwardLength=AccessTools.FieldRefAccess<Weapon,float>("_modelForwardLength");
            _projectileSpeed=AccessTools.MethodDelegate<Func<Weapon,float>>(AccessTools.Method(typeof(Weapon),"GetProjectileSpeed"),null,true);
            _bound=true;
        }

        public static void InstallPrefix(Harmony harmony,string target)
        {
            if(target!="PlayerAimAssist:GetRotationDelta")return;
            var method=AccessTools.Method(typeof(PlayerAimAssist),"GetRotationDelta",new[]{typeof(Vector3),typeof(Vector3),typeof(float)});
            if(method==null||method.ReturnType!=typeof(Vector2))throw new MissingMethodException("PlayerAimAssist.GetRotationDelta signature changed");
            var p=method.GetParameters();if(p[0].Name!="cameraPosition"||p[1].Name!="cameraEuler"||p[2].Name!="manualLookAmount")throw new MissingMethodException("PlayerAimAssist.GetRotationDelta parameter contract changed");
            Bind();
            harmony.Patch(method,new HarmonyMethod(typeof(PredictiveAimRuntime).GetMethod("Prefix")));
        }

        public static void Validate(string id,JObject args)
        {
            if(id!="combat.aim_assist")return;
            foreach(string key in new[]{"leadEnabled","headLockEnabled"})if(args[key]!=null&&args[key].Type!=JTokenType.Boolean)throw new ArgumentException(key+" 必须是布尔值");
        }

        public static JToken Configure(JToken result,string id,JObject args)
        {
            if(id!="combat.aim_assist")return result;
            _host=Read(typeof(Bootstrap),"_bridge") as TrainerBridgeHost;
            _enabled=args.Value<bool?>("enabled")??true;_leadEnabled=args.Value<bool?>("leadEnabled")??false;_headLockEnabled=args.Value<bool?>("headLockEnabled")??false;
            _strength=args.Value<float>("strength");_range=args.Value<float>("distance");_angle=args.Value<float>("angle");
            _acquireCos=Mathf.Cos(_angle*Mathf.Deg2Rad);_trackCos=Mathf.Cos(Mathf.Min(30f,Mathf.Max(_angle+4f,_angle*1.5f))*Mathf.Deg2Rad);
            _wasAds=false;_target=_silentTarget=null;_nextScan=_nextCatalog=0;_state=_enabled?"waiting-for-ads":"off";_failure=null;
            if(_enabled&&Player.LocalPlayer){Bind();RefreshCatalog(Time.unscaledTime,true);}
            return result;
        }

        public static void Normalize(string id,JObject args)
        {if(id=="combat.aim_assist"&&args.Value<string>("trigger")=="always")args["trigger"]="ads";}

        public static bool NativeSettingsUnchanged(JObject args)
        {
            var player=Player.LocalPlayer;
            bool same=player&&player.AimAssist&&ReferenceEquals(_nativeAssist,player.AimAssist)&&ReferenceEquals(_nativeArguments,args);
            if(same)_nativeApplySkipped++;return same;
        }
        public static void NativeSettingsApplied(JObject args)
        {var player=Player.LocalPlayer;_nativeAssist=player?player.AimAssist:null;_nativeArguments=args;}

        public static void Reset(TrainerBridgeHost host)
        {
            _enabled=false;_wasAds=false;_target=_silentTarget=null;_handledAssist=null;_handledFrame=-1;
            Targets.Clear();Candidates.Clear();Cache.Clear();_catalogCount=-1;_nextCatalog=_nextScan=0;_state="off";
            _nativeAssist=null;_nativeArguments=null;
        }

        public static void BeginSelection(TrainerBridgeHost host) { }
        public static Vector3 RememberPlayer(Vector3 point,TrainerBridgeHost host,object player)=>point;
        public static void Apply(TrainerBridgeHost host,object assist,Vector3 origin,Vector3 euler,float manualLook,ref Vector2 delta) { }

        private static bool Alive(AimTargetTrack t)
        {
            if(t==null||!t.Target||!t.Target.gameObject.activeInHierarchy)return false;
            if(t.Creature)return t.Creature.isActiveAndEnabled&&!t.Creature.IsDeinitializing&&!t.Creature.IsDead;
            return t.Player&&t.Player!=Player.LocalPlayer&&t.Player.Dying&&!t.Player.Dying.IsDead&&t.Capsule&&t.Capsule.enabled;
        }
        private static Vector3 Center(AimTargetTrack t)=>t.Creature?(t.Rig?t.Rig.worldCenterOfMass:t.Transform.position):t.Capsule.bounds.center;
        private static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
        private static bool Clear(Vector3 origin,Vector3 point)
        {_occlusionChecks++;return !Physics.Linecast(origin,point,(int)GameInfo.LevelLayer|(int)GameInfo.BoatLayer,QueryTriggerInteraction.Ignore);}
        private static int CompareTargets(AimTargetTrack a,AimTargetTrack b)=>AimLockPolicy.CompareDistance(a.DistanceSquared,b.DistanceSquared);

        private static AimTargetTrack Track(Creature creature)
        {
            AimTargetTrack t;if(!Cache.TryGetValue(creature,out t))
            {
                t=new AimTargetTrack{Target=creature,Creature=creature,Transform=creature.transform,Rig=creature.Rig,Colliders=_colliders(creature)};
                if(t.Colliders==null||t.Colliders.Length==0)t.Colliders=creature.GetComponentsInChildren<Collider>();
                Cache[creature]=t;
            }
            return t;
        }

        private static void RefreshCatalog(float now,bool force)
        {
            int count=ItemManager.Items.Count;if(!force&&now<_nextCatalog&&count==_catalogCount)return;
            _catalogRefreshes++;_nextCatalog=now+.2f;_catalogCount=count;
            _itemsInspected=_creaturesFound=_nearbyFound=0;
            _componentDisabled=_deinitializing=0;
            if(Cache.Count>2048)Cache.Clear();Targets.Clear();
            // Player is hosted on a persistent component object; the game's moving avatar uses
            // Player.Transform (the same position consumed by PlayerCamera), not Component.transform.
            Vector3 localPosition=Player.LocalPlayer?Player.LocalPlayer.Transform.position:Vector3.zero;float near=(_range+20f)*(_range+20f);
            foreach(Item item in ItemManager.Items.Values)
            {
                _itemsInspected++;
                if(!item||!item.Creature||item.Creature.IsDead)continue;
                _creaturesFound++;
                var creature=item.Creature;var position=creature.Rig?creature.Rig.worldCenterOfMass:creature.transform.position;
                if((position-localPosition).sqrMagnitude<=near)
                {_nearbyFound++;if(!creature.isActiveAndEnabled)_componentDisabled++;if(creature.IsDeinitializing)_deinitializing++;Targets.Add(Track(creature));}
            }
            // Remote players remain available to ordinary assist, as before; head mode excludes them.
            var resolver=Read(typeof(Bootstrap),"Targets");
            var players=resolver==null?null:AccessTools.Method(resolver.GetType(),"Players").Invoke(resolver,null) as IEnumerable;
            if(players!=null)foreach(object value in players)
            {
                var player=value as Player;if(!player||player==Player.LocalPlayer)continue;
                AimTargetTrack t;if(!Cache.TryGetValue(player,out t))
                {
                    var capsule=Read(Read(player,"Other"),"_col") as CapsuleCollider;
                    t=new AimTargetTrack{Target=player,Player=player,Transform=player.Transform,Capsule=capsule};Cache[player]=t;
                }
                if(t.Capsule)Targets.Add(t);
            }
        }

        public static bool HeadPoint(object creature,Vector3 origin,out Vector3 point)
        {Bind();var c=creature as Creature;return c&&SolveHead(Track(c),origin,out point)?true:EmptyPoint(out point);}
        private static bool EmptyPoint(out Vector3 point){point=default(Vector3);return false;}

        private static bool SolveHead(AimTargetTrack t,Vector3 origin,out Vector3 point)
        {
            _headSolves++;point=default(Vector3);if(!Alive(t)||!t.Creature)return false;
            float threshold=t.Creature.HeadPos,bestZ=float.NegativeInfinity;bool found=false;
            foreach(var collider in t.Colliders)
            {
                if(!collider||!collider.enabled||collider.isTrigger||!collider.gameObject.activeInHierarchy)continue;
                Bounds bounds=collider.bounds;float minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity;
                for(int mask=0;mask<8;mask++)
                {
                    Vector3 corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));
                    float z=t.Transform.InverseTransformPoint(corner).z;minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
                }
                if(maxZ<=threshold+.001f)continue;
                Vector3 local=t.Transform.InverseTransformPoint(bounds.center);local.z=(Math.Max(threshold,minZ)+maxZ)*.5f;
                Vector3 candidate=collider.ClosestPoint(t.Transform.TransformPoint(local));float zCandidate=t.Transform.InverseTransformPoint(candidate).z;
                if(!Finite(candidate)||zCandidate<=threshold+.0001f||zCandidate<=bestZ)continue;
                Vector3 difference=candidate-origin;float distance=difference.magnitude;if(distance<.01f)continue;
                Ray ray=new Ray(origin,difference/distance);float first=float.PositiveInfinity;Vector3 firstPoint=default(Vector3);bool hit=false;
                foreach(var physical in t.Colliders)
                {
                    if(!physical||!physical.enabled||physical.isTrigger||!physical.gameObject.activeInHierarchy)continue;
                    RaycastHit result;if(physical.Raycast(ray,out result,distance+.05f)&&result.distance<first){first=result.distance;firstPoint=result.point;hit=true;}
                }
                if(!hit||t.Transform.InverseTransformPoint(firstPoint).z<=threshold+.0001f)continue;
                point=candidate;bestZ=zCandidate;found=true;
            }
            t.HasHead=found;t.NextHeadCheck=Time.unscaledTime+.05f;
            if(found)t.LocalHead=t.Transform.InverseTransformPoint(point);
            return found;
        }

        private static bool Point(AimTargetTrack t,Vector3 muzzle,bool head,bool force,out Vector3 point)
        {
            if(!head){point=Center(t);return Finite(point);}
            if(!t.Creature){point=default(Vector3);return false;}
            if(force||!t.HasHead||Time.unscaledTime>=t.NextHeadCheck)return SolveHead(t,muzzle,out point);
            point=t.Transform.TransformPoint(t.LocalHead);return Finite(point);
        }

        private static bool Valid(AimTargetTrack t,Vector3 origin,Vector3 forward,Vector3 muzzle,float cos,bool force,out Vector3 point)
        {
            point=default(Vector3);if(!Alive(t))return false;
            Vector3 difference=Center(t)-origin;float d=difference.sqrMagnitude;
            if(!AimLockPolicy.Eligible(d,Vector3.Dot(forward,difference)/Mathf.Sqrt(d),_range*_range,cos))return false;
            if(!Point(t,muzzle,_headLockEnabled,force,out point))return false;
            difference=point-origin;d=difference.sqrMagnitude;
            return AimLockPolicy.Eligible(d,Vector3.Dot(forward,difference)/Mathf.Sqrt(d),_range*_range,cos)&&Clear(origin,point)&&Clear(muzzle,point);
        }

        private static AimTargetTrack Select(Vector3 origin,Vector3 forward,Vector3 muzzle,bool entering,bool cameraLock,out Vector3 point)
        {
            _scans++;RefreshCatalog(Time.unscaledTime,entering);Candidates.Clear();point=default(Vector3);
            _aliveRejected=0;_nearestCenterDistance=float.MaxValue;_bestAlignment=-1;
            foreach(var t in Targets)
            {
                if(!Alive(t)||(_headLockEnabled&&!t.Creature)){_aliveRejected++;continue;}
                Vector3 direction=Center(t)-origin;float d=direction.sqrMagnitude;
                _nearestCenterDistance=Math.Min(_nearestCenterDistance,Mathf.Sqrt(d));_bestAlignment=Math.Max(_bestAlignment,Vector3.Dot(forward,direction)/Mathf.Sqrt(d));
                if(!AimLockPolicy.Eligible(d,Vector3.Dot(forward,direction)/Mathf.Sqrt(d),_range*_range,cameraLock?-1f:_acquireCos))continue;
                t.DistanceSquared=d;Candidates.Add(t);
            }
            Candidates.Sort(Order);
            _lastCandidates=Candidates.Count;
            foreach(var t in Candidates)if(Valid(t,origin,forward,muzzle,cameraLock?-1f:_acquireCos,true,out point)){_selectedScanDistance=Mathf.Sqrt(t.DistanceSquared);return t;}
            return null;
        }

        private static bool Weapon(Player player,out Weapon weapon)
        {
            var held=player.Holding?player.Holding.HeldItem:null;weapon=held?held.Weapon:null;if(!weapon)return false;
            var attachment=weapon.Attachments;if(!attachment||!attachment.FirePoint)return false;
            _weapon=weapon;_muzzle=attachment.FirePoint.position;
            if(attachment.UseSniperUi&&_aimPercent(weapon)>.9f)_muzzle=player.Camera.CamTransform.position+player.Camera.CamTransform.forward*_forwardLength(weapon);
            var info=_weaponInfo(weapon);if(info==null||!ProjectileManager.Instance)return false;
            _speed=_projectileSpeed(weapon);_gravity=info.ProjectileGravity;
            _hitscan=ProjectileManager.Instance.GetType(attachment.ProjectileType).IsHitScan;
            return Finite(_muzzle)&&!float.IsNaN(_speed)&&!float.IsInfinity(_speed)&&(_hitscan||_speed>.01f);
        }

        private static Vector3 Velocity(AimTargetTrack t,Vector3 point)
        {
            if(t.Rig&&!t.Rig.isKinematic){var v=t.Rig.GetPointVelocity(point);return Finite(v)&&v.sqrMagnitude<=40000?v:Vector3.zero;}
            float now=Time.unscaledTime;
            if(!t.SampleReady){t.SampleReady=true;t.SamplePosition=point;t.SampleTime=now;return Vector3.zero;}
            float dt=now-t.SampleTime;if(dt<.01f)return t.SampleVelocity;
            Vector3 difference=point-t.SamplePosition;t.SamplePosition=point;t.SampleTime=now;
            if(dt>.5f||difference.sqrMagnitude>100f){t.SampleVelocity=Vector3.zero;return Vector3.zero;}
            Vector3 measured=difference/dt;if(!Finite(measured)||measured.sqrMagnitude>40000)measured=Vector3.zero;
            t.SampleVelocity=Vector3.Lerp(t.SampleVelocity,measured,1f-Mathf.Exp(-35f*dt));return t.SampleVelocity;
        }

        private static bool Predict(AimTargetTrack t,Vector3 point,Vector3 muzzle,float speed,out Vector3 launch)
        {
            launch=point-muzzle;_distance=launch.magnitude;_flightTime=_drop=0;_leadOffset=_velocity=Vector3.zero;
            if(!_leadEnabled||_hitscan){if(_hitscan)_hitscanFrames++;_state=_hitscan?"hitscan":"direct";return true;}
            _velocity=Velocity(t,point);NVector solved;double time;
            if(!PredictiveBallistics.TryIntercept(new NVector(launch.x,launch.y,launch.z),new NVector(_velocity.x,_velocity.y,_velocity.z),speed,_gravity,Time.fixedDeltaTime,8,out solved,out time)){_state="no-intercept";return false;}
            _leadOffset=_velocity*(float)time;
            if(_leadOffset.sqrMagnitude>.0001f&&!Clear(muzzle,point+_leadOffset)){_state="predicted-path-blocked";return false;}
            launch=new Vector3(solved.X,solved.Y,solved.Z);_flightTime=(float)time;_drop=(float)PredictiveBallistics.Drop(_gravity,time,Time.fixedDeltaTime);_predictedFrames++;_state="predicted";
            return Finite(launch);
        }

        public static bool Prefix(object __instance,Vector3 cameraPosition,Vector3 cameraEuler,float manualLookAmount,ref Vector2 __result)
        {
            _handledAssist=null;
            if(!_enabled||_host==null)return true;
            Player player=Player.LocalPlayer;if(!player||player.AimAssist!=(__instance as PlayerAimAssist))return true;
            _handledAssist=__instance;_handledFrame=Time.frameCount;_measureStart=Stopwatch.GetTimestamp();_allocationStart=GC.GetAllocatedBytesForCurrentThread();__result=Vector2.zero;
            _measureTracking=false;
            try
            {
                if(!_host.IsLockEnabled("combat.aim_assist")){_enabled=false;_state="off";_target=null;return false;}
                Weapon weapon;
                if(!player.Camera||!player.Dying||player.BlockInputs||!player.Camera.MouseLocked||player.Dying.IsDead||!Weapon(player,out weapon))
                {_wasAds=false;_target=null;_state="input-blocked";return false;}
                if(!weapon.IsAds){if(_wasAds)_released++;_outsideAdsFrames++;_wasAds=false;_target=null;_state="waiting-for-ads";return false;}
                bool entering=!_wasAds;_wasAds=true;if(entering){_adsEdges++;_adsEdgeFrame=Time.frameCount;_firstLockDelay=-1;}
                Vector3 forward=Quaternion.Euler(cameraEuler)*Vector3.forward,point=default(Vector3);
                var previousTarget=_target;
                if(entering||!Valid(_target,cameraPosition,forward,_muzzle,-1f,false,out point))
                {
                    _target=null;
                    if(AimLockPolicy.RetryNow(entering,previousTarget!=null,Time.unscaledTime,_nextScan))
                    {_target=Select(cameraPosition,forward,_muzzle,entering,true,out point);_nextScan=Time.unscaledTime+.05f;}
                    if(_target==null){_state="no-visible-target";return false;}
                    if(previousTarget!=null&&!ReferenceEquals(previousTarget,_target))_automaticSwitches++;
                }
                if(_firstLockDelay<0)_firstLockDelay=Time.frameCount-_adsEdgeFrame;
                _lockedFrames++;
                _measureTracking=true;
                if(_headLockEnabled){_headFrames++;_headMargin=_target.Transform.InverseTransformPoint(point).z-_target.Creature.HeadPos;}
                Vector3 launch;if(!Predict(_target,point,_muzzle,_speed,out launch))launch=point-_muzzle;
                Vector3 desired=Quaternion.LookRotation(launch,Vector3.up).eulerAngles;
                Vector2 difference=new Vector2(Mathf.DeltaAngle(cameraEuler.x,desired.x),Mathf.DeltaAngle(cameraEuler.y,desired.y));
                __result=Vector2.ClampMagnitude(difference*AimLockPolicy.TrackingWeight(_strength,Time.deltaTime,manualLookAmount),(360f+_strength*6f)*Time.deltaTime);
            }
            catch(Exception e){_exceptions++;_failure=e.GetType().Name+": "+e.Message;_state="fallback";_target=null;__result=Vector2.zero;}
            return false;
        }

        public static bool Handled(object assist,ref Vector2 result)
        {
            if(!ReferenceEquals(_handledAssist,assist)||_handledFrame!=Time.frameCount)return false;
            // Proofs are status reports, not control input. Publish at 5 Hz instead of every camera frame.
            if(result.sqrMagnitude>1e-6f&&Time.unscaledTime>=_nextProof)
            {_nextProof=Time.unscaledTime+.2f;_host.RecordAimAssistHit(assist,result);}
            long elapsed=Stopwatch.GetTimestamp()-_measureStart,allocated=GC.GetAllocatedBytesForCurrentThread()-_allocationStart;
            _frames++;_ticks+=elapsed;_maxTicks=Math.Max(_maxTicks,elapsed);_allocated+=allocated;_maxAllocated=Math.Max(_maxAllocated,allocated);
            double us=elapsed*1000000d/Stopwatch.Frequency;for(int i=0;i<Limits.Length;i++)if(us<=Limits[i]){Histogram[i]++;break;}
            if(_measureTracking){_trackingMeasureFrames++;_trackingTicks+=elapsed;_trackingMaxTicks=Math.Max(_trackingMaxTicks,elapsed);for(int i=0;i<Limits.Length;i++)if(us<=Limits[i]){TrackingHistogram[i]++;break;}}
            return true;
        }

        public static bool SelectHeadSilent(TrainerBridgeHost host,object assist,Transform view,Vector3 muzzle,out string scope,out Vector3? result)
        {
            scope="creature-head";result=null;if(!_headLockEnabled)return false;
            Bind();Vector3 point;
            if(Valid(_target,view.position,view.forward,muzzle,_acquireCos,false,out point))_silentTarget=_target;
            else _silentTarget=Select(view.position,view.forward,muzzle,false,false,out point);
            if(_silentTarget!=null)result=point;return true;
        }

        public static Vector3 AdjustSilent(Vector3 point,TrainerBridgeHost host,object owner,Vector3 muzzle,Vector3[] velocities)
        {
            if(!_leadEnabled||velocities==null||velocities.Length==0)return point;
            try
            {
                Bind();Weapon weapon;var player=owner as Player;if(!player||!Weapon(player,out weapon)||_hitscan)return point;
                AimTargetTrack target=_headLockEnabled?_silentTarget:null;
                if(target==null){RefreshCatalog(Time.unscaledTime,false);foreach(var t in Targets)if(Alive(t)&&(Center(t)-point).sqrMagnitude<.00001f){target=t;break;}}
                Vector3 launch;if(target!=null&&Predict(target,point,muzzle,velocities[0].magnitude,out launch)){_silentShots++;return muzzle+launch;}
            }
            catch(Exception e){_exceptions++;_failure=e.GetType().Name+": "+e.Message;_state="silent-fallback";}
            return point;
        }

        private static JObject VectorJson(Vector3 v)=>new JObject{["x"]=v.x,["y"]=v.y,["z"]=v.z};
        private static double Percentile95(long[] histogram,long frames,long maxTicks)
        {if(frames==0)return 0;long total=0;for(int i=0;i<histogram.Length;i++){total+=histogram[i];if(total>=frames*.95)return double.IsInfinity(Limits[i])?maxTicks*1000000d/Stopwatch.Frequency:Limits[i];}return 0;}
        public static JToken Describe(JToken session)
        {
            var obj=session as JObject;if(obj==null)return session;
            if(_allocationProbe<0){long before=GC.GetAllocatedBytesForCurrentThread();var allocation=new byte[4096];GC.KeepAlive(allocation);_allocationProbe=GC.GetAllocatedBytesForCurrentThread()-before;}
            var diagnostic=new JObject{["enabled"]=_enabled,["leadEnabled"]=_leadEnabled,["headLockEnabled"]=_headLockEnabled,["selection"]="nearest-distance-on-ads",
                ["adsOnly"]=true,["cameraAcquisitionAngleRestricted"]=false,["lockedTarget"]=_target!=null&&_target.Target?_target.Target.name:null,["firstLockDelayFrames"]=_firstLockDelay,["selectedScanDistance"]=_selectedScanDistance,
                ["selectionDiagnostics"]=new JObject{["range"]=_range,["angle"]=_angle,["acquireCos"]=_acquireCos,["items"]=_itemsInspected,["creatures"]=_creaturesFound,["nearby"]=_nearbyFound,["componentDisabled"]=_componentDisabled,["deinitializing"]=_deinitializing,["aliveRejected"]=_aliveRejected,["candidates"]=_lastCandidates,["nearestCenterDistance"]=_nearestCenterDistance,["bestAlignment"]=_bestAlignment},
                ["predictedFrames"]=_predictedFrames,["headFrames"]=_headFrames,["silentShots"]=_silentShots,["hitscanFrames"]=_hitscanFrames,["headRegionMargin"]=_headMargin,
                ["last"]=new JObject{["state"]=_state,["reason"]=_failure,["speed"]=_speed,["gravity"]=_gravity,["flightTime"]=_flightTime,["distance"]=_distance,["targetVelocity"]=VectorJson(_velocity),["leadOffset"]=VectorJson(_leadOffset),["dropCompensation"]=_drop},
                ["performance"]=new JObject{["frames"]=_frames,["meanUs"]=_frames==0?0:_ticks*1000000d/Stopwatch.Frequency/_frames,["p95UpperUs"]=Percentile95(Histogram,_frames,_maxTicks),["maxUs"]=_maxTicks*1000000d/Stopwatch.Frequency,
                    ["trackingFrames"]=_trackingMeasureFrames,["trackingMeanUs"]=_trackingMeasureFrames==0?0:_trackingTicks*1000000d/Stopwatch.Frequency/_trackingMeasureFrames,["trackingP95UpperUs"]=Percentile95(TrackingHistogram,_trackingMeasureFrames,_trackingMaxTicks),["trackingMaxUs"]=_trackingMaxTicks*1000000d/Stopwatch.Frequency,
                    ["allocatedBytes"]=_allocationProbe>=4096?new JValue(_allocated):JValue.CreateNull(),["meanAllocatedBytes"]=_allocationProbe>=4096?new JValue(_frames==0?0:(double)_allocated/_frames):JValue.CreateNull(),["maxAllocatedBytes"]=_allocationProbe>=4096?new JValue(_maxAllocated):JValue.CreateNull(),["catalogRefreshes"]=_catalogRefreshes,["selectionScans"]=_scans,["headGeometrySolves"]=_headSolves,["occlusionChecks"]=_occlusionChecks,["exceptions"]=_exceptions,
                    ["adsEdges"]=_adsEdges,["releaseEdges"]=_released,["lockedFrames"]=_lockedFrames,["automaticSwitches"]=_automaticSwitches,["outsideAdsFrames"]=_outsideAdsFrames,["nativeReapplySkipped"]=_nativeApplySkipped,["allocationCounterProbeBytes"]=_allocationProbe,["allocationCounterAvailable"]=_allocationProbe>=4096}};
            if(_weapon)diagnostic["weapon"]=new JObject{["name"]=_weapon.name,["speed"]=_speed,["gravity"]=_gravity,["hitscan"]=_hitscan,["muzzle"]=VectorJson(_muzzle)};
            var waves=Read(_host??Read(typeof(Bootstrap),"_bridge"),"_funWaves") as IDictionary;var counts=new JArray();
            if(waves!=null)foreach(object wave in waves.Values)
            {
                if(!Equals(Read(wave,"EventId"),"seagull-migration"))continue;int alive=0;var entities=Read(wave,"Entities") as IEnumerable;
                if(entities!=null)foreach(object entity in entities){var item=entity as Item;if(item&&item.Creature&&!item.Creature.IsDead)alive++;}
                counts.Add(new JObject{["waveId"]=(string)Read(wave,"Id"),["alive"]=alive});
            }
            diagnostic["seagullWaves"]=counts;obj["predictiveAim"]=diagnostic;return session;
        }
    }
}
