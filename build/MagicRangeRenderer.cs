using System;
using System.Collections.Generic;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbyssalCrab.Runtime
{
    public sealed class MagicRangeRenderer : MonoBehaviour
    {
        private static MagicRangeRenderer _instance;
        private static TrainerBridgeHost _host;
        private static volatile bool _active;
        private static float _angle, _distance = 200;
        private static readonly Vector2[] Circle = BuildCircle();
        private readonly Dictionary<Component,EntityOverlayVisual> _visuals = new Dictionary<Component,EntityOverlayVisual>();
        private readonly List<Component> _expired = new List<Component>();
        private readonly HashSet<Component> _catalog = new HashSet<Component>();
        private readonly Vector3[] _corners = new Vector3[8], _projected = new Vector3[8];
        private int _catalogRevision = -1;
        private static Vector2[] BuildCircle()
        {
            var points = new Vector2[97];
            for (int i = 0; i < points.Length; i++)
            { double a = i * Math.PI * 2 / 96; points[i] = new Vector2((float)Math.Cos(a), (float)Math.Sin(a)); }
            return points;
        }
        public static void Configure(TrainerBridgeHost host, float angle)
        { _angle = angle; Refresh(host); }
        public static void ConfigureDistance(TrainerBridgeHost host, float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 1 || distance > 1000)
                throw new ArgumentOutOfRangeException("distance", "实体显示距离必须在 1 到 1000 米之间。");
            _distance = distance;
        }
        public static void Refresh(TrainerBridgeHost host)
        {
            _host = host;
            _active = host != null && (PredictiveAimRuntime.Enabled || host.IsLockEnabled("combat.silent_aim"));
            if (!_active || _instance) return;
            var display = new GameObject("AbyssalCrab Magic Range");
            display.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(display);
            _instance = display.AddComponent<MagicRangeRenderer>();
        }
        // RestoreAll can run outside Unity's thread. Destruction stays in the next OnGUI event.
        public static void Stop() { _active = false; }
        private void OnDestroy()
        {
            _visuals.Clear(); _expired.Clear(); _catalog.Clear();
            if (ReferenceEquals(_instance, this)) { _instance = null; _active = false; _host = null; }
        }
        private void OnGUI()
        {
            if (!_active || !ReferenceEquals(_instance, this))
            {
                if (ReferenceEquals(_instance,this)) { _instance=null; _host=null; }
                UnityEngine.Object.Destroy(gameObject); return;
            }
            if (Event.current == null || Event.current.type != EventType.Repaint || !Application.isFocused ||
                _host == null) return;
            if (IslandManager.IsLoading) { _visuals.Clear(); _expired.Clear(); _catalog.Clear(); return; }
            bool silent = _host.IsLockEnabled("combat.silent_aim");
            if (!silent && !PredictiveAimRuntime.Enabled) { Stop(); return; }
            var player = Player.LocalPlayer;
            var camera = EntityOverlayProjection.DisplayCamera(_host);
            if (!player || player.IsDeinitializing || !player.Dying || player.Dying.IsDead || player.BlockInputs ||
                !player.Camera || !player.Camera.MouseLocked || !camera || !camera.isActiveAndEnabled) return;
            Rect viewport = camera.pixelRect;
            if (viewport.width <= 0 || viewport.height <= 0) return;
            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            try
            {
                var keyboard = Keyboard.current;
                if (keyboard == null || !keyboard.rightCtrlKey.isPressed)
                    DrawEntities(camera, viewport, previousMatrix);
                if (silent && player.Holding && player.Holding.HeldItem && player.Holding.HeldItem.Weapon)
                    DrawRange(camera, viewport, previousMatrix);
            }
            finally { GUI.matrix = previousMatrix; GUI.color = previousColor; }
        }
        private void DrawRange(Camera camera, Rect viewport, Matrix4x4 matrix)
        {
            double radius; bool abbreviated;
            if (!MagicRangePolicy.Project(_angle, camera.fieldOfView, viewport.width, viewport.height, out radius, out abbreviated)) return;
            float cx = viewport.x + viewport.width * .5f, cy = Screen.height - viewport.y - viewport.height * .5f;
            float r = (float)radius;
            GUI.color = new Color(.35f, .9f, 1, .65f);
            for (int i = 0; i < Circle.Length - 1; i++)
            {
                if (abbreviated && i % 4 >= 2) continue;
                GuiLine(new Vector2(cx,cy) + Circle[i] * r, new Vector2(cx,cy) + Circle[i+1] * r, 1.5f, matrix);
            }
            GUI.matrix = matrix;
            if (abbreviated)
                GUI.Label(new Rect(cx - 160, cy + r + 5, 320, 28), "追踪角度超出画面 · 虚线圈为缩略示意");
        }
        private void DrawEntities(Camera camera, Rect viewport, Matrix4x4 matrix)
        {
            var aimTarget = PredictiveAimRuntime.CurrentLock();
            var shotTarget = GameplayRuntime.HighlightedShotTarget();
            var targets = GameplayTargeting.DisplayTargets;
            int revision = GameplayTargeting.DisplayRevision;
            if (_catalogRevision != revision)
            {
                _catalogRevision = revision; _expired.Clear(); _catalog.Clear();
                for (int i=0;i<targets.Count;i++) _catalog.Add(targets[i]);
                foreach (var pair in _visuals)
                {
                    Vector3 point;
                    if (!_catalog.Contains(pair.Key) || !GameplayTargeting.DisplayPoint(pair.Key, out point, false)) _expired.Add(pair.Key);
                }
                foreach (var target in _expired) _visuals.Remove(target);
            }
            Vector2 aim;
            bool haveAim = EntityOverlayProjection.TryAimPoint(_host, camera, out aim);
            Vector3 origin = camera.transform.position;
            float maximum = _distance * _distance;
            for (int i = 0; i < targets.Count; i++)
            {
                Component target = targets[i]; Vector3 point;
                if (!GameplayTargeting.DisplayPoint(target, out point, false) || (point-origin).sqrMagnitude > maximum) continue;
                EntityOverlayVisual visual;
                if (!_visuals.TryGetValue(target, out visual))
                { visual = new EntityOverlayVisual(target); _visuals.Add(target, visual); }
                visual.Refresh(revision);
                Bounds bounds; Rect box;
                if (visual.TryBounds(out bounds))
                {
                    if (!ProjectBox(camera,viewport,bounds,out box)) continue;
                }
                else
                {
                    Vector3 screen = camera.WorldToScreenPoint(point);
                    if (!EntityOverlayGeometry.Finite(screen.x) || !EntityOverlayGeometry.Finite(screen.y) ||
                        !EntityOverlayGeometry.Finite(screen.z) || screen.z < camera.nearClipPlane) continue;
                    float left=Math.Max(viewport.xMin,screen.x-4), right=Math.Min(viewport.xMax,screen.x+4);
                    float bottom=Math.Max(viewport.yMin,screen.y-6), top=Math.Min(viewport.yMax,screen.y+6);
                    if (right <= left || top <= bottom) continue;
                    box=Rect.MinMaxRect(left,bottom,right,top);
                }
                GUI.color = Color.red;
                ScreenLine(new Vector2(box.xMin,box.yMin),new Vector2(box.xMax,box.yMin),viewport,matrix);
                ScreenLine(new Vector2(box.xMax,box.yMin),new Vector2(box.xMax,box.yMax),viewport,matrix);
                ScreenLine(new Vector2(box.xMax,box.yMax),new Vector2(box.xMin,box.yMax),viewport,matrix);
                ScreenLine(new Vector2(box.xMin,box.yMax),new Vector2(box.xMin,box.yMin),viewport,matrix);
                for (int bone = 0; bone < visual.Bones.Count; bone++)
                {
                    var from = visual.Bones[bone]; var to = visual.Parents[bone];
                    if (from && to && from.gameObject.activeInHierarchy && to.gameObject.activeInHierarchy)
                        WorldLine(camera, box, from.position, to.position, matrix);
                }
                if (haveAim)
                {
                    GUI.color = new Color(1,0,0,.55f);
                    ScreenLine(aim, box.center, viewport, matrix);
                }
                if (ReferenceEquals(target, aimTarget) || ReferenceEquals(target, shotTarget))
                    DrawLockCorners(box, matrix);
            }
        }
        private static void DrawLockCorners(Rect box, Matrix4x4 matrix)
        {
            float length = Mathf.Min(10f, Mathf.Min(box.width, box.height) * .35f);
            float thickness = Mathf.Min(2f, length);
            float top = Screen.height - box.yMax, bottom = Screen.height - box.yMin;
            GUI.matrix = matrix;
            GUI.color = new Color(1f, .84f, .2f, 1f);
            for (int i = 0; i < 4; i++)
            {
                bool left = (i & 1) == 0, upper = (i & 2) == 0;
                GUI.DrawTexture(new Rect(left ? box.xMin : box.xMax - length, upper ? top : bottom - thickness,
                    length, thickness), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(left ? box.xMin : box.xMax - thickness, upper ? top : bottom - length,
                    thickness, length), Texture2D.whiteTexture);
            }
        }
        private static bool ProjectSegment(Camera camera, Vector3 from, Vector3 to, out Vector3 a, out Vector3 b)
        {
            a = camera.WorldToScreenPoint(from); b = camera.WorldToScreenPoint(to);
            float start, end;
            if (!EntityOverlayGeometry.ClipDepth(a.z,b.z,camera.nearClipPlane,out start,out end)) return false;
            if (start > 0) a = camera.WorldToScreenPoint(Vector3.LerpUnclamped(from,to,start));
            if (end < 1) b = camera.WorldToScreenPoint(Vector3.LerpUnclamped(from,to,end));
            return EntityOverlayGeometry.Finite(a.x) && EntityOverlayGeometry.Finite(a.y) && EntityOverlayGeometry.Finite(b.x) && EntityOverlayGeometry.Finite(b.y);
        }
        private static void WorldLine(Camera camera, Rect box, Vector3 from, Vector3 to, Matrix4x4 matrix)
        {
            Vector3 a,b;
            if (ProjectSegment(camera,from,to,out a,out b)) ScreenLine(new Vector2(a.x,a.y),new Vector2(b.x,b.y),box,matrix);
        }
        private bool ProjectBox(Camera camera, Rect viewport, Bounds bounds, out Rect box)
        {
            box = default(Rect);
            for (int i = 0; i < 8; i++)
            {
                _corners[i] = bounds.center + Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                _projected[i] = camera.WorldToScreenPoint(_corners[i]);
            }
            float left=float.PositiveInfinity, bottom=float.PositiveInfinity, right=float.NegativeInfinity, top=float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
                for (int bit = 1; bit <= 4; bit <<= 1)
                {
                    if ((i & bit) != 0) continue;
                    int j = i | bit;
                    Vector3 a = _projected[i], b = _projected[j]; float start,end;
                    if (!EntityOverlayGeometry.ClipDepth(a.z,b.z,camera.nearClipPlane,out start,out end)) continue;
                    if (start > 0) a = camera.WorldToScreenPoint(Vector3.LerpUnclamped(_corners[i],_corners[j],start));
                    if (end < 1) b = camera.WorldToScreenPoint(Vector3.LerpUnclamped(_corners[i],_corners[j],end));
                    if (!EntityOverlayGeometry.Finite(a.x) || !EntityOverlayGeometry.Finite(a.y) || !EntityOverlayGeometry.Finite(b.x) || !EntityOverlayGeometry.Finite(b.y)) continue;
                    left=Math.Min(left,Math.Min(a.x,b.x)); right=Math.Max(right,Math.Max(a.x,b.x));
                    bottom=Math.Min(bottom,Math.Min(a.y,b.y)); top=Math.Max(top,Math.Max(a.y,b.y));
                }
            left=Math.Max(left,viewport.xMin); right=Math.Min(right,viewport.xMax);
            bottom=Math.Max(bottom,viewport.yMin); top=Math.Min(top,viewport.yMax);
            if (right <= left || top <= bottom) return false;
            box = Rect.MinMaxRect(left,bottom,right,top); return true;
        }
        private static void ScreenLine(Vector2 from, Vector2 to, Rect viewport, Matrix4x4 matrix)
        {
            float x0=from.x,y0=from.y,x1=to.x,y1=to.y;
            if (!EntityOverlayGeometry.ClipLine(ref x0,ref y0,ref x1,ref y1,viewport.xMin,viewport.yMin,viewport.xMax,viewport.yMax)) return;
            if ((x1-x0)*(x1-x0)+(y1-y0)*(y1-y0) < .01f) return;
            GuiLine(new Vector2(x0,Screen.height-y0),new Vector2(x1,Screen.height-y1),1,matrix);
        }
        private static void GuiLine(Vector2 from, Vector2 to, float width, Matrix4x4 matrix)
        {
            Vector2 delta = to-from;
            GUI.matrix = matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,from);
            GUI.DrawTexture(new Rect(from.x,from.y-width*.5f,delta.magnitude,width),Texture2D.whiteTexture);
        }
    }

    public sealed class EntityOverlayVisual
    {
        private static readonly AccessTools.FieldRef<Item,List<Renderer>> ItemRenderers = AccessTools.FieldRefAccess<Item,List<Renderer>>("_renderers");
        private static readonly AccessTools.FieldRef<Item,Collider[]> ItemColliders = AccessTools.FieldRefAccess<Item,Collider[]>("_worldColliders");
        private static readonly AccessTools.FieldRef<PlayerBody,GameObject> OldCharacter = AccessTools.FieldRefAccess<PlayerBody,GameObject>("_oldCharacter");
        private static readonly AccessTools.FieldRef<PlayerBody,GameObject> OldHead = AccessTools.FieldRefAccess<PlayerBody,GameObject>("_oldHead");
        private static readonly AccessTools.FieldRef<PlayerSkin,SkinnedMeshRenderer>[] PlayerRenderers = {
            AccessTools.FieldRefAccess<PlayerSkin,SkinnedMeshRenderer>("_bodyRenderer"), AccessTools.FieldRefAccess<PlayerSkin,SkinnedMeshRenderer>("_leftHand"),
            AccessTools.FieldRefAccess<PlayerSkin,SkinnedMeshRenderer>("_rightHand"), AccessTools.FieldRefAccess<PlayerSkin,SkinnedMeshRenderer>("_outfitRenderer"),
            AccessTools.FieldRefAccess<PlayerSkin,SkinnedMeshRenderer>("_hatRenderer"), AccessTools.FieldRefAccess<PlayerSkin,SkinnedMeshRenderer>("_accessoryRenderer") };
        private readonly Component _target;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Transform> _roots = new List<Transform>();
        private readonly List<Renderer> _candidates = new List<Renderer>();
        private readonly List<Collider> _colliders = new List<Collider>();
        public readonly List<Transform> Bones = new List<Transform>();
        public readonly List<Transform> Parents = new List<Transform>();
        private PlayerBody _body;
        private GameObject _oldCharacter, _oldHead;
        private bool _oldModel;
        private int _revision = -1;

        public EntityOverlayVisual(Component target) { _target = target; }
        public void Refresh(int revision)
        {
            var player = _target as Player;
            var body = player ? player.Body : null;
            var oldCharacter = body ? OldCharacter(body) : null;
            var oldHead = body ? OldHead(body) : null;
            bool oldModel = oldCharacter && oldCharacter.activeInHierarchy || oldHead && oldHead.activeInHierarchy;
            bool modelChanged = !ReferenceEquals(body,_body) || !ReferenceEquals(oldCharacter,_oldCharacter) ||
                !ReferenceEquals(oldHead,_oldHead) || oldModel != _oldModel;
            bool meshChanged = false;
            for (int i=0;i<_renderers.Count;i++)
            {
                var skin = _renderers[i] as SkinnedMeshRenderer;
                if (skin && (!ReferenceEquals(skin.sharedMesh,_meshes[i]) || !ReferenceEquals(skin.rootBone,_roots[i])))
                { meshChanged=true; break; }
            }
            if (_revision == revision && !modelChanged && !meshChanged) return;
            _revision = revision; _candidates.Clear(); _colliders.Clear();
            var creature = _target as Creature;
            if (creature)
            {
                var renderers = ItemRenderers(creature);
                if (renderers != null) foreach (var renderer in renderers) AddRenderer(renderer);
                var colliders = ItemColliders(creature);
                if (colliders != null) _colliders.AddRange(colliders);
            }
            else if (player)
            {
                if (oldModel)
                {
                    if (!modelChanged && _oldModel)
                    {
                        bool intact = true;
                        foreach (var renderer in _renderers) if (!renderer) { intact = false; break; }
                        if (intact) _candidates.AddRange(_renderers);
                        else AddOldRenderers(oldCharacter,oldHead);
                    }
                    else AddOldRenderers(oldCharacter,oldHead);
                }
                else if (player.Skin)
                    foreach (var read in PlayerRenderers) AddRenderer(read(player.Skin));
                var collider = GameplayTargeting.PlayerCollider(player);
                if (collider) _colliders.Add(collider);
            }
            _body=body; _oldCharacter=oldCharacter; _oldHead=oldHead; _oldModel=oldModel;
            bool changed = modelChanged || meshChanged || _candidates.Count != _renderers.Count;
            if (!changed) for (int i=0;i<_renderers.Count;i++) if (!ReferenceEquals(_renderers[i],_candidates[i])) { changed=true; break; }
            if (!changed) return;
            _renderers.Clear(); _renderers.AddRange(_candidates); _meshes.Clear(); _roots.Clear(); Bones.Clear(); Parents.Clear();
            var bones = new HashSet<Transform>();
            foreach (var renderer in _renderers)
            {
                var skin = renderer as SkinnedMeshRenderer;
                var mesh = skin ? skin.sharedMesh : null;
                var root = skin ? skin.rootBone : null;
                _meshes.Add(mesh); _roots.Add(root);
                if (!mesh) continue;
                foreach (var bone in skin.bones) if (bone) bones.Add(bone);
                if (root) bones.Add(root);
            }
            foreach (var bone in bones)
            {
                var parent = bone.parent;
                while (parent && !bones.Contains(parent)) parent = parent.parent;
                if (parent) { Bones.Add(bone); Parents.Add(parent); }
            }
        }
        private void AddRenderer(Renderer renderer)
        {
            if (renderer && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && !_candidates.Contains(renderer))
                _candidates.Add(renderer);
        }
        private void AddOldRenderers(GameObject character, GameObject head)
        {
            if (character) foreach (var renderer in character.GetComponentsInChildren<Renderer>(true)) AddRenderer(renderer);
            if (head) foreach (var renderer in head.GetComponentsInChildren<Renderer>(true)) AddRenderer(renderer);
        }
        private static bool ValidBounds(Bounds bounds)
        {
            var min = bounds.min; var max = bounds.max;
            return EntityOverlayGeometry.Finite(min.x) && EntityOverlayGeometry.Finite(min.y) && EntityOverlayGeometry.Finite(min.z)
                && EntityOverlayGeometry.Finite(max.x) && EntityOverlayGeometry.Finite(max.y) && EntityOverlayGeometry.Finite(max.z)
                && max.x >= min.x && max.y >= min.y && max.z >= min.z
                && (max.x > min.x || max.y > min.y || max.z > min.z);
        }
        public bool TryBounds(out Bounds bounds)
        {
            bounds = default(Bounds); bool found = false;
            foreach (var renderer in _renderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var current = renderer.bounds;
                if (!ValidBounds(current)) continue;
                if (!found) { bounds=current; found=true; } else bounds.Encapsulate(current);
            }
            if (found && ValidBounds(bounds)) return true;
            bounds=default(Bounds); found=false;
            foreach (var collider in _colliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                var current = collider.bounds;
                if (!ValidBounds(current)) continue;
                if (!found) { bounds=current; found=true; } else bounds.Encapsulate(current);
            }
            return found && ValidBounds(bounds);
        }
    }

    public static class EntityOverlayGeometry
    {
        public static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        public static bool ClipDepth(float a, float b, float near, out float start, out float end)
        {
            start=0; end=1;
            if (!Finite(a) || !Finite(b) || !Finite(near) || near <= 0 || a < near && b < near) return false;
            if (a < near) start=(float)(((double)near-a)/((double)b-a));
            else if (b < near) end=(float)(((double)near-a)/((double)b-a));
            return true;
        }
        private static bool Boundary(double p, double q, ref double start, ref double end)
        {
            if (p == 0) return q >= 0;
            double t=q/p;
            if (p < 0) { if (t > end) return false; if (t > start) start=t; }
            else { if (t < start) return false; if (t < end) end=t; }
            return true;
        }
        public static bool ClipLine(ref float x0, ref float y0, ref float x1, ref float y1, float left, float bottom, float right, float top)
        {
            if (!Finite(x0) || !Finite(y0) || !Finite(x1) || !Finite(y1) || !Finite(left) || !Finite(bottom) ||
                !Finite(right) || !Finite(top) || right <= left || top <= bottom) return false;
            double x=x0,y=y0,dx=(double)x1-x0,dy=(double)y1-y0,start=0,end=1;
            if (!Boundary(-dx,x-left,ref start,ref end) || !Boundary(dx,right-x,ref start,ref end) ||
                !Boundary(-dy,y-bottom,ref start,ref end) || !Boundary(dy,top-y,ref start,ref end)) return false;
            x1=(float)(x+end*dx); y1=(float)(y+end*dy); x0=(float)(x+start*dx); y0=(float)(y+start*dy);
            return true;
        }
    }
}
