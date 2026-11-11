using System;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public static class MagicRangePolicy
    {
        public static float CheckedDistance(JToken value)
        {
            if (value == null || value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
                throw new ArgumentException("显示距离需要数字");
            double distance = value.Value<double>();
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 1 || distance > 1000)
                throw new ArgumentOutOfRangeException("distance", "显示距离需要在 1–1000 米之间");
            return (float)distance;
        }
        // angle is the half-angle used by GameplayTargeting.Select, not a diameter in degrees.
        public static bool Project(double angle, double verticalFov, double width, double height, out double radius, out bool abbreviated)
        {
            radius = 0; abbreviated = false;
            if (double.IsNaN(angle) || double.IsInfinity(angle) || angle < .5 || angle > 180 ||
                double.IsNaN(verticalFov) || double.IsInfinity(verticalFov) || verticalFov <= 0 || verticalFov >= 180 ||
                double.IsNaN(width) || double.IsInfinity(width) || width <= 0 ||
                double.IsNaN(height) || double.IsInfinity(height) || height <= 0) return false;
            double limit = Math.Min(width, height) * .46;
            double projected = angle >= 90 ? double.PositiveInfinity : height * .5 * Math.Tan(angle * Math.PI / 180) / Math.Tan(verticalFov * Math.PI / 360);
            abbreviated = projected > limit;
            radius = abbreviated ? limit : projected;
            return radius > 0;
        }
    }

    public struct EntityAimFrame
    {
        public bool Projected, Visible, ThirdPerson;
        public float X, Y, Distance;
        public string Mode, Reason;
    }

    public static class EntityOverlayProjection
    {
        private static readonly AccessTools.FieldRef<TrainerBridgeHost, bool> Third = AccessTools.FieldRefAccess<TrainerBridgeHost, bool>("_thirdSuppressed");
        private static readonly AccessTools.FieldRef<TrainerBridgeHost, Camera> ThirdCamera = AccessTools.FieldRefAccess<TrainerBridgeHost, Camera>("_thirdCamera");
        private static readonly AccessTools.FieldRef<TrainerBridgeHost, RaycastHit[]> Hits = AccessTools.FieldRefAccess<TrainerBridgeHost, RaycastHit[]>("_aimHits");
        private static readonly AccessTools.FieldRef<Weapon, float> AimPercent = AccessTools.FieldRefAccess<Weapon, float>("_aimPercent");
        private static readonly AccessTools.FieldRef<Weapon, float> ForwardLength = AccessTools.FieldRefAccess<Weapon, float>("_modelForwardLength");

        public static Camera DisplayCamera(TrainerBridgeHost host)
        {
            var player = Player.LocalPlayer;
            var native = player && player.Camera ? player.Camera.Cam : null;
            var third = host != null ? ThirdCamera(host) : null;
            return host != null && Third(host) && third && third.enabled ? third : native;
        }

        public static EntityAimFrame ReadFrame(TrainerBridgeHost host)
        {
            var frame = new EntityAimFrame();
            var player = Player.LocalPlayer;
            var native = player && player.Camera ? player.Camera.Cam : null;
            if (!native || player.BlockInputs || player.Dying && player.Dying.IsDead ||
                player.CurCam != native || !player.Camera.MouseLocked) return frame;
            var camera = DisplayCamera(host);
            Vector3 origin = native.transform.position, direction = native.transform.forward;
            Transform held = null;
            frame.Mode = "视线方向";
            var item = player.Holding ? player.Holding.HeldItem : null;
            if (item)
            {
                held = item.transform;
                var weapon = item.Weapon;
                if (weapon)
                {
                    var attachment = weapon.Attachments;
                    var firePoint = attachment.FirePoint;
                    if (attachment.UseSniperUi && AimPercent(weapon) > .9f)
                        origin += direction * ForwardLength(weapon);
                    else { origin = firePoint.position; direction = firePoint.forward; }
                    frame.Mode = "枪口方向（不含散布与下坠）";
                }
            }
            var hits = Hits(host);
            int count = Physics.RaycastNonAlloc(origin, direction, hits, 250f, -5, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) { frame.Reason = "射线结果过多"; return frame; }
            float distance = 250f;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(((Component)player).transform) ||
                    held && hit.collider.transform.IsChildOf(held)) continue;
                distance = Math.Min(distance, hit.distance);
            }
            Vector3 point = camera.WorldToScreenPoint(origin + direction * distance);
            frame.Projected = true;
            frame.X = point.x / Math.Max(1, Screen.width);
            frame.Y = 1f - point.y / Math.Max(1, Screen.height);
            frame.Visible = camera.enabled && point.z > 0 && frame.X >= 0 && frame.X <= 1 && frame.Y >= 0 && frame.Y <= 1 && Application.isFocused;
            frame.ThirdPerson = Third(host); frame.Distance = distance;
            return frame;
        }

        public static bool TryAimPoint(TrainerBridgeHost host, Camera camera, out Vector2 point)
        {
            point = default(Vector2);
            if (host == null || !camera || camera != DisplayCamera(host)) return false;
            var frame = ReadFrame(host);
            if (!frame.Visible) return false;
            point = new Vector2(frame.X * Math.Max(1, Screen.width), (1f - frame.Y) * Math.Max(1, Screen.height));
            return true;
        }

        public static JToken Read(TrainerBridgeHost host)
        {
            var frame = ReadFrame(host);
            var result = new JObject { ["visible"] = frame.Visible };
            if (frame.Projected)
            {
                result["x"] = frame.X; result["y"] = frame.Y; result["mode"] = frame.Mode;
                result["thirdPerson"] = frame.ThirdPerson; result["distance"] = frame.Distance;
            }
            else if (frame.Reason != null) result["reason"] = frame.Reason;
            return result;
        }
    }
}
