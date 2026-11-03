using System;
using HowToFishTrainer.Runtime;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public sealed class MagicRangeRenderer : MonoBehaviour
    {
        private static MagicRangeRenderer _instance;
        private static TrainerBridgeHost _host;
        private static volatile bool _active;
        private static float _angle;
        private static readonly Vector2[] Circle = BuildCircle();
        private static Vector2[] BuildCircle()
        {
            var points = new Vector2[97];
            for (int i = 0; i < points.Length; i++)
            { double a = i * Math.PI * 2 / 96; points[i] = new Vector2((float)Math.Cos(a), (float)Math.Sin(a)); }
            return points;
        }
        public static void Configure(TrainerBridgeHost host, float angle)
        {
            _host = host; _angle = angle;
            if (!_instance)
            {
                var display = new GameObject("AbyssalCrab Magic Range");
                display.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(display);
                _instance = display.AddComponent<MagicRangeRenderer>();
            }
            _active = true;
        }
        // RestoreAll can run outside Unity's thread. Destruction stays in the next OnGUI event.
        public static void Stop() { _active = false; }
        private void OnDestroy() { if (ReferenceEquals(_instance, this)) { _instance = null; _active = false; _host = null; } }
        private void OnGUI()
        {
            if (!_active || !ReferenceEquals(_instance, this))
            { if (ReferenceEquals(_instance, this)) _instance = null; UnityEngine.Object.Destroy(gameObject); return; }
            if (Event.current == null || Event.current.type != EventType.Repaint || !Application.isFocused ||
                _host == null || !_host.IsLockEnabled("combat.silent_aim") || IslandManager.IsLoading) return;
            var p = Player.LocalPlayer;
            var camera = GameInfo.CurCamera;
            if (!p || p.IsDeinitializing || !p.Dying || p.Dying.IsDead || p.BlockInputs || !p.Camera ||
                !p.Camera.MouseLocked || !p.Holding || !p.Holding.HeldItem || !p.Holding.HeldItem.Weapon || !camera || !camera.isActiveAndEnabled) return;
            Rect viewport = camera.pixelRect;
            double radius; bool abbreviated;
            if (!MagicRangePolicy.Project(_angle, camera.fieldOfView, viewport.width, viewport.height, out radius, out abbreviated)) return;
            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            try
            {
                // pixelRect has a bottom-left origin; IMGUI has a top-left origin.
                float cx = viewport.x + viewport.width * .5f, cy = Screen.height - viewport.y - viewport.height * .5f;
                float r = (float)radius;
                GUI.color = new Color(.35f, .9f, 1, .65f);
                for (int i = 0; i < Circle.Length - 1; i++)
                {
                    if (abbreviated && i % 4 >= 2) continue;
                    var from = new Vector2(cx, cy) + Circle[i] * r;
                    var delta = (Circle[i + 1] - Circle[i]) * r;
                    GUI.matrix = previousMatrix;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
                    GUI.DrawTexture(new Rect(from.x, from.y - .75f, delta.magnitude + .4f, 1.5f), Texture2D.whiteTexture);
                }
                GUI.matrix = previousMatrix;
                if (abbreviated)
                    GUI.Label(new Rect(cx - 160, cy + r + 5, 320, 28), "追踪角度超出画面 · 虚线圈为缩略示意");
            }
            finally { GUI.matrix = previousMatrix; GUI.color = previousColor; }
        }
    }
}
