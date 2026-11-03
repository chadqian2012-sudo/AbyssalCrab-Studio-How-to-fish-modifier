using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HowToFishTrainer.Core;
using HowToFishTrainer.Runtime;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace AbyssalCrab.Runtime
{
    // Presentation pose only. Camera, physics, input and player position stay native.
    public static class SpinRuntime
    {
        public const string FeatureId = "player.spin.lock";
        public const float DefaultRotationsPerSecond = 4f;
        private static float _rotationsPerSecond = DefaultRotationsPerSecond;
        private static bool _autoJump;
        private static TrainerBridgeHost _host;
        private static object _player;
        private static float _startedAt, _baseYaw, _nextProofAt;
        private static int _networkHits, _avatarHits;
        private static Transform _camera;
        private static PropertyInfo _movementRotation;
        private static float _cameraError, _movementError;
        private static FieldInfo _avatarField;
        private static Exception _fault;
        private static Harmony _patches;
        private static FieldInfo _localPlayerField;
        private static readonly FieldInfo ProofField = typeof(TrainerBridgeHost).GetField("_proofs", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Apply(TrainerBridgeHost host, JToken value)
        {
            float speed = ReadSpeed(value);
            if (_fault != null) throw new InvalidOperationException("大陀螺姿态钩子失败，已停止应用。" + _fault.GetType().Name + ": " + _fault.Message, _fault);
            EnsureHooks();
            object player = _localPlayerField.GetValue(null);
            if (!(player is Component component) || component == null)
                throw new InvalidOperationException("大陀螺等待本机玩家进入世界。");
            if (_host != host || !ReferenceEquals(_player, player))
            {
                SpinPoseRuntime.Clear();
                _host = host;
                _player = player;
                _startedAt = Time.realtimeSinceStartup;
                object camera = AccessTools.Property(player.GetType(), "CamObject").GetValue(player, null);
                _camera = camera as Transform;
                _movementRotation = AccessTools.Property(player.GetType(), "CurPlayerRot");
                _baseYaw = camera is Transform transform ? transform.eulerAngles.y : 0f;
                _nextProofAt = 0f;
                _networkHits = _avatarHits = 0;
            }
            _autoJump = ReadAutoJump((JObject)value);
            if (_rotationsPerSecond != speed)
            {
                _baseYaw = Yaw();
                _startedAt = Time.realtimeSinceStartup;
                _rotationsPerSecond = speed;
                _nextProofAt = 0f;
            }
            // The existing SetLock/ApplyLocks machinery keeps this waiting until a real hook fires.
        }

        public static void Restore(TrainerBridgeHost host, string id)
        {
            if (id == FeatureId) RestoreAll(host);
        }

        public static void RestoreAll(TrainerBridgeHost host)
        {
            if (_host != null && _host != host) return;
            SpinPoseRuntime.Clear();
            if (_host != null && _camera != null && _avatarField != null)
            {
                GameObject avatar = (GameObject)_avatarField.GetValue(_host);
                if (avatar != null) avatar.transform.rotation = Quaternion.Euler(0f, _camera.eulerAngles.y, 0f);
            }
            _host = null;
            _player = null;
            _camera = null;
            _networkHits = _avatarHits = 0;
            _fault = null;
            _rotationsPerSecond = DefaultRotationsPerSecond;
            _autoJump = false;
            // The next native packet/animation immediately uses the current real viewing angle.
        }

        private static void EnsureHooks()
        {
            if (_patches != null) return;
            Type playerType = AccessTools.TypeByName("Player");
            Type serverType = AccessTools.TypeByName("Server");
            if (playerType == null || serverType == null) throw new NotSupportedException("玩家姿态接口缺失。");
            FieldInfo local = AccessTools.Field(playerType, "LocalPlayer");
            MethodInfo packet = AccessTools.Method(serverType, "UpdatePlayerPosRot");
            MethodInfo avatar = AccessTools.Method(typeof(TrainerBridgeHost), "AnimateThirdPersonAvatar");
            MethodInfo nativeView = AccessTools.Method(typeof(TrainerBridgeHost), "UseNativeThirdPersonView");
            MethodInfo restoreView = AccessTools.Method(typeof(TrainerBridgeHost), "RestoreThirdPerson");
            FieldInfo avatarField = typeof(TrainerBridgeHost).GetField("_thirdAvatar", BindingFlags.Instance | BindingFlags.NonPublic);
            if (local == null || packet == null || avatar == null || avatarField == null || nativeView == null || restoreView == null)
                throw new NotSupportedException("玩家姿态接口不匹配。");
            ParameterInfo[] parameters = packet.GetParameters();
            if (parameters.Length != 6 || parameters[0].ParameterType != playerType ||
                parameters[1].ParameterType != typeof(Vector3) || parameters[2].ParameterType != typeof(Vector2) ||
                parameters[3].ParameterType != typeof(bool) || parameters[4].ParameterType != typeof(bool))
                throw new NotSupportedException("玩家网络姿态参数不匹配。");
            Harmony patches = new Harmony(typeof(SpinRuntime).Assembly.GetName().Name + ".SpinVisual");
            try
            {
                patches.Patch(packet, prefix: new HarmonyMethod(typeof(SpinRuntime).GetMethod("NetworkYawPrefix")));
                patches.Patch(avatar, prefix: new HarmonyMethod(typeof(SpinRuntime).GetMethod("AvatarYawPrefix")),
                    postfix: new HarmonyMethod(typeof(SpinRuntime).GetMethod("AvatarYawPostfix")));
                patches.Patch(nativeView, prefix: new HarmonyMethod(typeof(SpinRuntime).GetMethod("PresentationSuspended")));
                patches.Patch(restoreView, prefix: new HarmonyMethod(typeof(SpinRuntime).GetMethod("PresentationSuspended")));
                _localPlayerField = local;
                _avatarField = avatarField;
                _patches = patches;
            }
            catch
            {
                patches.Unpatch(packet, HarmonyPatchType.All, patches.Id);
                patches.Unpatch(avatar, HarmonyPatchType.All, patches.Id);
                patches.Unpatch(nativeView, HarmonyPatchType.All, patches.Id);
                patches.Unpatch(restoreView, HarmonyPatchType.All, patches.Id);
                throw;
            }
        }

        private static bool Matches(object player)
        {
            return _host != null && _host.IsLockEnabled(FeatureId) && ReferenceEquals(player, _player) &&
                _localPlayerField != null && ReferenceEquals(player, _localPlayerField.GetValue(null)) &&
                player is Component component && component != null;
        }

        public static float CalculateYaw(float baseYaw, float elapsed, float rotationsPerSecond)
        {
            double turns = (Math.Max(0d, elapsed) * rotationsPerSecond) % 1d;
            double yaw = (baseYaw + turns * 360d) % 360d;
            return (float)(yaw < 0d ? yaw + 360d : yaw);
        }

        private static float Yaw() { return CalculateYaw(_baseYaw, Time.realtimeSinceStartup - _startedAt, _rotationsPerSecond); }

        public static float ReadSpeed(JToken value)
        {
            if (!(value is JObject configuration)) throw new ArgumentException("大陀螺转速配置缺失。");
            return ReadPublicSpeed(configuration);
        }

        public static bool ReadAutoJump(JObject arguments)
        {
            JToken option = arguments["autoJump"];
            if (option == null) return false;
            if (option.Type != JTokenType.Boolean) throw new ArgumentException("autoJump must be boolean");
            return option.Value<bool>();
        }

        private static float ReadPublicSpeed(JObject arguments)
        {
            JToken speed = arguments["rotationsPerSecond"];
            if (speed == null) return DefaultRotationsPerSecond; // Older callers retain 4 revolutions/sec.
            if (speed.Type != JTokenType.Integer && speed.Type != JTokenType.Float)
                throw new ArgumentException("rotationsPerSecond must be a number");
            double number = speed.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || number < 2d || number > 15d)
                throw new ArgumentOutOfRangeException("rotationsPerSecond", "转速范围为每秒 2–15 圈。");
            return (float)number;
        }

        public static void ValidateArguments(string id, JObject arguments)
        {
            if (id != FeatureId) return;
            if (arguments["enabled"] == null || arguments["enabled"].Type != JTokenType.Boolean)
                throw new ArgumentException("enabled must be boolean");
            if (!arguments.Value<bool>("enabled")) return; // Off must always remain available.
            if (arguments["value"] != null) throw new ArgumentException("大陀螺使用 rotationsPerSecond 参数。");
            ReadPublicSpeed(arguments);
            ReadAutoJump(arguments);
        }

        public static void NormalizeArguments(string id, JObject arguments)
        {
            if (id != FeatureId || !arguments.Value<bool>("enabled")) return;
            var configuration = new JObject();
            configuration["rotationsPerSecond"] = ReadPublicSpeed(arguments);
            configuration["autoJump"] = ReadAutoJump(arguments);
            arguments["value"] = configuration; // Existing SetLock stores and replays this per-feature configuration.
        }

        public static void NetworkYawPrefix(object __0, ref Vector2 __2)
        {
            if (!Matches(__0)) return;
            Vector2 original = __2;
            try
            {
                Quaternion cameraBefore = _camera.localRotation;
                Quaternion movementBefore = (Quaternion)_movementRotation.GetValue(_player, null);
                __2.x = SpinPosePolicy.LookDownPitch;
                __2.y = Yaw();
                _cameraError = Quaternion.Angle(cameraBefore, _camera.localRotation);
                _movementError = Quaternion.Angle(movementBefore, (Quaternion)_movementRotation.GetValue(_player, null));
                _networkHits++;
                RecordProof("network", original.y, __2.y);
            }
            catch (Exception exception)
            {
                __2 = original;
                Fail(exception);
            }
        }

        public static void AvatarYawPrefix(object __instance, object __0, ref float __1)
        {
            if (!ReferenceEquals(__instance, _host) || !Matches(__0)) return;
            float original = __1;
            try
            {
                SpinPoseRuntime.RestoreFrame();
                // Solve the native limb animation in the ordinary pose first; rotating its
                // cosmetic root afterward makes hands/feet rotate with the whole avatar.
                GameObject avatar = (GameObject)_avatarField.GetValue(_host);
                if (avatar != null) avatar.transform.rotation = Quaternion.Euler(0f, __1, 0f);
            }
            catch (Exception exception)
            {
                __1 = original;
                Fail(exception);
            }
        }

        public static void PresentationSuspended(object __instance)
        {
            if (ReferenceEquals(__instance, _host)) SpinPoseRuntime.Clear();
        }

        public static void AvatarYawPostfix(object __instance, object __0)
        {
            if (!ReferenceEquals(__instance, _host) || !Matches(__0)) return;
            try
            {
                GameObject avatar = (GameObject)_avatarField.GetValue(_host);
                if (avatar == null) return;
                SpinPoseRuntime.Apply(avatar, __0, _autoJump);
                float yaw = Yaw();
                avatar.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _avatarHits++;
                RecordProof("thirdPerson", _camera.eulerAngles.y, yaw);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void RecordProof(string path, float originalYaw, float visualYaw)
        {
            if (Time.realtimeSinceStartup < _nextProofAt) return;
            _nextProofAt = Time.realtimeSinceStartup + 0.25f;
            RuntimeFeatureProofRegistry proofs = (RuntimeFeatureProofRegistry)ProofField.GetValue(_host);
            var data = new Dictionary<string, object>();
            data.Add("path", path);
            data.Add("rotationsPerSecond", _rotationsPerSecond);
            data.Add("degreesPerSecond", _rotationsPerSecond * 360f);
            data.Add("nativeYaw", originalYaw);
            data.Add("visualYaw", visualYaw);
            data.Add("visualPitch", SpinPosePolicy.LookDownPitch);
            data.Add("autoJump", _autoJump);
            data.Add("modelAirborne", SpinPoseRuntime.SyntheticAirborne);
            data.Add("modelJumpHeight", SpinPoseRuntime.AppliedHeight);
            foreach (var entry in SpinPoseRuntime.DescribeRig()) data.Add("rig_" + entry.Key, entry.Value);
            data.Add("nativeJumpParametersPreserved", true);
            data.Add("networkPositionWritten", false);
            data.Add("networkHookHits", _networkHits);
            data.Add("avatarHookHits", _avatarHits);
            data.Add("cameraWritten", false);
            data.Add("movementWritten", false);
            data.Add("cameraRotationErrorDegrees", _cameraError);
            data.Add("movementRotationErrorDegrees", _movementError);
            proofs.Hit(FeatureId, data);
        }

        private static void Fail(Exception exception)
        {
            TrainerBridgeHost host = _host;
            RestoreAll(host);
            _fault = exception;
            if (host != null) ((RuntimeFeatureProofRegistry)ProofField.GetValue(host)).Fail(FeatureId, exception);
        }
    }
}
