using System;
using System.Collections.Generic;
using System.Reflection;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;

namespace AbyssalCrab.Runtime
{
    // Bootstrap can be injected while Assembly-CSharp is still loading. Only failed optional
    // jobs are retained; successfully installed hooks are never replayed by this retry loop.
    public static class RuntimeHookRecovery
    {
        private static readonly object Gate = new object();
        private static readonly List<string> PendingNames = new List<string>();
        private static readonly List<Action> PendingActions = new List<Action>();
        private static readonly HashSet<string> Installed = new HashSet<string>(StringComparer.Ordinal);
        private static Action<string, Exception> _report;
        private static long _nextRetry;
        private static int _retryPasses;
        private static string _lastError;

        public static void RunOptionalHooks(IEnumerable<(string, Action)> actions, Action<string, Exception> report)
        {
            lock (Gate) RunOptionalHooksCore(actions, report);
        }

        private static void RunOptionalHooksCore(IEnumerable<(string, Action)> actions, Action<string, Exception> report)
        {
            _report = report;
            PendingNames.Clear(); PendingActions.Clear(); Installed.Clear();
            foreach (var job in actions)
            {
                try { job.Item2(); Installed.Add(job.Item1); }
                catch (Exception e)
                {
                    PendingNames.Add(job.Item1); PendingActions.Add(job.Item2);
                    _lastError = job.Item1 + ": " + e.GetType().Name + ": " + e.Message;
                    if (_report != null) _report(job.Item1, e);
                }
            }
        }

        public static void RetryPendingHooks()
        {
            lock (Gate) RetryPendingHooksCore();
        }

        private static void RetryPendingHooksCore()
        {
            if (PendingActions.Count == 0 || DateTime.UtcNow.Ticks < _nextRetry) return;
            _nextRetry = DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond;
            Type player = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Assembly-CSharp") continue;
                player = assembly.GetType("Player", false);
                break;
            }
            if (player == null) return;
            _retryPasses++;
            for (int i = PendingActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    PendingActions[i](); Installed.Add(PendingNames[i]);
                    PendingActions.RemoveAt(i); PendingNames.RemoveAt(i);
                }
                catch (Exception e)
                {
                    _lastError = PendingNames[i] + ": " + e.GetType().Name + ": " + e.Message;
                    // Keep an unsupported optional feature isolated without flooding Player.log.
                    if (_retryPasses == 1 && _report != null) _report(PendingNames[i], e);
                }
            }
        }

        public static void EnsureCombatReady(string id, JObject args)
        {
            if (id != "combat.aim_assist" && id != "combat.silent_aim") return;
            if (args.Value<bool?>("enabled") == false) return;
            RetryPendingHooks();
            lock (Gate)
            {
                bool ready = id == "combat.silent_aim" ? Installed.Contains("silent-aim-projectiles") : AimReady();
                if (!ready) throw new InvalidOperationException("瞄准钩子尚未安装，正在等待游戏程序集加载并自动重试；请稍后再开启。" + (_lastError ?? ""));
            }
        }

        private static bool AimReady() => Installed.Contains("aim-assist-eligibility") &&
            Installed.Contains("aim-assist-trigger") && Installed.Contains("aim-assist-rotation");

        public static JToken AnnotateSession(JToken session)
        {
            lock (Gate) return AnnotateSessionCore(session);
        }

        private static JToken AnnotateSessionCore(JToken session)
        {
            JObject obj = session as JObject;
            if (obj != null)
            {
                var diagnostic = new JObject();
                diagnostic["installed"] = Installed.Count;
                diagnostic["pending"] = new JArray(PendingNames);
                diagnostic["retryPasses"] = _retryPasses;
                diagnostic["aimAssistReady"] = AimReady();
                diagnostic["silentAimReady"] = Installed.Contains("silent-aim-projectiles");
                diagnostic["lastFailure"] = _lastError;
                try
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (assembly.GetName().Name != "Assembly-CSharp") continue;
                        Type playerType = assembly.GetType("Player", false);
                        object player = playerType == null ? null : playerType.GetField("LocalPlayer", flags).GetValue(null);
                        object assist = player == null ? null : playerType.GetProperty("AimAssist", flags).GetValue(player, null);
                        if (assist != null)
                        {
                            diagnostic["nativeAimEligible"] = (bool)assist.GetType().GetMethod("CanUseAimAssist", flags).Invoke(assist, null);
                            diagnostic["nativeAimTrigger"] = (bool)assist.GetType().GetMethod("IsAimingDownSights", flags).Invoke(assist, null);
                        }
                        break;
                    }
                }
                catch (Exception e) { diagnostic["nativeProbeUnavailable"] = e.GetType().Name; }
                obj["runtimeHooks"] = diagnostic;
            }
            return session;
        }
    }
}
