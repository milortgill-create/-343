using UnityEngine;

namespace Sandbox
{
    /// <summary>Описание полученного урона/удара.</summary>
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 impulse;      // Н·с
        public GameObject source;
        public bool fromHeldItem;
        public bool fromPlayer;

        public float ImpulseMagnitude => impulse.magnitude;

        public static DamageInfo Punch(Vector3 point, Vector3 impulse, GameObject source, bool heldItem)
        {
            return new DamageInfo
            {
                amount = 8f + impulse.magnitude * 0.06f,
                point = point,
                impulse = impulse,
                source = source,
                fromHeldItem = heldItem,
                fromPlayer = true
            };
        }

        public static DamageInfo FromCollision(Collision collision, GameObject source, float scale)
        {
            Vector3 impulse = collision.impulse;
            float speed = collision.relativeVelocity.magnitude;
            return new DamageInfo
            {
                amount = speed * scale,
                point = collision.contacts.Length > 0 ? collision.contacts[0].point : collision.transform.position,
                impulse = impulse,
                source = source,
                fromHeldItem = false,
                fromPlayer = false
            };
        }
    }

    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo info);
        bool IsDead { get; }
    }
}
