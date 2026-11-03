using System;
using System.Reflection;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public static class MovementSafetyRuntime
    {
        private static FieldInfo bridgeField, stoppedField, restoreField, rejectedField, bobField, colliderField, rigidbodyField, jumpField, swimmingField, distanceField;
        private static float nextWarning;
        public static void Install(Harmony harmony, string target)
        {
            if (target != "Attachments:get_Damage") return;
            MethodInfo bob = null, move = null, bobHandler = null, moveHandler = null; bool addedBob = false, addedMove = false;
            try
            {
                bridgeField = AccessTools.Field(typeof(Bootstrap), "_bridge");
                stoppedField = AccessTools.Field(typeof(TrainerBridgeHost), "_stopped");
                restoreField = AccessTools.Field(typeof(TrainerBridgeHost), "_restoreRequested");
                rejectedField = AccessTools.Field(typeof(TrainerBridgeHost), "_channelRejected");
                bobField = AccessTools.Field(typeof(PlayerHolding), "_bobOffset");
                colliderField = AccessTools.Field(typeof(PlayerMovement), "_col");
                rigidbodyField = AccessTools.Field(typeof(PlayerMovement), "_rig");
                jumpField = AccessTools.Field(typeof(PlayerMovement), "_hasJumped");
                swimmingField = AccessTools.Field(typeof(PlayerMovement), "_isSwimming");
                distanceField = AccessTools.Field(typeof(PlayerMovement), "_groundCheckDist");
                bob = AccessTools.Method(typeof(PlayerHolding), "GetBobOffset");
                move = AccessTools.Method(typeof(PlayerMovement), "Move");
                bobHandler = AccessTools.Method(typeof(MovementSafetyRuntime), "AfterRadarBob");
                moveHandler = AccessTools.Method(typeof(MovementSafetyRuntime), "AfterMovement");
                if (bridgeField == null || stoppedField == null || restoreField == null || rejectedField == null || bobField == null || colliderField == null || rigidbodyField == null || jumpField == null || swimmingField == null || distanceField == null || bob == null || move == null || bobHandler == null || moveHandler == null)
                    throw new MissingMemberException("Movement/radar native contract unavailable");
                if (!HasPostfix(bob, bobHandler)) { addedBob = true; harmony.Patch(bob, postfix: new HarmonyMethod(bobHandler)); }
                if (!HasPostfix(move, moveHandler)) { addedMove = true; harmony.Patch(move, postfix: new HarmonyMethod(moveHandler)); }
            }
            catch (Exception e)
            {
                if (addedBob && bob != null && bobHandler != null) try { harmony.Unpatch(bob, bobHandler); } catch { }
                if (addedMove && move != null && moveHandler != null) try { harmony.Unpatch(move, moveHandler); } catch { }
                Debug.LogWarning("[AbyssalCrab] 雷达与坡面适配不可用: " + e.Message);
            }
        }
        private static bool HasPostfix(MethodInfo original, MethodInfo handler)
        {
            var info = Harmony.GetPatchInfo(original); if (info == null) return false;
            foreach (var patch in info.Postfixes) if (patch.PatchMethod == handler) return true;
            return false;
        }
        private static void Warn(string context, Exception error)
        {
            if (Time.unscaledTime < nextWarning) return;
            nextWarning = Time.unscaledTime + 2f;
            Debug.LogWarning("[AbyssalCrab] " + context + ": " + error.Message);
        }
        private static TrainerBridgeHost ActiveBridge()
        {
            var bridge = bridgeField == null ? null : bridgeField.GetValue(null) as TrainerBridgeHost;
            if (bridge == null || stoppedField == null || restoreField == null || rejectedField == null ||
                (bool)stoppedField.GetValue(bridge) || (bool)restoreField.GetValue(bridge) || (bool)rejectedField.GetValue(bridge)) return null;
            return bridge;
        }
        public static void AfterRadarBob(PlayerHolding __instance)
        {
            try
            {
                var p = Player.LocalPlayer;
                if (ActiveBridge() == null || !p || p.Holding != __instance || !(__instance.HeldItem is Map) || !p.Movement) return;
                var velocity = p.Movement.Velocity;
                if (p.Movement.Sprinting || velocity.x * velocity.x + velocity.z * velocity.z > 64f)
                    bobField.SetValue(__instance, Vector3.zero);
            }
            catch (Exception e) { Warn("雷达摆动适配失败", e); }
        }
        public static Vector3 LimitUphill(Vector3 velocity, Vector3 normal, float maximum)
        {
            float horizontal = (float)Math.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            if (horizontal <= maximum || normal.y <= 0f || normal.y >= 0.99939f || velocity.x * normal.x + velocity.z * normal.z >= -0.01f * horizontal) return velocity;
            float scale = maximum / horizontal;
            return new Vector3(velocity.x * scale, velocity.y, velocity.z * scale);
        }
        public static void AfterMovement(PlayerMovement __instance)
        {
            try
            {
                var bridge = ActiveBridge(); var p = Player.LocalPlayer;
                if (bridge == null || !p || p.Movement != __instance || !__instance.Grounded || __instance.OnBoat || (bool)jumpField.GetValue(__instance) || (bool)swimmingField.GetValue(__instance) ||
                    !(bridge.IsLockEnabled("player.move.multiplier") || bridge.IsLockEnabled("player.sprint.multiplier"))) return;
                var col = colliderField.GetValue(__instance) as CapsuleCollider; var rig = rigidbodyField.GetValue(__instance) as Rigidbody;
                if (!col || !rig) return;
                var velocity = rig.linearVelocity;
                // Match LimitUphill's calculation so values at the threshold retain identical behavior.
                if ((float)Math.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z) <= 12f) return;
                RaycastHit hit;
                var start = __instance.CurFeetWorldPos + Vector3.up * (col.radius + col.center.y);
                if (!Physics.SphereCast(start, col.radius * .9f, Vector3.down, out hit, (float)distanceField.GetValue(__instance), GameInfo.CanJumpOnLayers) || !hit.transform || Vector3.Angle(Vector3.up, hit.normal) >= __instance.SlipAngle) return;
                rig.linearVelocity = LimitUphill(velocity, hit.normal, 12f);
            }
            catch (Exception e) { Warn("上坡速度保护失败", e); }
        }
    }
}
