using System;

namespace AbyssalCrab.Runtime
{
    public static class AimLockPolicy
    {
        public static bool Eligible(float distanceSquared, float alignment, float maximumSquared, float minimumAlignment)
        {
            return !float.IsNaN(distanceSquared) && !float.IsInfinity(distanceSquared) && distanceSquared > .000001f
                && distanceSquared <= maximumSquared && !float.IsNaN(alignment) && !float.IsInfinity(alignment)
                && (minimumAlignment <= -1 || alignment >= minimumAlignment);
        }

        public static int CompareDistance(float left, float right) => left.CompareTo(right);

        public static bool RetryNow(bool enteringAds,bool lostTarget,float now,float nextScan)
        {return enteringAds||lostTarget||now>=nextScan;}

        public static float TrackingWeight(float strength, float dt, float manualLook)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return 0;
            float sharpness = 40f + Math.Max(0, Math.Min(100, strength)) * .8f;
            return (float)(1 - Math.Exp(-sharpness * Math.Min(dt, .1f))) * (1 - Math.Max(0, Math.Min(1, manualLook)));
        }
    }
}
