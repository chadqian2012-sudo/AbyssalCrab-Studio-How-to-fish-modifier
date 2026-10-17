using System.Collections.Generic;
using HowToFishTrainer.Core;
using System.Text.Json;

namespace AbyssalCrab.Runtime
{
    public static class SpinRegistry
    {
        private static IReadOnlyList<FeatureDescriptor> _cached;
        public static IReadOnlyList<FeatureDescriptor> Add(IReadOnlyList<FeatureDescriptor> baseline)
        {
            if (_cached != null) return _cached;
            var descriptors = new FeatureDescriptor[baseline.Count + 1];
            for (int i = 0; i < baseline.Count; i++) descriptors[i] = baseline[i];
            descriptors[baseline.Count] = new FeatureDescriptor("player.spin.lock", "玩家", "大陀螺",
                Capability.OwnedPlayer, FeatureControl.Toggle, 2d, 15d, false,
                "转速每秒 2–15 圈，默认 4 圈；第一人称镜头、移动和瞄准保持正常。开启时采用输入的转速，运行中点击“应用转速”更新；关闭后恢复原生姿态。");
            _cached = descriptors;
            return _cached;
        }

        public static bool ValidateSpinArguments(JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("enabled", out var enabled)) return false;
            if (enabled.ValueKind == JsonValueKind.False) return true;
            if (enabled.ValueKind != JsonValueKind.True || arguments.TryGetProperty("value", out _)) return false;
            if (!arguments.TryGetProperty("rotationsPerSecond", out var speed)) return true;
            return speed.ValueKind == JsonValueKind.Number && speed.TryGetDouble(out double number) &&
                !double.IsNaN(number) && !double.IsInfinity(number) && number >= 2d && number <= 15d;
        }
    }
}
