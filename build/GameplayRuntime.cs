using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbyssalCrab.Runtime
{
    public static class GameplayRuntime
    {
        private static TrainerBridgeHost _host;
        private static float _angle = 8, _distance = 35, _strength = 100;
        private static string _trigger = "fire";
        private static bool _head;
        private static Harmony _harmony;
        private static int _readyGroups;
        private static string _dashError, _damageError, _projectileError;
        private static float _dashUntil;
        private static int _dashPressedFrame = -1;
        private static Vector3 _dashDirection;
        private static Player _dashPlayer;
        private static GameplayExplosion _explosion;
        private static readonly Dictionary<Projectile, GameplayFlight> Shots = new Dictionary<Projectile, GameplayFlight>();
        private static readonly List<Projectile> Expired = new List<Projectile>();
        private static float _nextPrune;
        private static float _nextDashWarning, _nextShotWarning;
        private static string _lastDashError, _lastShotError;
        private static AccessTools.FieldRef<ProjectileManager,uint> _nextId;
        private static AccessTools.FieldRef<Weapon,float> _aimPercent;
        private static AccessTools.FieldRef<PlayerMovement,bool> _swimming;
        private static AccessTools.FieldRef<PlayerVitals,Player> _vitalsPlayer;
        private static AccessTools.FieldRef<PlayerVitals,float> _playerDamageMultiplier;
        private static object Call(object target, string name, params object[] args)
        { try { return AccessTools.Method(target.GetType(), name).Invoke(target, args); } catch(TargetInvocationException e) { throw e.InnerException ?? e; } }
        private static void Warn(Exception error,bool dash)
        {
            string message=error.GetType().Name+": "+error.Message;float now=Time.unscaledTime;
            if(dash){_lastDashError=message;if(now<_nextDashWarning)return;_nextDashWarning=now+1;}
            else {_lastShotError=message;if(now<_nextShotWarning)return;_nextShotWarning=now+1;}
            Debug.LogWarning("[AbyssalCrab] "+(dash?"冲刺":"弹丸")+"暂时使用原生流程："+message);
        }
        public static JToken Annotate(JToken session)
        {
            var obj=session as JObject;if(obj==null)return session;
            obj["gameplayExtensions"]=new JObject{["dashReady"]=GroupReady(1),["damageReady"]=GroupReady(2),["projectileReady"]=GroupReady(4),
                ["dashBindError"]=_dashError,["damageBindError"]=_damageError,["projectileBindError"]=_projectileError,
                ["lastDashError"]=_lastDashError,["lastProjectileError"]=_lastShotError,["trackedProjectiles"]=Shots.Count,["targetScans"]=GameplayTargeting.SelectionScans};
            return obj;
        }
        public static void Install(Harmony harmony, string target)
        {
            if (target != "Attachments:get_Damage") return;
            _harmony=harmony;
            TryInstallGroup(1); TryInstallGroup(2); TryInstallGroup(4);
        }
        private static string Error(int group) => group==1?_dashError:group==2?_damageError:_projectileError;
        private static bool GroupReady(int group) => (_readyGroups&group)!=0;
        private static bool TryInstallGroup(int group)
        {
            try
            {
                var plan=GameplayHooks.Plan(group,typeof(GameplayRuntime));
                if(group==1) _swimming=AccessTools.FieldRefAccess<PlayerMovement,bool>("_isSwimming");
                if(group==2) { _vitalsPlayer=AccessTools.FieldRefAccess<PlayerVitals,Player>("_player"); _playerDamageMultiplier=AccessTools.FieldRefAccess<PlayerVitals,float>("_playerDamageMultiplier"); }
                if(group==4) { _nextId=AccessTools.FieldRefAccess<ProjectileManager,uint>("_nextId"); _aimPercent=AccessTools.FieldRefAccess<Weapon,float>("_aimPercent"); }
                GameplayHooks.Install(_harmony,plan);_readyGroups|=group;
                if(group==1)_dashError=null;else if(group==2)_damageError=null;else _projectileError=null;
                return true;
            }
            catch(Exception e)
            {
                _readyGroups&=~group;string error=e.GetType().Name+": "+e.Message;
                if(group==1)_dashError=error;else if(group==2)_damageError=error;else _projectileError=error;
                Debug.LogWarning("[AbyssalCrab] 功能组 "+group+" 暂不可用："+error);return false;
            }
        }
        public static void EnsureBeforeLock(string id,JObject args)
        {
            int group=id=="player.air_dash.lock"?1:id=="combat.one_hit"?2:id=="combat.silent_aim"?4:0;
            if(group==0)return;
            if(args.Value<bool?>("enabled")==false)return;
            if(_harmony==null)_harmony=new Harmony("HowToFishTrainer.RuntimeBridge");
            if(!TryInstallGroup(group))throw new NotSupportedException("对应功能钩子暂不可用，可稍后重新开启。"+Error(group));
        }
        public static bool Validate(string id, JObject args)
        {
            if (id == "player.move.multiplier" || id == "player.sprint.multiplier" || id == "player.jump.multiplier")
            { Require(args, "value", .25, GameplayPolicy.Maximum(id), false); return true; }
            if (id != "combat.silent_aim" && id != "player.air_dash.lock") return false;
            if (args["enabled"] == null || args["enabled"].Type != JTokenType.Boolean) throw new ArgumentException("enabled 必须为布尔值");
            if (!args.Value<bool>("enabled") || id != "combat.silent_aim") return true;
            Require(args, "angle", .5, 180, true); Require(args, "distance", 1, 1000, true); Require(args, "strength", 0, 100, true);
            if (args["trigger"] != null && (args["trigger"].Type != JTokenType.String || args.Value<string>("trigger") != "ads" && args.Value<string>("trigger") != "fire")) throw new ArgumentException("trigger 必须为 ads 或 fire");
            if (args["targetTeammates"] != null && args["targetTeammates"].Type != JTokenType.Boolean) throw new ArgumentException("targetTeammates 必须是布尔值");
            return true;
        }
        private static void Require(JObject a, string key, double min, double max, bool optional)
        { var t = a[key]; if (t == null && optional) return; if (t == null || t.Type != JTokenType.Float && t.Type != JTokenType.Integer || !GameplayPolicy.Number(t.Value<double>(), min, max)) throw new ArgumentOutOfRangeException(key, key + " 超出有效范围"); }
        public static bool ApplyExtra(TrainerBridgeHost host, string id, JToken value)
        {
            if (id == "combat.silent_aim" && !GroupReady(4)) throw new NotSupportedException(_projectileError ?? "子弹追踪接口尚未绑定");
            if (id != "combat.one_hit" && id != "player.air_dash.lock") return false;
            int group=id=="combat.one_hit"?2:1;
            if (!GroupReady(group)) throw new NotSupportedException(Error(group) ?? "对应游戏接口尚未绑定");
            _host = host; return true;
        }
        public static JToken Configured(JToken result, TrainerBridgeHost host, string id, JObject args)
        {
            _host = host;
            if (id == "combat.aim_assist") _head = args.Value<bool?>("headLockEnabled") ?? false;
            if (id == "combat.silent_aim")
            {
                if(args.Value<bool?>("enabled")==false)
                { GameplayTargeting.ConfigureTeammates(false); MagicRangeRenderer.Stop(); return result; }
                _angle = args.Value<float?>("angle") ?? 8; _distance = args.Value<float?>("distance") ?? 35;
                _strength = args.Value<float?>("strength") ?? 100; _trigger = args.Value<string>("trigger") ?? "fire";
                GameplayTargeting.ConfigureTeammates(args.Value<bool?>("targetTeammates") ?? false);
                MagicRangeRenderer.Configure(host, _angle);
            }
            return result;
        }
        public static void Restore(TrainerBridgeHost host, string id)
        { if (id == "combat.silent_aim") { ClearProjectiles(); GameplayTargeting.ConfigureTeammates(false); MagicRangeRenderer.Stop(); } if (id == "player.air_dash.lock") StopDash(); }
        public static void Reset(TrainerBridgeHost host)
        { ClearProjectiles(); StopDash(); GameplayTargeting.Reset(); MagicRangeRenderer.Stop(); _host = null; _head = false; _explosion = null;_nextDashWarning=_nextShotWarning=0;_lastDashError=_lastShotError=null; }
        private static void ClearProjectiles() { Shots.Clear(); Expired.Clear(); _nextPrune = 0; }
        private static void StopDash() { _dashUntil = 0; _dashPressedFrame = -1; _dashPlayer = null; }
        private static bool Ready(Player p) => p && p == Player.LocalPlayer && !p.IsDeinitializing && p.Dying && !p.Dying.IsDead && p.Camera && p.Camera.MouseLocked && !p.BlockInputs;
        public static void Tick(TrainerBridgeHost host)
        {
            _host = host;
            try
            {
                var p = Player.LocalPlayer;
                if (!GroupReady(1) || !host.IsLockEnabled("player.air_dash.lock") || !Ready(p) || !p.Movement || p.Movement.Grounded || p.Movement.OnBoat || _swimming(p.Movement) || Boat.IsDrivingLocally || IslandManager.IsLoading)
                { _dashUntil = 0; _dashPlayer = null; return; }
                var kb = Keyboard.current;
                if (kb == null || !Application.isFocused || _dashPressedFrame == Time.frameCount || !(kb.leftAltKey.wasPressedThisFrame || kb.rightAltKey.wasPressedThisFrame)) return;
                float x,z; GameplayPolicy.Direction(kb.wKey.isPressed,kb.aKey.isPressed,kb.sKey.isPressed,kb.dKey.isPressed,out x,out z);
                _dashDirection = p.CurPlayerRot * new Vector3(x,0,z); _dashPlayer = p; _dashUntil = Time.time + .18f; _dashPressedFrame = Time.frameCount;
                host.RecordFeatureHit("player.air_dash.lock", "alt-directional-air-dash");
            }
            catch(Exception e) { StopDash(); Warn(e,true); }
        }
        public static void DashFixed(PlayerMovement __instance)
        {
            try { DashPhysics(__instance); }
            catch(Exception e) { StopDash(); Warn(e,true); }
        }
        private static void DashPhysics(PlayerMovement __instance)
        {
            if (_host == null || !_host.IsLockEnabled("player.air_dash.lock") || !_dashPlayer || _dashPlayer.Movement != __instance || Time.time >= _dashUntil) return;
            if (!Ready(_dashPlayer) || __instance.Grounded || __instance.OnBoat || _swimming(__instance) || IslandManager.IsLoading || !_dashPlayer.Rigidbody) { _dashUntil = 0; return; }
            float multiplier; if (!_host.TryGetLockFloat("player.move.multiplier", out multiplier)) multiplier = 1;
            var rig = _dashPlayer.Rigidbody; Vector3 v = rig.linearVelocity;
            float speed = 20 * Mathf.Clamp(multiplier,.25f,50); RaycastHit hit;
            if (rig.SweepTest(_dashDirection, out hit, speed * Time.fixedDeltaTime + .05f, QueryTriggerInteraction.Ignore)) speed = Mathf.Min(speed,Mathf.Max(0,hit.distance-.05f)/Time.fixedDeltaTime);
            rig.linearVelocity = new Vector3(_dashDirection.x * speed,v.y,_dashDirection.z * speed);
        }
        private static bool OneHit(Player source, int damage) => _host != null && GroupReady(2) && _host.IsLockEnabled("combat.one_hit") && source && source == Player.LocalPlayer && damage > 0;
        public static void WeaponDamage(TrainerBridgeHost host,object attachments,ref int result)
        {
            var a=attachments as Attachments;var p=Player.LocalPlayer;
            if(host==null||!host.CanApplyOwnedWeaponDamage()||!a||!p||!p.Holding||!p.Holding.HeldItem||!p.Holding.HeldItem.Weapon||p.Holding.HeldItem.Weapon.Attachments!=a)return;
            float multiplier;if(host.TryGetLockFloat("combat.damage.multiplier",out multiplier))result=HowToFishTrainer.Core.DamageMultiplierPolicy.Scale(result,multiplier);
        }
        public static void CreatureHit(Creature __instance, Player player, ref int damage, bool fromNpc, Vector3 point, bool rangedHit)
        { if (!fromNpc && OneHit(player,damage) && __instance && !__instance.IsDead) { double scale=rangedHit&&__instance.transform.InverseTransformPoint(point).z>__instance.HeadPos?GameInfo.HeadShotDamageMulti:1;damage=GameplayPolicy.BeforeMultiplier(__instance.Hp,damage,scale);_host.RecordFeatureHit("combat.one_hit","creature-local-hit"); } }
        public static void ServerCreatureHit(Creature creature, Player playerWhoHit, ref int damage)
        { if (OneHit(playerWhoHit,damage) && creature && !creature.IsDead) { damage=GameplayPolicy.Lethal(creature.Hp,damage);_host.RecordFeatureHit("combat.one_hit","server-creature-hit"); } }
        public static void PlayerHit(PlayerVitals __instance, Player playerWhoHit, ref int damage, bool fromNpc)
        { if (!fromNpc && ServerSettings.UseFriendlyFire && OneHit(playerWhoHit,damage) && _vitalsPlayer(__instance)!=playerWhoHit) damage=GameplayPolicy.BeforeMultiplier(__instance.Health,damage,(double)_playerDamageMultiplier(__instance)*ServerSettings.DamageMultiplier); }
        public static void ServerPlayerHit(Player player, ref int damage, Player playerWhoHit)
        { if (player && player != playerWhoHit && ServerSettings.UseFriendlyFire && OneHit(playerWhoHit,damage)) { damage=GameplayPolicy.BeforeMultiplier(player.Vitals.Health,damage,ServerSettings.DamageMultiplier); _host.RecordFeatureHit("combat.one_hit","player-hit"); } }
        public static void BeforeExplosion(Item item, out GameplayExplosion __state)
        { __state=_explosion;var owner=item?(item.Explosive&&item.Explosive.PlayerWhoForcedExplosion?item.Explosive.PlayerWhoForcedExplosion:item.LastPlayer):null;_explosion=OneHit(owner,1)?new GameplayExplosion{Owner=owner}:null; }
        public static Exception EndExplosion(Exception __exception, GameplayExplosion __state) { _explosion=__state; return __exception; }
        public static void ExplosionDamage(Creature creature, ref int __result)
        { if (creature && _explosion!=null && OneHit(_explosion.Owner,__result)) { int damage;if(!_explosion.Damage.TryGetValue(creature,out damage)){damage=GameplayPolicy.Lethal(creature.Hp,__result);_explosion.Damage[creature]=damage;}__result=damage;_host.RecordFeatureHit("combat.one_hit","owned-explosion"); } }
        private static bool ShotReady(object owner, object weaponInfo, bool local, bool forbidden)
        {
            var p=owner as Player; var info=weaponInfo as WeaponInfo;
            return GroupReady(4) && _host != null && _host.IsLockEnabled("combat.silent_aim") && local && !forbidden && Ready(p) && p.Holding && p.Holding.HeldItem && p.Holding.HeldItem.Weapon && info != null && info.Weapon == p.Holding.HeldItem.Weapon && GameplayPolicy.Trigger(_trigger,info.Weapon.IsAds);
        }
        public static bool Redirect(TrainerBridgeHost host, object owner, object info, bool local, bool forbidden, Vector3 muzzle, Vector3[] velocities, out Vector3[] corrected)
        {
            corrected=null;
            try { return RedirectShot(host,owner,info,local,forbidden,muzzle,velocities,out corrected); }
            catch(Exception e) { corrected=null; Warn(e,false); return false; }
        }
        private static bool RedirectShot(TrainerBridgeHost host, object owner, object info, bool local, bool forbidden, Vector3 muzzle, Vector3[] velocities, out Vector3[] corrected)
        {
            corrected=null; if (host != _host || !ShotReady(owner,info,local,forbidden) || velocities == null || velocities.Length == 0 || _strength <= 0) return false;
            var p=(Player)owner; if ((muzzle-p.Camera.CamTransform.position).sqrMagnitude>25) return false;
            Vector3 point; var target=GameplayTargeting.Select(p.Camera.CamTransform.position,p.Camera.CamTransform.forward,muzzle,_distance,_angle,_head,out point);
            if (!target) return false;
            var weapon=((WeaponInfo)info).Weapon;
            Vector3 current=weapon.Attachments.UseSniperUi && _aimPercent(weapon)>.9f ? p.Camera.CamTransform.forward : weapon.Attachments.FirePoint.forward;
            if (current.sqrMagnitude<.0001f) return false;
            if(!ProjectileManager.Instance)return false;
            Vector3 launch=GameplayTargeting.LaunchDirection(target,point,muzzle,velocities[0],((WeaponInfo)info).ProjectileGravity,ProjectileManager.Instance.GetType(((WeaponInfo)info).ProjectileType).IsHitScan);
            Vector3 desired=Vector3.Slerp(current.normalized,launch.normalized,_strength/100);
            Quaternion turn=Quaternion.FromToRotation(current.normalized,desired); corrected=new Vector3[velocities.Length];
            for(int i=0;i<velocities.Length;i++) corrected[i]=turn*velocities[i];
            host.RecordFeatureHit("combat.silent_aim","configured-projectile-launch"); return true;
        }
        private static GameplayLaunch Launch(ProjectileManager manager, Player owner, WeaponInfo info, bool local, bool forbidden, Vector3 pos)
        {
            if (!ShotReady(owner,info,local,forbidden) || _strength<=0) return null;
            Vector3 point; var t=GameplayTargeting.Select(owner.Camera.CamTransform.position,owner.Camera.CamTransform.forward,pos,_distance,_angle,_head,out point);
            return new GameplayLaunch { StartId=_nextId(manager),Target=t,Angle=_angle,Distance=_distance,Strength=_strength,Head=_head,NextScan=Time.unscaledTime+.05f,PointTime=Time.fixedTime,CachedPoint=point,HasPoint=t!=null };
        }
        public static void BeforeSingle(ProjectileManager __instance, Player owner, WeaponInfo weaponInfo, bool isLocal, bool fromNpc, Vector3 pos, out GameplayLaunch __state)
        { __state=null; try { __state=Launch(__instance,owner,weaponInfo,isLocal,fromNpc,pos); } catch(Exception e) { Warn(e,false); } }
        public static void BeforeMultiple(ProjectileManager __instance, Player owner, WeaponInfo weaponInfo, bool isLocal, bool canHitOwner, Vector3 pos, out GameplayLaunch __state)
        { __state=null; try { __state=Launch(__instance,owner,weaponInfo,isLocal,canHitOwner,pos); } catch(Exception e) { Warn(e,false); } }
        public static void AfterLaunch(ProjectileManager __instance, Player owner, WeaponInfo weaponInfo, GameplayLaunch __state)
        {
            try { RegisterLaunch(__instance,owner,weaponInfo,__state); }
            catch(Exception e) { Warn(e,false); }
        }
        private static void RegisterLaunch(ProjectileManager __instance, Player owner, WeaponInfo weaponInfo, GameplayLaunch __state)
        {
            if (__state==null || Shots.Count>=1024)return;
            uint count=unchecked(_nextId(__instance)-__state.StartId);
            foreach(var p in __instance.GetType(weaponInfo.ProjectileType).Projectiles)
                if(p.IsLocal&&p.Owner==owner&&unchecked(p.Id-__state.StartId)<count)
                    if(Shots.Count<1024)Shots[p]=new GameplayFlight {Volley=__state,LastSeen=Time.unscaledTime};
        }
        public static void TrackProjectile(Projectile projectile)
        {
            try { Track(projectile); }
            catch(Exception e) { if(projectile!=null)Shots.Remove(projectile);Warn(e,false); }
        }
        private static void Track(Projectile projectile)
        {
            if (_host==null || !_host.IsLockEnabled("combat.silent_aim")) { ClearProjectiles(); return; }
            float now=Time.unscaledTime;
            if(now>=_nextPrune){_nextPrune=now+.5f;Expired.Clear();foreach(var pair in Shots)if(now-pair.Value.LastSeen>1)Expired.Add(pair.Key);foreach(var p in Expired)Shots.Remove(p);}
            GameplayFlight flight; if(!Shots.TryGetValue(projectile,out flight)||!projectile.IsLocal||projectile.Owner!=Player.LocalPlayer||projectile.FromNpc)return;
            flight.LastSeen=now;var state=flight.Volley; Vector3 point;
            if(!projectile.Owner||projectile.Owner.IsDeinitializing||IslandManager.IsLoading){Shots.Remove(projectile);return;}
            bool alive=GameplayTargeting.Point(state.Target,out point);
            if(now>=state.NextScan||!ReferenceEquals(state.Target,null)&&!alive)
            {state.NextScan=now+.05f;state.Target=GameplayTargeting.Select(projectile.Position,projectile.Velocity.normalized,projectile.Position,state.Distance,state.Angle,state.Head,out point);state.CachedPoint=point;state.PointTime=Time.fixedTime;state.HasPoint=state.Target!=null;}
            if(ReferenceEquals(state.Target,null))return;
            if(!state.HasPoint||state.PointTime!=Time.fixedTime)
            {state.HasPoint=GameplayTargeting.AimPoint(state.Target,projectile.Position,state.Head,out point);state.CachedPoint=point;state.PointTime=Time.fixedTime;}
            if(!state.HasPoint)return;point=state.CachedPoint;
            Vector3 difference=point-projectile.Position;
            if(difference.sqrMagnitude>state.Distance*state.Distance||!GameplayTargeting.Clear(projectile.Position,point))return;
            float speed=projectile.Velocity.magnitude;
            Vector3 launch=GameplayTargeting.LaunchDirection(state.Target,point,projectile.Position,projectile.Velocity,projectile.GravityForce,false);
            projectile.Velocity=Vector3.RotateTowards(projectile.Velocity,launch.normalized*speed,GameplayPolicy.TurnRadians(state.Strength,Time.fixedDeltaTime),0);
        }
        public static bool TryHandle(TrainerBridgeHost host, JObject envelope)
        {
            if(envelope.Value<string>("featureId")!="unlock.islands")return false;
            Call(host,"ValidateAuthority","unlock.islands");
            if(!OnlineIslandManager.Instance || !OnlineIslandManager.Instance.IsServerInitialized || SaveManager.CurServerSave==null)throw new InvalidOperationException("请进入可保存的房主或单人世界");
            var catalog=(JObject)Call(host,"ReadIslandCatalog"); int max=-1,count=0;
            foreach(JObject island in (JArray)catalog["islands"])
            {int? index=island.Value<int?>("runtimeIndex");if(index.HasValue&&island.Value<bool>("available")&&index>=0&&index<=255){max=Math.Max(max,index.Value);count++;}}
            if(max<0)throw new InvalidOperationException("当前未读取到有效岛屿目录");
            OnlineIslandManager.Instance.UnlockIsland((byte)max);
            if(OnlineIslandManager.MaxIslandUnlocked<max)throw new InvalidOperationException("岛屿解锁状态未更新");
            try { SaveManager.SaveServer(false); }
            catch(Exception e) { throw new InvalidOperationException("岛屿已解锁，但原生世界保存失败；请检查存档位置并手动保存。",e); }
            if(SaveManager.CurServerSave.MaxIsland<max)throw new InvalidOperationException("解锁已应用，但原生世界保存未确认，请手动保存");
            Call(host,"Reply",envelope.Value<string>("correlationId"),true,"全部有效岛屿已解锁并请求原生保存",new JObject{["count"]=count,["maxIsland"]=max,["saved"]=true},false,true);
            return true;
        }
        public static void PrepareIsland(JObject args)
        {
            var catalog=(JObject)AccessTools.Method(typeof(TrainerBridgeHost),"ReadIslandCatalog").Invoke(null,null);
            string key=args.Value<string>("key");int? legacy=args.Value<int?>("value");JObject selected=null;
            foreach(JObject candidate in (JArray)catalog["islands"])
                if(!string.IsNullOrWhiteSpace(key) ? candidate.Value<string>("key")==key : legacy.HasValue&&candidate.Value<int?>("runtimeIndex")==legacy) {selected=candidate;break;}
            if(selected==null||!selected.Value<bool>("available"))throw new InvalidOperationException(selected?.Value<string>("reason")??"所选岛屿不在有效目录中");
            int? index=selected.Value<int?>("runtimeIndex"),specified=args.Value<int?>("runtimeIndex"),scene=args.Value<int?>("sceneBuildIndex");
            if(scene.HasValue&&scene!=selected.Value<int>("sceneBuildIndex")||specified.HasValue&&specified!=index)throw new InvalidOperationException("岛屿目录已变化，请刷新后重新选择");
            string mode=selected.Value<string>("loadMode");
            if(index.HasValue&&(mode=="native-island"||mode=="native-mini-island"))
            {
                if(!OnlineIslandManager.Instance||!OnlineIslandManager.Instance.IsServerInitialized)throw new InvalidOperationException("原生岛屿载入管理器尚未就绪");
                OnlineIslandManager.Instance.UnlockIsland(checked((byte)index.Value));
            }
        }
    }
}
