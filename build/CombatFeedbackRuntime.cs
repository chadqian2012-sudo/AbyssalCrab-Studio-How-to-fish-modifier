using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AbyssalCrab.Runtime
{
    // Only observes native, host-confirmed creature damage. Never modifies HP, input,
    // weapon parameters, damage arguments, return values or the original method flow.
    public static class CombatFeedbackRuntime
    {
        private static TrainerBridgeHost _owner;
        private static Harmony _harmony;
        private static GameObject _display;
        private static Player _player;
        private static CombatFeedbackState _state = new CombatFeedbackState();
        private static bool _enabled, _streaks, _markers, _headSound, _killSound, _numbers, _dynamic;
        private static float _window = 8, _volume = .35f, _opacity = .9f, _scale = 1;
        private static float _lastMarker = -100, _lastAudio = -100;
        private static bool _markerHead, _markerKill;
        private static CombatHitContext _pending = new CombatHitContext();
        private static string _comboLabel = "";
        private static int _scene;
        private static AccessTools.FieldRef<Weapon,float> _spread;
        private static AccessTools.FieldRef<TrainerBridgeHost,float> _lastCommand;
        private static readonly Vector3[] Positions = new Vector3[24];
        private static readonly int[] TargetIds = new int[24], Damages = new int[24];
        private static readonly float[] Created = new float[24], LastMerged = new float[24];
        private static readonly bool[] Heads = new bool[24], Deaths = new bool[24];
        private static readonly string[] Labels = new string[24];
        private static int _cursor;
        private static AudioClip _headClip, _killClip, _streakClip;
        private static AudioSource _audio;
        private static GUIStyle _label, _small;
        private static Font _font;
        private static int _errors;
        private static int _mainThread;
        private static volatile bool _cleanupRequested;

        private static GullChallengeRound Round;
        public static GullChallengeRound RoundFor(TrainerBridgeHost host) => host == _owner ? Round : null;
        private static Harmony _gunHooks;
        private static AccessTools.FieldRef<ProjectileManager,uint> _nextProjectile;

        private static void Observe(TrainerBridgeHost host)
        {
            var local = Player.LocalPlayer;
            if (!local || !local.IsServerInitialized)
                throw new InvalidOperationException("需要单人或房主世界的实际伤害结算。");
            if (_owner != host || _player != local || _cleanupRequested) Reset(null);
            if (_harmony != null) return;
            _owner = host; _player = local; _scene = SceneManager.GetActiveScene().handle.GetHashCode();
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                _lastCommand = AccessTools.FieldRefAccess<TrainerBridgeHost,float>("_lastCommandAt");
                var hit = AccessTools.Method(typeof(Creature), "LocalHit");
                var server = AccessTools.Method(typeof(Server), "RpcLogic___HitCreature___215526726");
                if (hit == null || server == null) throw new MissingMethodException("当前游戏伤害接口不匹配");
                _harmony = new Harmony("AbyssalCrab.CombatFeedback." + Guid.NewGuid().ToString("N"));
                _harmony.Patch(hit, new HarmonyMethod(typeof(CombatFeedbackRuntime), "LocalPrefix"));
                _harmony.Patch(server, new HarmonyMethod(typeof(CombatFeedbackRuntime), "DamagePrefix"), new HarmonyMethod(typeof(CombatFeedbackRuntime), "DamagePostfix"));
            }
            catch { Reset(null); throw; }
        }

        public static GullChallengeRound PrepareRound(TrainerBridgeHost host, string id)
        {
            var player = Player.LocalPlayer;
            var weapon = player && player.Holding && player.Holding.HeldItem ? player.Holding.HeldItem.Weapon : null;
            if (!weapon || weapon.UsesArrows || !player.Dying || player.Dying.IsDead || IslandManager.IsLoading)
                throw new InvalidOperationException("请进入测试世界并手持枪械。");
            Observe(host);
            if (Round != null && Round.Reason == null) throw new InvalidOperationException("请先结束当前飞靶挑战。");
            if (_gunHooks == null)
            {
                try
                {
                    _nextProjectile = AccessTools.FieldRefAccess<ProjectileManager,uint>("_nextId");
                    _gunHooks = new Harmony("AbyssalCrab.GullChallenge." + Guid.NewGuid().ToString("N"));
                    _gunHooks.Patch(AccessTools.Method(typeof(Weapon),"Shoot"), new HarmonyMethod(typeof(CombatFeedbackRuntime), "ShotPrefix"), null, null, new HarmonyMethod(typeof(CombatFeedbackRuntime), "ShotFinalizer"));
                    _gunHooks.Patch(AccessTools.Method(typeof(ProjectileManager),"Hit"), new HarmonyMethod(typeof(CombatFeedbackRuntime), "ImpactPrefix"), null, null, new HarmonyMethod(typeof(CombatFeedbackRuntime), "ImpactFinalizer"));
                }
                catch { ReleaseIdleHooks(); throw; }
            }
            Round = new GullChallengeRound(id,weapon);
            return Round;
        }

        public static void ShotPrefix(Weapon __instance, out uint? __state)
        {
            __state = null;
            if (Round == null || !Round.Active || __instance != Round.Gun || __instance.Holder != _player || !ProjectileManager.Instance) return;
            Round.LaunchManager = ProjectileManager.Instance;
            __state = _nextProjectile(Round.LaunchManager); Round.Shooting = true;
        }

        public static void ShotFinalizer(uint? __state,int ____projectileCountPerShot)
        {
            if (!__state.HasValue || Round == null) return;
            Round.Shooting = false;
            var manager = Round.LaunchManager; Round.LaunchManager = null;
            if (!Round.Active || !manager || manager != ProjectileManager.Instance) return;
            Round.RememberVolley(__state.Value,_nextProjectile(manager),____projectileCountPerShot);
        }

        public static void ImpactPrefix(Projectile __0, out bool __state)
        {
            __state = Round != null && Round.Active && __0 != null && __0.IsLocal && __0.Owner == _player &&
                !__0.FromNpc && !__0.IsArrow && Round.Projectiles.Remove(__0.Id);
            if (__state) Round.Impact = true;
        }
        public static void ImpactFinalizer(bool __state) { if (__state && Round != null) Round.Impact = false; }

        public static void Configure(TrainerBridgeHost host, JObject args)
        {
            bool enabled = Boolean(args, "enabled", false);
            bool streaks = Boolean(args, "streaks", true), markers = Boolean(args, "markers", true);
            bool headSound = Boolean(args, "headSound", true), killSound = Boolean(args, "killSound", true);
            bool numbers = Boolean(args, "numbers", true), dynamic = Boolean(args, "dynamicCrosshair", false);
            float window = Number(args, "comboWindow", 8, 1, 30);
            float volume = Number(args, "volume", .35f, 0, 1);
            float opacity = Number(args, "opacity", .9f, .1f, 1);
            float scale = Number(args, "scale", 1, .5f, 2);
            if (!enabled)
            {
                if (_owner == host) { ClearDisplay(); ReleaseIdleHooks(); }
                return;
            }
            Observe(host);
            if (!_enabled)
            {
                try
                {
                    _spread = AccessTools.FieldRefAccess<Weapon,float>("_spread");
                    _display = new GameObject("AbyssalCrab Combat Feedback");
                    _display.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(_display);
                    _display.AddComponent<CombatFeedbackRenderer>();
                    CombatFeedbackRenderer.Render = new Action(Draw);
                    _audio = _display.AddComponent<AudioSource>(); _audio.spatialBlend = 0; _audio.playOnAwake = false;
                    _headClip = Tone("AbyssalCrab Head", 1100, .055f);
                    _killClip = Tone("AbyssalCrab Kill", 650, .11f);
                    _streakClip = Tone("AbyssalCrab Streak", 1500, .16f);
                }
                catch { ClearDisplay(); ReleaseIdleHooks(); throw; }
            }
            _streaks = streaks; _markers = markers; _headSound = headSound; _killSound = killSound;
            _numbers = numbers; _dynamic = dynamic; _window = window; _volume = volume; _opacity = opacity; _scale = scale;
            _enabled = true;
        }

        private static bool Boolean(JObject args, string key, bool fallback)
        {
            var token = args[key]; if (token == null) return fallback;
            if (token.Type != JTokenType.Boolean) throw new ArgumentException(key + " 必须为布尔值");
            return token.Value<bool>();
        }
        private static float Number(JObject args, string key, float fallback, float min, float max)
        {
            var token = args[key]; if (token == null) return fallback;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new ArgumentException(key + " 必须为数字");
            float value = token.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new ArgumentOutOfRangeException(key);
            return value;
        }
        private static AudioClip Tone(string name, float frequency, float seconds)
        {
            int n = (int)(22050 * seconds); var samples = new float[n];
            for (int i = 0; i < n; i++) samples[i] = (float)Math.Sin(2 * Math.PI * frequency * i / 22050) * .18f * (1f - (float)i / n);
            var clip = AudioClip.Create(name, n, 1, 22050, false); clip.SetData(samples, 0); return clip;
        }

        public static void LocalPrefix(Creature __instance, Vector3 point, Player player, bool rangedHit, bool fromNpc)
        {
            if (_cleanupRequested || fromNpc || player != _player || player != Player.LocalPlayer || !__instance || __instance.IsDead) return;
            if (Round != null && Round.Active && Round.Birds.ContainsKey(__instance.GetInstanceID()))
                Round.PendingHits.Track(__instance.GetInstanceID(),point.x,point.y,point.z,Time.unscaledTime,
                    rangedHit && (Round.Shooting || Round.Impact));
            if (!_enabled) return;
            try { _pending.Track(__instance.GetInstanceID(),point.x,point.y,point.z,Time.unscaledTime,
                rangedHit && __instance.transform.InverseTransformPoint(point).z > __instance.HeadPos); } catch { _errors++; }
        }
        public static void DamagePrefix(Creature __0, Player __1, out int __state)
        {
            __state = -1;
            if (!_cleanupRequested && (_enabled || Round != null && Round.Active) && __0 && !__0.IsDeinitializing && __0.IsServerInitialized && __1 == _player && _player == Player.LocalPlayer)
                __state = __0.Hp;
        }
        public static void DamagePostfix(Creature __0, Vector3 __3, int __state)
        {
            if (_cleanupRequested || __state <= 0 || !__0) return;
            try
            {
                int damage = Math.Max(0, __state - Math.Max(0, __0.Hp));
                float now = Time.unscaledTime;
                if (Round != null && Round.Active && Round.Birds.ContainsKey(__0.GetInstanceID()) &&
                    Round.PendingHits.Consume(__0.GetInstanceID(),__3.x,__3.y,__3.z,now) && damage > 0)
                    Round.Record(__0.GetInstanceID(),Time.realtimeSinceStartup,__0.Hp <= 0);
                if (!_enabled || damage == 0) return;
                bool killed = __0.Hp <= 0, head = _pending.Consume(__0.GetInstanceID(),__3.x,__3.y,__3.z,now);
                _state.Register(now, damage, head, killed, _window);
                if(killed)_comboLabel=_state.Stage+"  ·  "+_state.Combo+" 连杀";
                _lastMarker = now; _markerHead = head; _markerKill = killed;
                int id = __0.GetInstanceID(), slot = -1;
                for (int i = 0; i < 24; i++)
                    if (Damages[i] > 0 && TargetIds[i] == id && now - LastMerged[i] <= .12f && now - Created[i] < .75f) { slot = i; break; }
                if (slot < 0) { slot = _cursor++ % 24; Damages[slot] = 0; Heads[slot] = Deaths[slot] = false; Created[slot] = now; }
                TargetIds[slot] = id; Positions[slot] = __3; LastMerged[slot] = now;
                Damages[slot] = (int)Math.Min(int.MaxValue,(long)Damages[slot]+damage); Heads[slot] |= head; Deaths[slot] |= killed;
                Labels[slot] = (Heads[slot] ? "爆头 " : "") + Damages[slot] + (Deaths[slot] ? "  击败" : "");
                bool milestone=killed&&_streaks&&(_state.Combo==3||_state.Combo==5||_state.Combo==10);
                if (_audio && now - _lastAudio >= .05f && (milestone || killed && _killSound || head && _headSound))
                { _audio.PlayOneShot(milestone ? _streakClip : killed && _killSound ? _killClip : _headClip, _volume); _lastAudio = now; }
            }
            catch { _errors++; }
        }

        public static void Tick(TrainerBridgeHost host)
        {
            if(_cleanupRequested&&host==_owner){Reset(host);return;}
            if (host != _owner || _owner == null || _harmony == null) return;
            try
            {
                if (!SameWorld(host))
                { Reset(host); return; }
                if (Round != null && Round.Active) Round.Tick(_player,Time.realtimeSinceStartup);
                ReleaseIdleHooks();
                if (_enabled) _state.Tick(Time.unscaledTime, _window);
            }
            catch { Reset(host); }
        }
        private static bool SameWorld(TrainerBridgeHost host)
        {
            return _player && _player == Player.LocalPlayer && _player.IsServerInitialized &&
                SceneManager.GetActiveScene().handle.GetHashCode() == _scene && Time.realtimeSinceStartup - _lastCommand(host) <= 5;
        }
        public static JToken Describe(JToken session)
        {
            var root = session as JObject; if (root == null) return session;
            if (_owner != null && _harmony == null && !SameWorld(_owner)) Reset(_owner);
            root["gullChallenge"] = Round == null || Round.Pending ? null : Round.Snapshot();
            root["combatFeedback"] = new JObject { ["available"] = true, ["enabled"] = _enabled,
                ["hits"] = _state.Hits, ["headshots"] = _state.Headshots, ["kills"] = _state.Kills,
                ["damage"] = _state.Damage, ["combo"] = _state.Combo, ["bestCombo"] = _state.BestCombo,
                ["stage"] = _state.Stage, ["errors"] = _errors };
            return session;
        }
        public static void RequestReset(TrainerBridgeHost host)
        {
            if(_owner==null||host!=_owner)return;
            if(Thread.CurrentThread.ManagedThreadId==_mainThread){Reset(host);return;}
            _enabled=false;_cleanupRequested=true;
        }
        public static void Reset(TrainerBridgeHost host)
        {
            if (host != null && _owner != null && host != _owner) return;
            Round = null; _cleanupRequested = false; _player = null; _owner = null;
            ClearDisplay();
            ReleaseIdleHooks();
        }
        private static void ReleaseIdleHooks()
        {
            if (Round != null && Round.Reason == null) return;
            if (_gunHooks != null) { _gunHooks.UnpatchAll(_gunHooks.Id); _gunHooks = null; }
            if (!_enabled && _harmony != null) { _harmony.UnpatchAll(_harmony.Id); _harmony = null; }
        }
        private static void ClearDisplay()
        {
            _enabled = false;
            CombatFeedbackRenderer.Render = null;
            if (_display) UnityEngine.Object.Destroy(_display);
            if (_headClip) UnityEngine.Object.Destroy(_headClip);
            if (_killClip) UnityEngine.Object.Destroy(_killClip);
            if (_streakClip) UnityEngine.Object.Destroy(_streakClip);
            if (_font) UnityEngine.Object.Destroy(_font);
            _font = null; _label = _small = null;
            _display = null; _audio = null; _headClip = _killClip = _streakClip = null;
            Array.Clear(Damages, 0, 24); _lastMarker = _lastAudio = -100; _cursor = 0;
            _state = new CombatFeedbackState(); _pending = new CombatHitContext(); _errors = 0;
        }
        private static void Line(float x, float y, float w, float h, Color color)
        { GUI.color = color; GUI.DrawTexture(new Rect(x,y,w,h), Texture2D.whiteTexture); }
        public static void Draw()
        {
            if(_cleanupRequested){Reset(_owner);return;}
            if (!_enabled) return;
            Color old = GUI.color;
            try
            {
                if (!_player || _player.BlockInputs || !_player.Camera || !_player.Camera.MouseLocked ||
                    _player.Dying.IsDead || Event.current.type != EventType.Repaint) return;
                if (_label == null)
                {
                    _font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft YaHei UI","Arial"},24);
                    _label = new GUIStyle(GUI.skin.label); _label.alignment = TextAnchor.MiddleCenter; _label.fontStyle = FontStyle.Bold;
                    if(_font)_label.font=_font;
                    _small = new GUIStyle(_label);
                }
                float now = Time.unscaledTime, s = Mathf.Clamp(Screen.height / 1080f, .65f, 2f) * _scale;
                _label.fontSize = Mathf.RoundToInt(22*s); _small.fontSize = Mathf.RoundToInt(16*s);
                float cx = Screen.width*.5f, cy = Screen.height*.5f;
                if (_markers && now - _lastMarker < .22f)
                {
                    var color = _markerKill ? new Color(1,.65f,.25f,_opacity) : _markerHead ? new Color(1,.35f,.4f,_opacity) : new Color(.75f,1,1,_opacity);
                    float d = 7*s, n = 9*s; Line(cx-d-n,cy-1*s,n,2*s,color);Line(cx+d,cy-1*s,n,2*s,color);Line(cx-s,cy-d-n,2*s,n,color);Line(cx-s,cy+d,2*s,n,color);
                }
                var camera = GameInfo.CurCamera;
                if (_dynamic && camera)
                {
                    var held = _player.Holding.HeldItem; var weapon = held ? held.Weapon : null;
                    if (weapon)
                    {
                        float spread = _spread(weapon);
                        if (!float.IsNaN(spread) && !float.IsInfinity(spread))
                        {
                            float radius = Mathf.Clamp(Mathf.Tan(Mathf.Abs(spread)*Mathf.Deg2Rad)*Screen.height/(2*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)),3*s,120*s);
                            var color = new Color(.4f,.9f,1,_opacity*.65f);
                            Line(cx-radius,cy-2*s,s,4*s,color);Line(cx+radius,cy-2*s,s,4*s,color);Line(cx-2*s,cy-radius,4*s,s,color);Line(cx-2*s,cy+radius,4*s,s,color);
                        }
                    }
                }
                if (_numbers && camera)
                    for (int i = 0; i < 24; i++)
                    {
                        float age = now - Created[i]; if (Damages[i] <= 0 || age < 0 || age >= .9f) continue;
                        Vector3 p = camera.WorldToScreenPoint(Positions[i]); if (p.z <= 0) continue;
                        GUI.color = new Color(1, Heads[i] ? .65f : 1, Deaths[i] ? .4f : 1,_opacity*(1-age/.9f));
                        GUI.Label(new Rect(p.x-120*s,Screen.height-p.y-30*s-age*35*s,240*s,35*s),Labels[i],_label);
                    }
                if (_streaks && _state.Combo > 0)
                {
                    float heat = (float)_state.Heat(now,_window);
                    Line(cx-130*s,Screen.height*.19f,260*s,4*s,new Color(.05f,.12f,.18f,_opacity*.65f));
                    Line(cx-130*s,Screen.height*.19f,260*s*heat,4*s,new Color(.3f,.9f,1,_opacity));
                    GUI.color = new Color(.8f,1,1,_opacity);
                    GUI.Label(new Rect(cx-200*s,Screen.height*.19f-38*s,400*s,32*s),_comboLabel,_label);
                }
            }
            catch { _errors++; }
            finally { GUI.color = old; }
        }
    }
    // Registration supplies the exact wave objects, regardless of other birds in the scene.
    public sealed class GullChallengeRound
    {
        public readonly string Id;
        public Weapon Gun;
        public readonly Dictionary<int,Creature> Birds = new Dictionary<int,Creature>();
        public ProjectileManager LaunchManager;
        public readonly HashSet<uint> Projectiles = new HashSet<uint>();
        public readonly CombatHitContext PendingHits = new CombatHitContext();
        public bool Shooting, Impact;
        public string WaveId, Reason;
        public int Total, Hits, Kills;
        public float Started = -1, FirstHit = -1;
        public bool Pending => Started < 0 && Reason == null;
        public bool Active => Started >= 0 && Reason == null;

        public GullChallengeRound(string id,Weapon gun) { Id=id; Gun=gun; }
        public void Bind(IList registered,float now)
        {
            foreach (var entry in registered)
            {
                var item = entry as Item;
                var bird = item ? item.GetComponent<Bird>() : null;
                if (bird) Birds[bird.GetInstanceID()] = bird;
            }
            Total = Birds.Count;
            if (Total == 0 || Total != registered.Count)
                { Finish("游戏未登记完整的飞靶，未开始计分"); return; }
            Started = now;
        }
        public void RememberVolley(uint first,uint end,int pellets)
        {
            if (pellets <= 0 || unchecked(end-first) != (uint)pellets) return;
            for (int i = 0; i < pellets; i++) Projectiles.Add(unchecked(first+(uint)i));
        }
        public void Record(int target,float now,bool killed)
        {
            if (!Active || !(now >= Started && now - Started < 60) || !Birds.ContainsKey(target)) return;
            if (Hits == 0) FirstHit = now - Started;
            Hits++; if (killed) { Kills++; Birds.Remove(target); }
        }
        public void Tick(Player player,float now)
        {
            if (now - Started >= 60) { Finish("60 秒已到"); return; }
            if (IslandManager.IsLoading || !player.Dying || player.Dying.IsDead)
                { Finish("角色或世界状态已改变"); return; }
            var held = player.Holding ? player.Holding.HeldItem : null;
            if (!held || !Gun || held.Weapon != Gun) { Finish("已切换手持武器"); return; }
            foreach (var bird in Birds.Values) if (GameplayTargeting.IsAliveCreature(bird)) return;
            Finish("本轮飞靶均已击落或离场");
        }
        public void Finish(string reason)
        {
            Reason = reason;
            Gun = null; LaunchManager = null; Shooting = Impact = false; Birds.Clear(); Projectiles.Clear();
        }
        public JObject Snapshot()
        {
            return new JObject { ["roundId"] = Id, ["active"] = Active, ["total"] = Total,
                ["hits"] = Hits, ["kills"] = Kills, ["firstHitSeconds"] = FirstHit < 0 ? JValue.CreateNull() : new JValue(FirstHit),
                ["reason"] = Reason ?? "挑战进行中" };
        }
    }
}
