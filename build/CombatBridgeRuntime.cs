using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HowToFishTrainer.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public static class CombatBridgeRuntime
    {
        private static int _count;
        private static TrainerBridgeHost _waveHost;
        private static object _wave;
        private static float _waveDeadline, _nextWaveCheck;
        private static int _waveIndex;
        private static AccessTools.FieldRef<TrainerBridgeHost,float> _lastCommand;
        public static bool Animal(string id)
        {
            return id == "piranha-rain" || id == "seagull-migration" || id == "fish-rain" || id == "crab-parade" ||
                id == "whale-fall" || id == "rare-fish-school" || id == "seafood-platter" || id == "seagull-feast" || id == "ocean-parade";
        }
        private static object Read(object obj, string name)
        {
            var property = AccessTools.Property(obj.GetType(), name);
            return property != null ? property.GetValue(obj, null) : AccessTools.Field(obj.GetType(), name).GetValue(obj);
        }
        internal static object Call(object host, string name, object[] args)
        {
            try { return AccessTools.Method(host.GetType(), name).Invoke(host, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        public static bool TryHandle(TrainerBridgeHost host, JObject envelope)
        {
            string id = envelope.Value<string>("featureId");
            var args = envelope["arguments"] as JObject ?? new JObject();
            string correlation = envelope.Value<string>("correlationId");
            if (id == "world.clear_drops")
            {
                DropCleanup.Start(host, correlation, envelope["arguments"] as JObject);
                return true;
            }
            if (id == "view.entity_overlay.configure")
            {
                Call(host, "ValidateAuthority", new object[] { id });
                float distance = MagicRangePolicy.CheckedDistance(args["distance"]);
                MagicRangeRenderer.ConfigureDistance(host, distance);
                Call(host, "Reply", new object[] { correlation, true, "实体标注显示距离已应用", new JObject { ["distance"] = distance }, false, true });
                return true;
            }
            if (id == "combat.feedback.configure")
            {
                CombatFeedbackRuntime.Configure(host, args);
                Call(host, "Reply", new object[] { correlation, true, "战斗反馈设置已应用", new JObject { ["enabled"] = args.Value<bool?>("enabled") ?? false }, false, true });
                return true;
            }
            var round = CombatFeedbackRuntime.RoundFor(host);
            bool start = id == "fun.challenge.start";
            if (start || id == "fun.challenge.stop")
            {
                Call(host,"ValidateAuthority",new object[]{"fun.event.execute"});
                var key = args["roundId"];
                Guid parsed;
                if (key == null || key.Type != JTokenType.String || !Guid.TryParseExact((string)key,"N",out parsed))
                    throw new ArgumentException("飞靶回合编号无效");
                if (round != null && round.Id == (string)key)
                {
                    if (round.Pending) throw new InvalidOperationException("本轮海鸥正在登记，请等待生成完成。");
                    if (!start && round.Active) round.Finish("已手动结束");
                    Call(host,"Reply",new object[]{correlation,true,"本轮飞靶成绩",new JObject { ["gullChallenge"]=round.Snapshot() },false,true});
                    return true;
                }
                if (!start)
                {
                    Call(host,"Reply",new object[]{correlation,true,"本轮已不存在，无法读取最终成绩",
                        new JObject { ["roundId"]=(string)key,["missing"]=true,["gullChallenge"]=JValue.CreateNull() },false,true});
                    return true;
                }
            }
            if (!start && (id != "fun.event.execute" || args["count"] == null)) return false;
            string eventId = start ? "seagull-migration" : args.Value<string>("eventId");
            if (!Animal(eventId)) throw new ArgumentException("此事件不支持自定义动物数量");
            if (args["count"] == null || args["count"].Type != JTokenType.Integer) throw new ArgumentException("动物数量需要整数");
            long count = args.Value<long>("count");
            if (count < 1 || count > 50) throw new ArgumentOutOfRangeException("count", "每波动物数量需要在 1–50 之间");
            // Use the original authority, preflight, registration, cancellation and rollback.
            if (!start) Call(host, "ValidateAuthority", new object[] { id });
            if (start) round = CombatFeedbackRuntime.PrepareRound(host,(string)args["roundId"]);
            _count = (int)count;
            try
            {
                Call(host, "BeginFunEvent", new object[] { correlation, eventId });
                _waveHost=host;_wave=Read(host,"_activeFunWave");_waveIndex=0;
                _waveDeadline=Time.realtimeSinceStartup+(start ? 28f : 10.5f);_nextWaveCheck=0;
                if (start) round.WaveId = (string)Read(_wave,"WaveId");
            }
            catch
            {
                if (start) round.Finish("飞靶生成失败");
                throw;
            }
            finally { _count = 0; }
            return true;
        }
        public static JObject ChallengeReceipt(JObject receipt, TrainerBridgeHost host, object wave)
        {
            var round = CombatFeedbackRuntime.RoundFor(host);
            if (round == null || !round.Pending || round.WaveId != (string)receipt["waveId"]) return receipt;
            round.Bind((IList)Read(wave,"Entities"),Time.realtimeSinceStartup);
            receipt["gullChallenge"] = round.Snapshot();
            return receipt;
        }
        public static void ChallengeRollback(TrainerBridgeHost host, string waveId)
        {
            var round = CombatFeedbackRuntime.RoundFor(host);
            if (round != null && round.Pending && round.WaveId != null && round.WaveId == waveId)
                round.Finish("飞靶登记未完成");
        }
        public static void MaintainWave(TrainerBridgeHost host)
        {
            try
            {
            if(_wave==null||host!=_waveHost||Time.realtimeSinceStartup<_nextWaveCheck)return;
            _nextWaveCheck=Time.realtimeSinceStartup+.5f;
            // A large wave is one in-flight request. Only actual native generation progress
            // can extend its heartbeat, and never beyond its 10.5s bounded deadline.
            // Native channel close, cancellation and error rollback continue to run normally.
            if(Time.realtimeSinceStartup>_waveDeadline||Read(host,"_activeFunWave")!=_wave){_wave=null;_waveHost=null;return;}
            int progress=(int)Read(_wave,"NextIndex");
            if(progress<=_waveIndex)return;
            _waveIndex=progress;
            if(_lastCommand==null)_lastCommand=AccessTools.FieldRefAccess<TrainerBridgeHost,float>("_lastCommandAt");
            _lastCommand(host)=Time.realtimeSinceStartup;
            }
            catch{_wave=null;_waveHost=null;}
        }
        public static void Reset(TrainerBridgeHost host)
        {DropCleanup.Cancel(host);if(_waveHost!=null&&host!=_waveHost)return;_wave=null;_waveHost=null;_count=0;}
        public static object AdjustDefinition(object definition)
        {
            if (definition == null || _count == 0 || !Animal((string)Read(definition,"Id"))) return definition;
            return Activator.CreateInstance(definition.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { Read(definition,"Id"), Read(definition,"Title"), Read(definition,"Risk"), _count }, null);
        }
        public static int AvailabilityMinimum(object definition)
        { return (string)Read(definition,"Id") == "ocean-parade" ? 1 : (int)Read(definition,"Count"); }
        public static bool HasCustomPlan() { return _count > 0; }
        public static object Build(TrainerBridgeHost host, object definition, object catalog, string seed)
        {
            string id = (string)Read(definition,"Id"); int count = (int)Read(definition,"Count");
            IList pool;
            if (id == "piranha-rain") pool = (IList)Call(host,"Piranhas",new object[]{catalog});
            else if (id == "ocean-parade") pool = (IList)Call(host,"MixParade",new object[]{catalog});
            else pool = (IList)Read(catalog,id == "seagull-migration" ? "Birds" : id == "crab-parade" ? "Crabs" : "Fish");
            var round = CombatFeedbackRuntime.RoundFor(host);
            if (id == "seagull-migration" && round != null && round.Pending && round.WaveId == null)
            {
                var birds = new System.Collections.Generic.List<object>();
                foreach (var value in pool)
                {
                    var item = value as Item;
                    var bird = item ? item.GetComponent<Bird>() : null;
                    if (bird && bird.BossType == BossType.None && !item.GetComponent<Albatross>()) birds.Add(item);
                }
                if (birds.Count == 0) throw new InvalidOperationException("当前目录没有可确认的普通海鸥。");
                pool = birds;
            }
            if (pool.Count == 0) throw new InvalidOperationException("当前游戏目录没有兼容动物");
            if (id == "whale-fall")
            {
                object largest = pool[0]; float score = float.NegativeInfinity;
                foreach (object candidate in pool) { float n = Convert.ToSingle(Call(host,"CreatureSizeScore",new[]{candidate})); if(n > score){largest=candidate;score=n;} }
                pool = new object[]{largest};
            }
            Type instructionType = host.GetType().GetNestedType("FunSpawnInstruction",BindingFlags.NonPublic);
            var result = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(instructionType));
            int seedValue = 17; foreach (char c in seed) seedValue = unchecked(seedValue*31+c);
            var random = new System.Random(seedValue);
            int fishCount = count == 1 ? 0 : Math.Max(1,count*3/7);
            for (int i = 0; i < count; i++)
            {
                bool bird = id == "seagull-migration" || id == "seagull-feast" && i >= fishCount;
                IList current = id == "seagull-feast" ? (IList)Read(catalog,bird?"Birds":"Fish") : pool;
                if(current.Count==0)throw new InvalidOperationException("当前游戏目录没有兼容动物");
                bool circle = id == "seafood-platter";
                float angle = count == 1 ? 0 : i*Mathf.PI*2/count;
                // Keep large waves close enough to the origin rather than extending a 50m line.
                float lateral = ((i%10) - (Math.Min(10,count)-1)/2f)*(bird?1.3f:1f);
                var offset = circle ? new Vector3(Mathf.Cos(angle)*3,1.2f,5+Mathf.Sin(angle)*2) :
                    new Vector3(lateral,id.IndexOf("rain",StringComparison.Ordinal)>=0||id=="whale-fall"?7+i%3:bird?5+i%2:.8f,4.5f+i%2+(i/10)*1.5f);
                Vector3? velocity = id == "seagull-migration" ? new Vector3(6,0,0) :
                    id == "piranha-rain" || id == "fish-rain" || id == "whale-fall" ? new Vector3(0,-3,0) : (Vector3?)null;
                bool kill = id == "fish-rain" || id == "whale-fall" || id == "seafood-platter";
                var rotation = circle ? Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0) : Quaternion.identity;
                result.Add(Activator.CreateInstance(instructionType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,
                    new object[]{current[random.Next(current.Count)],offset,rotation,velocity,kill,id=="rare-fish-school",bird?.18f:.12f},null));
            }
            return result;
        }
    }

    public sealed class DropCleanup
    {
        private static DropCleanup _active;
        private readonly TrainerBridgeHost _host;
        private readonly Transform _root;
        private readonly Player _player;
        private readonly byte _island;
        private readonly string _saveName;
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly List<KeyValuePair<Item, int>> _items = new List<KeyValuePair<Item, int>>();
        private int _next, _removed, _skipped, _failed;

        private DropCleanup(TrainerBridgeHost host, Transform root, string saveName)
        {
            _host = host;
            _root = root;
            _player = Player.LocalPlayer;
            _island = OnlineIslandManager.ActiveIsland;
            _saveName = saveName;
            // Each item is registered under both its root and its collider transforms.
            foreach (var pair in ItemManager.Items)
            {
                Item item = pair.Value;
                if (item && pair.Key == item.transform && IsClearableDrop(item, root))
                    _items.Add(new KeyValuePair<Item, int>(item, item.NetworkObject.ObjectId));
            }
        }

        public static bool IsClearableDrop(Item item, Transform worldRoot)
        {
            return item && item.transform.parent == worldRoot && item.NetworkObject &&
                !item.NetworkObject.IsSceneObject && item.IsServerInitialized && item.IsSpawned &&
                !item.IsDestroying && !item.IsQuestItem && !item.DeadPlayer &&
                !item.HasPlayerHolder && !item.IsInInventory && !item.WaitingForForceDrop &&
                !item.BirdHolder && !item.AttachedRod && (!item.Creature || item.Creature.IsDead);
        }

        public static void Cancel(TrainerBridgeHost host)
        {
            if (_active != null && _active._host == host) _active = null;
        }

        public static void Start(TrainerBridgeHost host, string correlation, JObject args)
        {
            if (args == null || (args.Count != 0 && (args.Count != 1 || args["operationId"] == null)))
                throw new ArgumentException("清理请求需要空参数或本次清理编号。");
            var server = Server.Instance;
            var manager = ItemManager.Instance;
            var player = Player.LocalPlayer;
            var save = SaveManager.CurServerSave;
            if (!server || !server.IsServerInitialized || !manager || !manager.IsServerInitialized ||
                !player || !server.DynamicObjectsHolder || !OnlineIslandManager.Instance || IslandManager.IsLoading || save == null)
            {
                Cancel(host);
                throw new InvalidOperationException("请在单人或房主世界加载完成后清理掉落物。");
            }
            Transform root = server.DynamicObjectsHolder;
            var token = args["operationId"];
            var job = _active;
            if (token == null)
            {
                job = new DropCleanup(host, root, save.Name);
                _active = job;
            }
            else
            {
                if (token.Type != JTokenType.String || job == null || job._id != (string)token || job._host != host)
                    throw new InvalidOperationException("本次清理已结束，请重新点击清理。");
                if (job._root != root || job._player != player || job._island != OnlineIslandManager.ActiveIsland || job._saveName != save.Name)
                {
                    Cancel(host);
                    throw new InvalidOperationException("房间或岛屿已切换，本次清理已停止。");
                }
            }
            job.ProcessBatch(manager);
            int remaining = job._items.Count - job._next;
            if (remaining == 0) _active = null;
            var result = new JObject { ["operationId"] = job._id, ["total"] = job._items.Count, ["remaining"] = remaining,
                ["removed"] = job._removed, ["skipped"] = job._skipped, ["failed"] = job._failed };
            string message = "已清理 " + job._removed + " 个掉落物";
            if (job._skipped != 0) message += "，已拾取或状态改变 " + job._skipped + " 个";
            if (job._failed != 0) message += "，失败 " + job._failed + " 个";
            if (remaining != 0) message += "，剩余 " + remaining + " 个";
            CombatBridgeRuntime.Call(host, "Reply", new object[] { correlation, true, message, result, false, true });
        }

        private void ProcessBatch(ItemManager manager)
        {
            float deadline = Time.realtimeSinceStartup + .002f;
            int end = Math.Min(_next + 32, _items.Count);
            while (_next < end)
            {
                var candidate = _items[_next++];
                Item item = candidate.Key;
                if (!IsClearableDrop(item, _root) || item.NetworkObject.ObjectId != candidate.Value)
                    _skipped++;
                else
                {
                    try
                    {
                        // Match the native island-unload path: despawn the network object without effects.
                        manager.Despawn(item.gameObject);
                        if (!item || !item.IsSpawned || item.NetworkObject.ObjectId != candidate.Value) _removed++;
                        else _failed++;
                    }
                    catch (Exception error)
                    {
                        if (_failed == 0) Debug.LogWarning("[AbyssalCrab] 清理掉落物失败：" + error.Message);
                        _failed++;
                    }
                }
                if (Time.realtimeSinceStartup >= deadline) break;
            }
        }
    }
}
