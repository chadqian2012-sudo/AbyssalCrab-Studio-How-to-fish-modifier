using System;

namespace AbyssalCrab.Runtime
{
    // Pure confirmed-event reducer, shared by the runtime and offline verification.
    public sealed class CombatFeedbackState
    {
        public int Hits, Headshots, Kills, Combo, BestCombo;
        public long Damage;
        public double LastKill = double.NegativeInfinity;

        public void Register(double now, int damage, bool head, bool killed, double window)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || damage <= 0) return;
            Tick(now, window);
            Hits++; Damage += damage;
            if (head) Headshots++;
            if (!killed) return;
            Kills++; Combo++; BestCombo = Math.Max(BestCombo, Combo); LastKill = now;
        }

        public void Tick(double now, double window)
        {
            if (now < LastKill || now - LastKill >= window) Combo = 0;
        }

        public double Heat(double now, double window)
        {
            Tick(now, window);
            return Combo == 0 ? 0 : Math.Min(1, Combo / 10.0) * Math.Max(0, 1 - (now - LastKill) / window);
        }

        public string Stage => Combo >= 10 ? "深渊暴走" : Combo >= 5 ? "狂潮" : Combo >= 3 ? "热身" : "蓄势";
    }
}
