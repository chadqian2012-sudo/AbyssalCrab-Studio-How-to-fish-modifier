using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public sealed class AimTargetTrack
    {
        public Component Target;
        public Creature Creature;
        public Player Player;
        public Transform Transform;
        public Rigidbody Rig;
        public Collider[] Colliders;
        public CapsuleCollider Capsule;
        public Vector3 LocalHead, SamplePosition, SampleVelocity;
        public bool HasHead, SampleReady;
        public float NextHeadCheck, SampleTime, DistanceSquared;
    }
}
