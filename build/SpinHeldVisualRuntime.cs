using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    // Mesh/transform copies only: no Item, Animator, collider, input or network components.
    public static class SpinHeldVisualRuntime
    {
        private static GameObject _container;
        private static Transform _avatar;
        private static Player _player;
        private static Item _item;
        private static Transform _axisStart, _axisEnd;
        private static Vector3 _pivot;
        private static Quaternion _downRotation = Quaternion.identity;
        private static string _directionSource = "none";
        private static float _nativePositionError, _nativeRotationError;
        private static Transform[] _rodParts;
        private static Transform _rodTip;
        private static readonly FieldInfo RodParts = typeof(FishingRod).GetField("_rodParts", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RodTip = typeof(FishingRod).GetField("_tipJoint", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<Transform, Transform> Copies = new Dictionary<Transform, Transform>();
        private static readonly List<Transform> Sources = new List<Transform>(), Nodes = new List<Transform>();
        private static readonly List<bool> Roots = new List<bool>(), NativeHands = new List<bool>();
        // These transforms/renderers belong only to this helper; no Animator or gameplay component writes them.
        private static readonly List<Vector3> Positions = new List<Vector3>(), Scales = new List<Vector3>();
        private static readonly List<Quaternion> Rotations = new List<Quaternion>();
        private static readonly List<bool> Initialized = new List<bool>(), Active = new List<bool>(), Visible = new List<bool>();
        private static bool _containerVisible;
        private static readonly List<Renderer> RenderSources = new List<Renderer>(), RenderCopies = new List<Renderer>();
        private static readonly List<MaterialPropertyBlock> Properties = new List<MaterialPropertyBlock>();
        private static readonly List<SkinnedMeshRenderer> Skins = new List<SkinnedMeshRenderer>(), SkinCopies = new List<SkinnedMeshRenderer>();
        private static readonly List<MeshFilter> Filters = new List<MeshFilter>(), FilterCopies = new List<MeshFilter>();
        private static readonly List<Mesh> Meshes = new List<Mesh>();
        private static readonly List<int> ShapeCounts = new List<int>();
        private static readonly List<List<Material>> Materials = new List<List<Material>>();
        private static readonly List<Material> MaterialScratch = new List<Material>();
        private static readonly Dictionary<Renderer, bool> Hidden = new Dictionary<Renderer, bool>();

        public static int RendererCount { get { return RenderCopies.Count; } }
        public static string DirectionSource { get { return _directionSource; } }
        public static float NativePositionError { get { return _nativePositionError; } }
        public static float NativeRotationError { get { return _nativeRotationError; } }
        public static Vector3 DisplayDirection
        {
            get
            {
                Transform start, end;
                if (_axisStart == null || !Copies.TryGetValue(_axisStart, out start)) return Vector3.zero;
                return _axisEnd != null && Copies.TryGetValue(_axisEnd, out end) ?
                    (end.position - start.position).normalized : start.forward;
            }
        }

        public static Vector3 MapPosition(Vector3 position) { return _pivot + _downRotation * (position - _pivot); }
        public static Quaternion MapRotation(Quaternion rotation) { return _downRotation * rotation; }

        private static void PrepareDownPose(Player player)
        {
            _axisStart = _axisEnd = null; _directionSource = "none";
            _downRotation = Quaternion.identity;
            _pivot = Vector3.zero;
            if (_item == null) return;
            Weapon weapon = _item.Weapon;
            if (weapon != null && weapon.Attachments != null && weapon.Attachments.FirePoint != null)
            {
                _axisStart = weapon.Attachments.FirePoint; _directionSource = "muzzle";
            }
            else if (_item.FishingRod != null && RodParts != null)
            {
                if (_rodParts != null)
                    foreach (Transform part in _rodParts)
                    {
                        if (part == null) continue;
                        if (_axisStart == null) _axisStart = part;
                        else _axisEnd = part;
                    }
                if (_rodTip != null && _rodTip.IsChildOf(_item.transform)) _axisEnd = _rodTip;
                if (_axisStart != null && _axisEnd != null &&
                    (_axisEnd.position - _axisStart.position).sqrMagnitude > 0.000001f) _directionSource = "rod";
                else _axisStart = _axisEnd = null;
            }
            if (_axisStart == null)
            {
                _axisStart = _item.transform; _directionSource = "item";
            }
            Vector3 direction = _axisEnd == null ? _axisStart.forward : _axisEnd.position - _axisStart.position;
            if (direction.sqrMagnitude < 0.000001f) throw new NotSupportedException("持物朝向尚未就绪。");
            Transform right = player.Hands.HandBoneRight, left = player.Hands.HandBoneLeft;
            _pivot = right != null && left != null ? (right.position + left.position) * 0.5f : _item.transform.position;
            _downRotation = Quaternion.FromToRotation(direction, Vector3.down);
        }

        private static Renderer Hand(Player player, string field)
        {
            return (Renderer)typeof(PlayerHands).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player.Hands);
        }

        private static Transform CommonRoot(Renderer renderer)
        {
            Transform root = renderer.transform;
            SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
            if (skin == null) return root;
            foreach (Transform bone in skin.bones)
            {
                if (bone == null) continue;
                while (root != null && !bone.IsChildOf(root)) root = root.parent;
                if (root == null) throw new NotSupportedException("手掌网格的骨骼不在同一模型内。");
            }
            if (skin.rootBone != null)
                while (root != null && !skin.rootBone.IsChildOf(root)) root = root.parent;
            if (root == null) throw new NotSupportedException("手掌网格的根骨骼缺失。");
            return root;
        }

        private static void CopyTree(Transform source, Transform parent, bool root)
        {
            if (Copies.ContainsKey(source)) return;
            if (source is RectTransform) return;
            Transform copy = new GameObject(source.name).transform;
            copy.gameObject.hideFlags = HideFlags.HideAndDontSave;
            copy.gameObject.layer = source.gameObject.layer;
            copy.SetParent(parent, false);
            Copies.Add(source, copy); Sources.Add(source); Nodes.Add(copy); Roots.Add(root);
            Positions.Add(Vector3.zero); Rotations.Add(Quaternion.identity); Scales.Add(Vector3.one);
            Initialized.Add(false); Active.Add(true);
            for (int i = 0; i < source.childCount; i++) CopyTree(source.GetChild(i), copy, false);
        }

        private static void AddRoot(Transform source)
        {
            if (source != null && !Copies.ContainsKey(source)) CopyTree(source, _container.transform, true);
        }

        private static void Bind(GameObject avatar, Player player, Item item)
        {
            Clear();
            _avatar = avatar.transform; _player = player; _item = item;
            if (item != null && item.FishingRod != null)
            {
                _rodParts = RodParts == null ? null : RodParts.GetValue(item.FishingRod) as Transform[];
                Component tip = RodTip == null ? null : RodTip.GetValue(item.FishingRod) as Component;
                _rodTip = tip == null ? null : tip.transform;
            }
            _container = new GameObject("AbyssalCrab Studio Spin Held Visuals");
            _container.hideFlags = HideFlags.HideAndDontSave;
            _container.transform.SetParent(_avatar, false);
            _container.SetActive(false);
            Renderer right = Hand(player, "_handModelRight"), left = Hand(player, "_handModelLeft");
            if (right != null) AddRoot(CommonRoot(right));
            if (left != null) AddRoot(CommonRoot(left));
            if (item != null) AddRoot(item.transform);
            foreach (KeyValuePair<Transform, Transform> pair in Copies)
            foreach (Renderer source in pair.Key.GetComponents<Renderer>())
            {
                Renderer copy = null;
                SkinnedMeshRenderer skin = source as SkinnedMeshRenderer;
                MeshRenderer mesh = source as MeshRenderer;
                MeshFilter filter = null, filterCopy = null;
                if (skin != null)
                {
                    SkinnedMeshRenderer target = pair.Value.gameObject.AddComponent<SkinnedMeshRenderer>();
                    target.sharedMesh = skin.sharedMesh;
                    Transform[] bones = skin.sharedMesh == null ? new Transform[0] : skin.bones;
                    Transform[] mapped = new Transform[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        if (bones[i] != null && !Copies.TryGetValue(bones[i], out mapped[i]))
                            throw new NotSupportedException("持物显示网格引用了模型外的骨骼。");
                    target.bones = mapped;
                    if (skin.sharedMesh != null && skin.rootBone != null)
                    {
                        Transform rootBone;
                        if (!Copies.TryGetValue(skin.rootBone, out rootBone))
                            throw new NotSupportedException("持物显示网格引用了模型外的根骨骼。");
                        target.rootBone = rootBone;
                    }
                    target.localBounds = skin.localBounds;
                    target.updateWhenOffscreen = true;
                    copy = target;
                }
                else if (mesh != null)
                {
                    filter = source.GetComponent<MeshFilter>();
                    if (filter == null) continue;
                    filterCopy = pair.Value.gameObject.AddComponent<MeshFilter>();
                    filterCopy.sharedMesh = filter.sharedMesh;
                    copy = pair.Value.gameObject.AddComponent<MeshRenderer>();
                }
                if (copy == null) continue; // Never copy particle, trail or other gameplay renderers.
                var materials = new List<Material>();
                source.GetSharedMaterials(materials); copy.SetSharedMaterials(materials);
                copy.shadowCastingMode = source.shadowCastingMode;
                copy.receiveShadows = source.receiveShadows;
                copy.sortingLayerID = source.sortingLayerID; copy.sortingOrder = source.sortingOrder;
                RenderSources.Add(source); RenderCopies.Add(copy);
                copy.enabled = false; Visible.Add(false);
                NativeHands.Add(source == right || source == left);
                Properties.Add(new MaterialPropertyBlock());
                Skins.Add(skin); SkinCopies.Add(copy as SkinnedMeshRenderer);
                Filters.Add(filter); FilterCopies.Add(filterCopy);
                Mesh currentMesh = skin != null ? skin.sharedMesh : filter.sharedMesh;
                Meshes.Add(currentMesh);
                ShapeCounts.Add(skin != null && currentMesh != null ? currentMesh.blendShapeCount : 0);
                Materials.Add(materials);
            }
        }

        private static bool NeedsBinding(GameObject avatar, Player player, Item item)
        {
            if (_avatar != avatar.transform || _player != player || _item != item || _container == null) return true;
            foreach (Transform source in Sources) if (source == null) return true;
            for (int i = 0; i < RenderSources.Count; i++)
                if (RenderSources[i] == null || (Skins[i] != null ? Skins[i].sharedMesh != Meshes[i] : Filters[i] == null)) return true;
            return false;
        }

        private static void SyncMaterials(Renderer source, Renderer copy, List<Material> cached)
        {
            MaterialScratch.Clear(); source.GetSharedMaterials(MaterialScratch);
            bool changed = cached.Count != MaterialScratch.Count;
            for (int i = 0; !changed && i < cached.Count; i++) changed = cached[i] != MaterialScratch[i];
            if (!changed) return;
            copy.SetSharedMaterials(MaterialScratch);
            cached.Clear(); cached.AddRange(MaterialScratch);
        }

        public static void Capture(GameObject avatar, Player player)
        {
            Item item = player.Holding.HeldItem;
            if (NeedsBinding(avatar, player, item)) Bind(avatar, player, item);
            PrepareDownPose(player);
            if (_axisStart != null && (!Copies.ContainsKey(_axisStart) || (_axisEnd != null && !Copies.ContainsKey(_axisEnd))))
            {
                // An attachment can replace the active fire point without replacing the held item.
                Bind(avatar, player, item); PrepareDownPose(player);
                if (!Copies.ContainsKey(_axisStart) || (_axisEnd != null && !Copies.ContainsKey(_axisEnd)))
                    throw new NotSupportedException("持物朝向节点不在显示模型内。");
            }
            Vector3 originalPosition = item == null ? Vector3.zero : item.transform.position;
            Quaternion originalRotation = item == null ? Quaternion.identity : item.transform.rotation;
            Quaternion inverseAvatar = Quaternion.Inverse(_avatar.rotation);
            Vector3 avatarScale = _avatar.lossyScale;
            for (int i = 0; i < Sources.Count; i++)
            {
                Transform source = Sources[i], copy = Nodes[i];
                bool active = source.gameObject.activeSelf;
                if (Active[i] != active) { copy.gameObject.SetActive(active); Active[i] = active; }
                Vector3 position, scale;
                Quaternion rotation;
                if (Roots[i])
                {
                    position = _avatar.InverseTransformPoint(MapPosition(source.position));
                    rotation = inverseAvatar * MapRotation(source.rotation);
                    Vector3 native = source.lossyScale;
                    scale = new Vector3(native.x / avatarScale.x, native.y / avatarScale.y, native.z / avatarScale.z);
                }
                else
                {
                    position = source.localPosition; rotation = source.localRotation; scale = source.localScale;
                }
                // Exact comparisons preserve even small animation changes, without repeatedly dirtying a stable rig.
                if (!Initialized[i] || !Positions[i].Equals(position)) { copy.localPosition = position; Positions[i] = position; }
                if (!Initialized[i] || !Rotations[i].Equals(rotation)) { copy.localRotation = rotation; Rotations[i] = rotation; }
                if (!Initialized[i] || !Scales[i].Equals(scale)) { copy.localScale = scale; Scales[i] = scale; }
                Initialized[i] = true;
            }
            bool animatedHands = item != null && item.Tool != null && item.Tool.HandsMesh != null && item.Tool.HandsMesh.enabled;
            for (int i = 0; i < RenderSources.Count; i++)
            {
                Renderer source = RenderSources[i], copy = RenderCopies[i];
                bool hidden;
                if (!Hidden.TryGetValue(source, out hidden)) { hidden = source.forceRenderingOff; Hidden.Add(source, hidden); }
                if (Filters[i] != null && Filters[i].sharedMesh != Meshes[i])
                {
                    Meshes[i] = Filters[i].sharedMesh; FilterCopies[i].sharedMesh = Meshes[i];
                }
                bool visible = !hidden && Meshes[i] != null && source.gameObject.activeInHierarchy &&
                    (NativeHands[i] ? !animatedHands : source.enabled);
                if (visible)
                {
                    SyncMaterials(source, copy, Materials[i]);
                    source.GetPropertyBlock(Properties[i]); copy.SetPropertyBlock(Properties[i]);
                    if (Skins[i] != null)
                        for (int shape = 0; shape < ShapeCounts[i]; shape++)
                            SkinCopies[i].SetBlendShapeWeight(shape, Skins[i].GetBlendShapeWeight(shape));
                }
                if (Visible[i] != visible) { copy.enabled = visible; Visible[i] = visible; }
                // Leave enabled and every gameplay transform untouched; the game still animates/uses the real item.
                if (!source.forceRenderingOff) source.forceRenderingOff = true;
            }
            if (!_containerVisible) { _container.SetActive(true); _containerVisible = true; }
            _nativePositionError = item == null ? 0f : Vector3.Distance(originalPosition, item.transform.position);
            _nativeRotationError = item == null || originalRotation.Equals(item.transform.rotation) ? 0f :
                Quaternion.Angle(originalRotation, item.transform.rotation);
        }

        public static void Clear()
        {
            foreach (KeyValuePair<Renderer, bool> pair in Hidden)
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            Hidden.Clear();
            if (_container != null)
            {
                _container.SetActive(false); _container.transform.SetParent(null, true);
                UnityEngine.Object.Destroy(_container);
            }
            _container = null; _avatar = null; _player = null; _item = null;
            _containerVisible = false;
            _axisStart = _axisEnd = null; _directionSource = "none";
            _downRotation = Quaternion.identity; _pivot = Vector3.zero;
            _nativePositionError = _nativeRotationError = 0f;
            _rodParts = null; _rodTip = null;
            Copies.Clear(); Sources.Clear(); Nodes.Clear(); Roots.Clear();
            Positions.Clear(); Scales.Clear(); Rotations.Clear(); Initialized.Clear(); Active.Clear(); Visible.Clear();
            RenderSources.Clear(); RenderCopies.Clear(); NativeHands.Clear(); Properties.Clear();
            Skins.Clear(); SkinCopies.Clear(); Filters.Clear(); FilterCopies.Clear(); Meshes.Clear(); ShapeCounts.Clear();
            Materials.Clear(); MaterialScratch.Clear();
        }
    }
}
