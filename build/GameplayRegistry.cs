using System.Collections.Generic;
using System.Text.Json;
using HowToFishTrainer.Core;

namespace AbyssalCrab.Runtime
{
    public static class GameplayRegistry
    {
        private static IReadOnlyList<FeatureDescriptor> _cached;
        public static IReadOnlyList<FeatureDescriptor> Add(IReadOnlyList<FeatureDescriptor> original)
        {
            if (_cached != null) return _cached;
            var result = new List<FeatureDescriptor>(original.Count + 3);
            foreach (var f in original)
            {
                if (f.Id == "player.move.multiplier" || f.Id == "player.sprint.multiplier" || f.Id == "player.jump.multiplier")
                    result.Add(new FeatureDescriptor(f.Id, f.Category, f.Title, f.RequiredCapability, f.Control, f.Minimum, GameplayPolicy.Maximum(f.Id), f.Advanced, f.Description));
                else if (f.Id == "combat.one_hit")
                    result.Add(new FeatureDescriptor(f.Id, f.Category, "一击必杀", f.RequiredCapability, f.Control, f.Minimum, f.Maximum, f.Advanced,
                        "本机玩家的枪械、近战、投掷命中及归属明确的爆炸攻击一击致命；保留游戏的命中判定、友伤开关和 Boss 阶段免疫。"));
                else if (f.Id == "combat.silent_aim")
                    result.Add(new FeatureDescriptor(f.Id, f.Category, f.Title, f.RequiredCapability, f.Control, f.Minimum, f.Maximum, f.Advanced,
                        "自定义捕获角度、距离、追踪强度与触发方式；优先 Boss、其次可攻击动物、最后玩家，同类优先最近目标。开枪修正弹道，实体子弹持续转向，保留遮挡、弹速、散布和伤害结算，不移动镜头。"));
                else result.Add(f);
            }
            result.Add(new FeatureDescriptor("player.air_dash.lock", "玩家", "空中快速冲刺", Capability.OwnedPlayer, FeatureControl.Toggle,
                null, null, false, "空中按 Alt，配合 W/A/S/D 向对应方向冲刺，支持斜向；未按方向键默认向前。距离随已应用的移动速度倍率变化，保留垂直速度和碰撞；每次按下触发一次。"));
            result.Add(new FeatureDescriptor("unlock.islands", "解锁中心", "解锁所有岛屿", Capability.HostWorld, FeatureControl.Action,
                null, null, false, "解锁当前游戏构建中有效的全部岛屿，并调用原生世界保存；不会自动传送。"));
            result.Add(new FeatureDescriptor("world.clear_drops", "金钱背包", "一键清理掉落物", Capability.HostWorld, FeatureControl.Action,
                null, null, false, "清理散落的武器、普通物品、死鱼和可拾取的动物尸体，也清理能确认由修改器召唤的活体。野生活体、玩家遗体、任务物品，以及手持、背包和鱼钩上的物品都会保留。"));
            return _cached = result.AsReadOnly();
        }
        public static bool Handles(string id) => id == "combat.silent_aim" || id == "player.air_dash.lock" ||
            id == "player.move.multiplier" || id == "player.sprint.multiplier" || id == "player.jump.multiplier";
        public static bool Validate(FeatureDescriptor f, JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object) return false;
            if (f.Control == FeatureControl.Number) return Numeric(args, "value", .25, GameplayPolicy.Maximum(f.Id), false);
            if (!args.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False) return false;
            if (enabled.ValueKind == JsonValueKind.False || f.Id != "combat.silent_aim") return true;
            if (!Numeric(args, "angle", .5, 180, true) || !Numeric(args, "distance", 1, 1000, true) || !Numeric(args, "strength", 0, 100, true)) return false;
            return !args.TryGetProperty("trigger", out var trigger) || trigger.ValueKind == JsonValueKind.String && (trigger.GetString() == "ads" || trigger.GetString() == "fire");
        }
        private static bool Numeric(JsonElement a, string key, double min, double max, bool optional)
        { return a.TryGetProperty(key, out var t) ? t.ValueKind == JsonValueKind.Number && t.TryGetDouble(out double n) && GameplayPolicy.Number(n, min, max) : optional; }
    }
}
