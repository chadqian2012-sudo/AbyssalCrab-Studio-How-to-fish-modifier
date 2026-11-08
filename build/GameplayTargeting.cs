using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace AbyssalCrab.Runtime
{
    public static class GameplayTargeting
    {
        private static readonly List<Component> Targets = new List<Component>(128);
        private static readonly IReadOnlyList<Component> DisplayCatalog = Targets.AsReadOnly();
        private static float _nextRefresh;
        private static int _count = -1;
        private static int _displayRevision;
        private static bool _targetTeammates;
        private static AccessTools.FieldRef<OtherPlayer,CapsuleCollider> _capsule;
        public static long SelectionScans;
        public static int Priority(Component target)
        { var c = target as Creature; return c ? (c.BossType != BossType.None ? 0 : 1) : 2; }
        public static bool IsAliveCreature(Creature creature)
        { return creature && creature.gameObject.activeInHierarchy && creature.isActiveAndEnabled && !creature.IsDeinitializing && !creature.IsDead && creature.MaxHp > 0; }
        public static bool ProjectileCollider(Collider collider)
        {
            // Native ProjectileManager uses ProjectileHitLayer and QueryTriggerInteraction.UseGlobal.
            // Albatross uses trigger hitboxes, so blanket trigger exclusion loses a real boss.
            return collider && collider.enabled && collider.gameObject.activeInHierarchy
                && ((1 << collider.gameObject.layer) & (int)GameInfo.ProjectileHitLayer) != 0
                && (!collider.isTrigger || Physics.queriesHitTriggers);
        }
        public static object Spawnable(object singleton, string method, object[] arguments)
        {
            if (method != "GetSpawnable" || arguments == null || arguments.Length != 1 || !(arguments[0] is byte))
                throw new ArgumentException("Native spawn lookup requires exactly one byte ID.");
            // Bind the byte overload at compilation; never convert it to the string overload.
            return GameInfo.GetSpawnable((byte)arguments[0]);
        }
        public static void ConfigureTeammates(bool enabled) { _targetTeammates = enabled; }
        public static void Reset() { Targets.Clear(); _nextRefresh = 0; _count = -1; _displayRevision++; SelectionScans=0; _targetTeammates=false; }
        private static void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh && _count == ItemManager.Items.Count) return;
            _nextRefresh = Time.unscaledTime + .2f; _count = ItemManager.Items.Count; _displayRevision++; Targets.Clear();
            foreach (var item in ItemManager.Items.Values) if (item && item.Creature && !item.Creature.IsDead) Targets.Add(item.Creature);
            foreach (var p in PlayerManager.Players) if (p && p != Player.LocalPlayer) Targets.Add(p);
        }
        public static IReadOnlyList<Component> DisplayTargets { get { Refresh(); return DisplayCatalog; } }
        public static int DisplayRevision { get { return _displayRevision; } }
        public static CapsuleCollider PlayerCollider(Player player)
        {
            if (!player || !player.Other) return null;
            if (_capsule == null) _capsule = AccessTools.FieldRefAccess<OtherPlayer,CapsuleCollider>("_col");
            return _capsule(player.Other);
        }
        public static bool Point(Component target, out Vector3 point)
        {
            if (target is Player && !_targetTeammates) { point = default(Vector3); return false; }
            return DisplayPoint(target, out point, true);
        }
        public static bool DisplayPoint(Component target, out Vector3 point, bool requirePlayerCollider)
        {
            point = default(Vector3);
            if (!target || !target.gameObject.activeInHierarchy) return false;
            var c = target as Creature;
            if (c)
            {
                if (!IsAliveCreature(c)) return false;
                point = c.Rig ? c.Rig.worldCenterOfMass : c.transform.position; return Finite(point);
            }
            var p = target as Player;
            if (!p || p == Player.LocalPlayer || p.IsDeinitializing || !p.Dying || p.Dying.IsDead || !p.Other) return false;
            var col = PlayerCollider(p);
            if (col && col.enabled && col.gameObject.activeInHierarchy) point = col.bounds.center;
            else if (requirePlayerCollider) return false;
            else point = p.transform.position;
            return Finite(point);
        }
        private static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z)
            && !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);
        public static bool AimPoint(Component target, Vector3 muzzle, bool head, out Vector3 point)
        {
            if (!Point(target, out point)) return false;
            if (!head) return true;
            if (target is Creature) return PredictiveAimRuntime.HeadPoint(target, muzzle, out point);
            var p = target as Player; if (!p || !p.Other) return false;
            var col = _capsule(p.Other); if (!col) return false;
            point += Vector3.up * col.bounds.extents.y * .65f; return true;
        }
        public static bool Clear(Vector3 origin, Vector3 point) => !Physics.Linecast(origin, point, (int)GameInfo.LevelLayer | (int)GameInfo.BoatLayer, QueryTriggerInteraction.Ignore);
        public static Vector3 LaunchDirection(Component target, Vector3 point, Vector3 origin, Vector3 velocity, float gravity, bool hitscan)
        {
            Vector3 relative=point-origin;
            if(hitscan || !PredictiveAimRuntime.SilentLeadEnabled())return relative;
            Vector3 motion=PredictiveAimRuntime.SilentVelocity(target,point); NVector solved; double time;
            if(!PredictiveBallistics.TryIntercept(new NVector(relative.x,relative.y,relative.z),new NVector(motion.x,motion.y,motion.z),velocity.magnitude,gravity,Time.fixedDeltaTime,8,out solved,out time))return relative;
            if(!Clear(origin,point+motion*(float)time))return relative;
            return new Vector3(solved.X,solved.Y,solved.Z);
        }
        public static Component Select(Vector3 origin, Vector3 forward, Vector3 muzzle, float distance, float angle, bool head, out Vector3 point)
        {
            SelectionScans++;Refresh(); point = default(Vector3); Component best = null; int rank = 3; float bestDistance = float.MaxValue;
            float cos = Mathf.Cos(angle * Mathf.Deg2Rad), distanceSquared = distance * distance;
            bool sameOrigin = origin.x == muzzle.x && origin.y == muzzle.y && origin.z == muzzle.z;
            foreach (var t in Targets)
            {
                Vector3 candidate; if (!Point(t, out candidate)) continue;
                Vector3 delta = candidate - origin; float d = delta.sqrMagnitude;
                if (d < .0001f || d > distanceSquared || Vector3.Dot(forward, delta) / Mathf.Sqrt(d) < cos) continue;
                int priority = Priority(t); if (GameplayPolicy.Compare(priority, d, rank, bestDistance) >= 0) continue;
                // Body selection already has the validated point and cone/distance result.
                if (head)
                {
                    if (!AimPoint(t, muzzle, true, out candidate)) continue;
                    Vector3 aimDelta = candidate - origin; float aimDistance = aimDelta.sqrMagnitude;
                    if (aimDistance < .0001f || aimDistance > distanceSquared || Vector3.Dot(forward, aimDelta) / Mathf.Sqrt(aimDistance) < cos) continue;
                }
                // Exact equality only: nearby camera/muzzle origins may have different occlusion.
                if (!Clear(origin, candidate) || (!sameOrigin && !Clear(muzzle, candidate))) continue;
                best = t; rank = priority; bestDistance = d; point = candidate;
            }
            return best;
        }
    }
    public sealed class GameplayLaunch
    {
        public uint StartId;
        public Component Target;
        public float Angle, Distance, Strength;
        public bool Head;
        public float NextScan, PointTime;
        public Vector3 CachedPoint;
        public bool HasPoint;
    }
    public sealed class GameplayFlight { public GameplayLaunch Volley; public float LastSeen; }
    public sealed class GameplayExplosion { public Player Owner; public readonly Dictionary<Creature,int> Damage = new Dictionary<Creature,int>(); }
}
