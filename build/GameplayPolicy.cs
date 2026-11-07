using System;

namespace AbyssalCrab.Runtime
{
    public static class GameplayPolicy
    {
        public static double Maximum(string id)
        {
            if (id == "player.move.multiplier" || id == "player.sprint.multiplier") return 50;
            if (id == "player.jump.multiplier") return 25;
            return 20;
        }
        public static bool Number(double value, double min, double max) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        public static int Compare(int priorityA, double distanceA, int priorityB, double distanceB)
        { int rank = priorityA.CompareTo(priorityB); return rank != 0 ? rank : distanceA.CompareTo(distanceB); }
        public static void Direction(bool w, bool a, bool s, bool d, out float x, out float z)
        {
            x = (d ? 1 : 0) - (a ? 1 : 0); z = (w ? 1 : 0) - (s ? 1 : 0);
            if (x == 0 && z == 0) z = 1;
            float length = (float)Math.Sqrt(x * x + z * z); x /= length; z /= length;
        }
        public static bool Trigger(string mode, bool ads) => mode == "fire" || mode == "ads" && ads;
        public static float TurnRadians(float strength, float dt) => (float)(Math.PI * 4 * strength / 100 * Math.Max(0, dt));
        public static int Lethal(int health, int original) => Math.Max(original, Math.Max(1, health));
        public static int BeforeMultiplier(int health, int original, double multiplier)
        {
            if (!Number(multiplier, double.Epsilon, double.MaxValue)) return original;
            double safe = Math.Max(1, Math.Min(int.MaxValue, (int.MaxValue - 4096d) / Math.Max(1, multiplier)));
            return (int)Math.Min(safe, Math.Max(original, Math.Ceiling(Math.Max(1, health) / multiplier)));
        }
    }
}
