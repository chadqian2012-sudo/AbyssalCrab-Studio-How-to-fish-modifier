using System;

namespace AbyssalCrab.Runtime
{
    public sealed class SpinPosePolicy
    {
        public const float LookDownPitch = 89f;
        public const float TorsoLean = 18f;
        public const float HopDuration = 0.70f;
        public const float GroundPause = 0.20f;
        private double _nextHop = double.NaN;
        private double _lastTime = double.NaN;
        public bool SyntheticAirborne { get; private set; }
        public float HeightFraction { get; private set; }

        public void Reset()
        {
            _nextHop = _lastTime = double.NaN;
            SyntheticAirborne = false;
            HeightFraction = 0f;
        }

        public void Advance(double now, bool autoJump, bool grounded, bool blocked)
        {
            SyntheticAirborne = false;
            HeightFraction = 0f;
            if (double.IsNaN(now) || double.IsInfinity(now)) { Reset(); return; }
            if (!autoJump || !grounded || blocked || !double.IsNaN(_lastTime) && now < _lastTime)
            {
                Reset();
                _lastTime = now;
                return;
            }
            _lastTime = now;
            if (double.IsNaN(_nextHop)) _nextHop = now + GroundPause;
            double elapsed = now - _nextHop;
            if (elapsed < 0d) return;
            double phase = elapsed % (HopDuration + GroundPause);
            if (phase >= HopDuration) return;
            float progress = (float)(phase / HopDuration);
            SyntheticAirborne = true;
            HeightFraction = 4f * progress * (1f - progress);
        }

        public static float NativeBlend(float speed, float deltaTime)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) return 0f;
            return Math.Min(1f, speed * deltaTime);
        }
    }
}
