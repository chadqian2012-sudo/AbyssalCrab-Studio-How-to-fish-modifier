using System;
using System.Collections;
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
        private static object Call(object host, string name, object[] args)
        {
            try { return AccessTools.Method(host.GetType(), name).Invoke(host, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        public static bool TryHandle(TrainerBridgeHost host, JObject envelope)
        {
            string id = envelope.Value<string>("featureId");
            var args = envelope["arguments"] as JObject ?? new JObject();
            string correlation = envelope.Value<string>("correlationId");
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
        {if(_waveHost!=null&&host!=_waveHost)return;_wave=null;_waveHost=null;_count=0;}
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
}
