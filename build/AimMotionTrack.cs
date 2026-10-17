using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public sealed class AimMotionTrack
    {
        public Object Target;
        public Vector3 Position;
        public Vector3 Velocity;
        public float SampleTime;
        public bool Initialized;
    }
}
