using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public static class SpinPoseRuntime
    {
        private static Transform _root, _head, _torso;
        private static Transform[] _bones, _poseBones;
        private static byte[] _poseChannels; // position=1, rotation=2, scale=4
        private static Quaternion[] _savedRotations;
        private static Vector3[] _savedPositions, _savedScales;
        private static readonly Transform[] Upper = new Transform[2], Lower = new Transform[2], Feet = new Transform[2];
        private static readonly Transform[] ArmUpper = new Transform[2], ArmLower = new Transform[2], Hands = new Transform[2];
        private static readonly Vector3[] WristOffsets = new Vector3[2];
        private static readonly Vector3[] JumpFeet = new Vector3[2], CurrentFeet = new Vector3[2];
        private static readonly SpinPosePolicy Hop = new SpinPosePolicy();
        private static Player _player;
        private static float _jumpSpeed, _landingSpeed, _footSpacing, _height, _appliedHeight;
        private static bool _saved, _wasAirborne, _landing;
        private static float _rightWristError, _leftWristError;

        public static bool SyntheticAirborne { get { return Hop.SyntheticAirborne; } }
        public static float AppliedHeight { get { return _appliedHeight; } }
        public static int SnapshotBoneCount { get { return _poseBones == null ? 0 : _poseBones.Length; } }
        public static int SnapshotChannelCount
        {
            get
            {
                int count = 0;
                if (_poseChannels != null) foreach (byte channels in _poseChannels)
                    for (int bit = 1; bit <= 4; bit <<= 1) if ((channels & bit) != 0) count++;
                return count;
            }
        }

        private static object Field(object source, string name)
        {
            return AccessTools.Field(source.GetType(), name).GetValue(source);
        }

        private static Transform CopyOf(Transform source, Transform original)
        {
            if (source == null) return null;
            string path = source.name;
            Transform cursor = source;
            while (cursor != original && cursor.parent != null)
            {
                cursor = cursor.parent;
                path = cursor.name + "/" + path;
            }
            return cursor == original ? _root.Find(path) : null;
        }

        private static Transform Bone(string name)
        {
            Transform result = null;
            foreach (Transform bone in _bones)
            {
                if (bone.name != name) continue;
                if (result != null) throw new NotSupportedException("角色动作骨骼重复：" + name);
                result = bone;
            }
            if (result == null) throw new NotSupportedException("角色动作骨骼缺失：" + name);
            return result;
        }

        private static void Bind(GameObject avatar, Player player)
        {
            Clear();
            _root = avatar.transform;
            _player = player;
            _bones = _root.GetComponentsInChildren<Transform>(true);
            bool found = false;
            foreach (UnityEngine.Object candidate in Resources.FindObjectsOfTypeAll(typeof(Player)))
            {
                Player prefab = candidate as Player;
                if (prefab == null || prefab.gameObject.scene.IsValid() || prefab.Body == null || prefab.Legs == null) continue;
                GameObject model = Field(prefab.Body, "_newCharacter") as GameObject;
                if (model == null) continue;
                Transform head = CopyOf(prefab.Body.Head, model.transform);
                Transform torso = CopyOf(prefab.Body.LowerBody, model.transform);
                Vector3[] jump = Field(prefab.Legs, "_jumpFeetPos") as Vector3[];
                if (head == null || torso == null || jump == null || jump.Length != 2) continue;
                if (prefab.Arms == null) continue;
                IK rightIK = Field(prefab.Arms, "_ikRight") as IK, leftIK = Field(prefab.Arms, "_ikLeft") as IK;
                if (rightIK == null || leftIK == null) continue;
                WristOffsets[0] = (Vector3)Field(rightIK, "_targetOffset");
                WristOffsets[1] = (Vector3)Field(leftIK, "_targetOffset");
                _head = head;
                _torso = torso;
                JumpFeet[0] = jump[0]; JumpFeet[1] = jump[1];
                _jumpSpeed = Convert.ToSingle(Field(prefab.Legs, "_jumpLerpSpeed"));
                _landingSpeed = Convert.ToSingle(Field(prefab.Legs, "_landingStepSpeed"));
                _footSpacing = Convert.ToSingle(Field(prefab.Legs, "_spaceBetweenFeet"));
                found = true;
                break;
            }
            if (!found) throw new NotSupportedException("未找到匹配的原生头部、身体及跳跃姿态参数。");
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "_R" : "_L";
                Upper[i] = Bone("LegUpper" + side);
                Lower[i] = Bone("LegLower" + side);
                Feet[i] = Bone("Foot" + side);
                ArmUpper[i] = Bone("ArmUpper" + side);
                ArmLower[i] = Bone("ArmLower" + side);
                Hands[i] = Bone("Hand" + side);
            }
            var affected = new List<Transform>();
            var channels = new List<byte>();
            Transform[] candidates = { _torso, _head, Upper[0], Lower[0], Feet[0], Upper[1], Lower[1], Feet[1],
                ArmUpper[0], ArmLower[0], Hands[0], ArmUpper[1], ArmLower[1], Hands[1] };
            // Inline List.Add calls avoid compiler-generated data-blob dependencies in the incremental module copier.
            var masks = new List<byte> { 2, 2, 3, 2, 2, 3, 2, 2, 2, 6, 6, 2, 6, 6 };
            for (int i = 0; i < candidates.Length; i++)
            {
                int index = affected.IndexOf(candidates[i]);
                if (index < 0) { affected.Add(candidates[i]); channels.Add(masks[i]); }
                else channels[index] |= masks[i];
            }
            _poseBones = affected.ToArray();
            _poseChannels = channels.ToArray();
            _savedRotations = new Quaternion[_poseBones.Length];
            _savedPositions = new Vector3[_poseBones.Length];
            _savedScales = new Vector3[_poseBones.Length];
            _height = Mathf.Clamp((Vector3.Distance(Upper[0].position, Lower[0].position) +
                Vector3.Distance(Lower[0].position, Feet[0].position)) * 0.65f, 0.15f, 0.65f);
        }

        public static void RestoreFrame()
        {
            if (!_saved) return;
            if (_root != null)
            {
                _root.position -= Vector3.up * _appliedHeight;
                for (int i = 0; i < _poseBones.Length; i++)
                {
                    if (_poseBones[i] == null || _poseBones[i] == _root) continue;
                    if ((_poseChannels[i] & 1) != 0) _poseBones[i].localPosition = _savedPositions[i];
                    if ((_poseChannels[i] & 2) != 0) _poseBones[i].localRotation = _savedRotations[i];
                    if ((_poseChannels[i] & 4) != 0) _poseBones[i].localScale = _savedScales[i];
                }
            }
            _appliedHeight = 0f;
            _saved = false;
        }

        public static void Clear()
        {
            RestoreFrame();
            SpinHeldVisualRuntime.Clear();
            _root = _head = _torso = null;
            _player = null;
            _bones = _poseBones = null; _savedRotations = null; _savedPositions = _savedScales = null;
            _poseChannels = null;
            for (int i = 0; i < 2; i++)
            {
                Upper[i] = Lower[i] = Feet[i] = null;
                ArmUpper[i] = ArmLower[i] = Hands[i] = null;
            }
            _rightWristError = _leftWristError = 0f;
            _wasAirborne = _landing = false;
            Hop.Reset();
        }

        public static void Apply(GameObject avatar, object player, bool autoJump)
        {
            Player local = player as Player;
            if (local == null || local != Player.LocalPlayer || avatar == null) return;
            if (_root != avatar.transform || _player != local) Bind(avatar, local);
            SpinHeldVisualRuntime.Capture(avatar, local);
            bool grounded = local.Movement.Grounded;
            bool blocked = local.BlockInputs || local.Dying.IsDead || local.Movement.OnBoat;
            Hop.Advance(Time.unscaledTime, autoJump, grounded, blocked);
            bool airborne = !grounded || Hop.SyntheticAirborne;
            for (int i = 0; i < _poseBones.Length; i++)
            {
                if ((_poseChannels[i] & 1) != 0) _savedPositions[i] = _poseBones[i].localPosition;
                if ((_poseChannels[i] & 2) != 0) _savedRotations[i] = _poseBones[i].localRotation;
                if ((_poseChannels[i] & 4) != 0) _savedScales[i] = _poseBones[i].localScale;
            }
            _saved = true;
            Vector3 rightHand = WristTarget(0), leftHand = WristTarget(1);
            Quaternion rightHandRotation = SpinHeldVisualRuntime.MapRotation(Hands[0].rotation),
                leftHandRotation = SpinHeldVisualRuntime.MapRotation(Hands[1].rotation);
            Vector3 rightFoot = Feet[0].position, leftFoot = Feet[1].position;
            Quaternion rightRotation = Feet[0].rotation, leftRotation = Feet[1].rotation;
            Vector3 rightHip = Upper[0].position, leftHip = Upper[1].position;
            Quaternion rightHipRotation = Upper[0].rotation, leftHipRotation = Upper[1].rotation;
            if (airborne && !_wasAirborne)
            {
                CurrentFeet[0] = _root.InverseTransformPoint(rightFoot);
                CurrentFeet[1] = _root.InverseTransformPoint(leftFoot);
            }
            if (!airborne && _wasAirborne) _landing = true;
            if (airborne) _landing = false;
            _torso.rotation = Quaternion.AngleAxis(SpinPosePolicy.TorsoLean, _root.right) * _torso.rotation;
            Upper[0].position = rightHip; Upper[1].position = leftHip;
            Upper[0].rotation = rightHipRotation; Upper[1].rotation = leftHipRotation;
            _head.rotation = Quaternion.Euler(SpinPosePolicy.LookDownPitch, _root.eulerAngles.y, 0f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 target = i == 0 ? rightHand : leftHand;
                SolveArm(ArmUpper[i], ArmLower[i], Hands[i], target,
                    -_root.forward * 0.4f + _root.right * (i == 0 ? 1f : -1f));
                Hands[i].rotation = i == 0 ? rightHandRotation : leftHandRotation;
                float error = Vector3.Distance(Hands[i].position, target);
                if (i == 0) _rightWristError = error; else _leftWristError = error;
            }
            bool landed = true;
            for (int i = 0; i < 2; i++)
            {
                Vector3 nativeFoot = i == 0 ? rightFoot : leftFoot;
                if (airborne || _landing)
                {
                    Vector3 target = airborne ? JumpFeet[i] + Vector3.right * (i == 0 ? _footSpacing : -_footSpacing) :
                        _root.InverseTransformPoint(nativeFoot);
                    CurrentFeet[i] = Vector3.Lerp(CurrentFeet[i], target,
                        SpinPosePolicy.NativeBlend(airborne ? _jumpSpeed : _landingSpeed, Time.deltaTime));
                    if ((CurrentFeet[i] - target).sqrMagnitude > 0.00001f) landed = false;
                    nativeFoot = _root.TransformPoint(CurrentFeet[i]);
                }
                SolveLeg(Upper[i], Lower[i], Feet[i], nativeFoot, _root.forward);
                Feet[i].rotation = i == 0 ? rightRotation : leftRotation;
            }
            if (_landing && landed) _landing = false;
            _wasAirborne = airborne;
            _appliedHeight = Hop.HeightFraction * _height;
            _root.position += Vector3.up * _appliedHeight;
        }

        public static Dictionary<string, object> DescribeRig()
        {
            var data = new Dictionary<string, object>();
            data["bound"] = _root != null;
            if (_root == null || _head == null || _torso == null) return data;
            data["headForwardY"] = _head.forward.y;
            data["rootHeight"] = _appliedHeight;
            data["grounded"] = _player.Movement.Grounded;
            data["rightWristError"] = _rightWristError;
            data["leftWristError"] = _leftWristError;
            data["heldVisualRenderers"] = SpinHeldVisualRuntime.RendererCount;
            data["poseSnapshotBones"] = SnapshotBoneCount;
            data["poseSnapshotChannels"] = SnapshotChannelCount;
            Vector3 heldDirection = SpinHeldVisualRuntime.DisplayDirection;
            data["heldDirectionSource"] = SpinHeldVisualRuntime.DirectionSource;
            data["heldDirectionX"] = heldDirection.x;
            data["heldDirectionY"] = heldDirection.y;
            data["heldDirectionZ"] = heldDirection.z;
            data["nativeHeldPositionError"] = SpinHeldVisualRuntime.NativePositionError;
            data["nativeHeldRotationError"] = SpinHeldVisualRuntime.NativeRotationError;
            return data;
        }

        private static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
            float distance = Vector3.Distance(upper.position, target);
            // As with the game's arm IK, extend the copied forearm if the leaning shoulder makes the grip unreachable.
            // The palm stays at the native solved grip; never translate a wrist away from its forearm.
            if (b > 0.0001f && (distance >= a + b - 0.0001f || distance < Mathf.Abs(a - b) + 0.0001f))
            {
                float stretch = distance < Mathf.Abs(a - b) + 0.0001f ? a / b :
                    Mathf.Max(1f, (distance - a + 0.0002f) / b);
                Vector3 scale = lower.localScale, endScale = hand.localScale;
                lower.localScale = new Vector3(scale.x, scale.y * stretch, scale.z);
                hand.localScale = new Vector3(endScale.x, endScale.y / stretch, endScale.z);
            }
            SolveLeg(upper, lower, hand, target, pole);
        }

        private static Vector3 WristTarget(int side)
        {
            // Local players can have their body IK removed. PlayerHands still follows animated tool targets.
            Transform target = side == 0 ? _player.Hands.HandBoneRight : _player.Hands.HandBoneLeft;
            if (target == null) return Hands[side].position;
            // Match native IK.ResolveIK: the serialized offset is rotated but is not scaled with the held item.
            return SpinHeldVisualRuntime.MapPosition(target.position + target.rotation * WristOffsets[side]);
        }

        private static void SolveLeg(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 start = upper.position;
            float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, end.position);
            if (a < 0.0001f || b < 0.0001f) return;
            Vector3 direction = target - start;
            if (direction.sqrMagnitude < 0.000001f) direction = Vector3.down;
            float reach = Mathf.Clamp(direction.magnitude, Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);
            direction.Normalize();
            float along = (a * a - b * b + reach * reach) / (2f * reach);
            float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 0.00001f) bend = Vector3.Cross(direction, Vector3.right);
            if (bend.sqrMagnitude < 0.00001f) bend = Vector3.Cross(direction, Vector3.up);
            Vector3 knee = start + direction * along + bend.normalized * height;
            upper.rotation = Quaternion.FromToRotation(lower.position - start, knee - start) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position,
                start + direction * reach - lower.position) * lower.rotation;
        }
    }
}
