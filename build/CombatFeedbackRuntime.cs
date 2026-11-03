using System;
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
            if (!enabled) { Reset(host); return; }
            // Actual Hp deltas are authoritative only in a local host/single-player world.
            if (!Player.LocalPlayer || !Player.LocalPlayer.IsServerInitialized)
                throw new InvalidOperationException("战斗反馈需要本机房主的实际伤害结算。");
            if (!_enabled || _owner != host)
            {
                Reset(null);
                _owner = host; _player = Player.LocalPlayer; _scene = SceneManager.GetActiveScene().handle.GetHashCode();
                _mainThread=Thread.CurrentThread.ManagedThreadId;
                _state = new CombatFeedbackState();
                _pending = new CombatHitContext();
                try
                {
                    _spread = AccessTools.FieldRefAccess<Weapon,float>("_spread");
                    _lastCommand = AccessTools.FieldRefAccess<TrainerBridgeHost,float>("_lastCommandAt");
                    var hit = AccessTools.Method(typeof(Creature), "LocalHit");
                    var server = AccessTools.Method(typeof(Server), "RpcLogic___HitCreature___215526726");
                    if (hit == null || server == null) throw new MissingMethodException("当前游戏伤害接口不匹配");
                    _harmony = new Harmony("AbyssalCrab.CombatFeedback." + Guid.NewGuid().ToString("N"));
                    _harmony.Patch(hit, new HarmonyMethod(typeof(CombatFeedbackRuntime), "LocalPrefix"));
                    _harmony.Patch(server, new HarmonyMethod(typeof(CombatFeedbackRuntime), "DamagePrefix"), new HarmonyMethod(typeof(CombatFeedbackRuntime), "DamagePostfix"));
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
                catch { Reset(null); throw; }
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
            if (!_enabled || fromNpc || player != Player.LocalPlayer || !__instance || __instance.IsDead) return;
            try { _pending.Track(__instance.GetInstanceID(),point.x,point.y,point.z,Time.unscaledTime,
                rangedHit && __instance.transform.InverseTransformPoint(point).z > __instance.HeadPos); } catch { _errors++; }
        }
        public static void DamagePrefix(Creature __0, Player __1, out int __state)
        {
            __state = -1;
            if (_enabled && __0 && !__0.IsDeinitializing && __0.IsServerInitialized && __1 == _player && _player == Player.LocalPlayer)
                __state = __0.Hp;
        }
        public static void DamagePostfix(Creature __0, Vector3 __3, int __state)
        {
            if (!_enabled || __state <= 0 || !__0) return;
            try
            {
                int damage = Math.Max(0, __state - Math.Max(0, __0.Hp));
                if (damage == 0) return;
                float now = Time.unscaledTime;
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
            if (!_enabled || host != _owner) return;
            try
            {
                if (!_player || _player != Player.LocalPlayer || !_player.IsServerInitialized ||
                    SceneManager.GetActiveScene().handle.GetHashCode() != _scene || Time.realtimeSinceStartup - _lastCommand(host) > 5)
                { Reset(host); return; }
                _state.Tick(Time.unscaledTime, _window);
            }
            catch { Reset(host); }
        }
        public static JToken Describe(JToken session)
        {
            var root = session as JObject; if (root == null) return session;
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
            _enabled = false; _cleanupRequested=false; _player = null; _owner = null;
            try { if (_harmony != null) _harmony.UnpatchAll(_harmony.Id); } catch { }
            _harmony = null;
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
}
