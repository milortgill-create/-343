using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Обрабатывает столкновения предмета: звук, пыль, урон тому, во что попали,
    /// и разрушение (для бутылок). Стоит на всех физических объектах карты.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactHandler : MonoBehaviour
    {
        [Header("Пороги")]
        [Tooltip("Минимальная скорость удара (м/с), чтобы вообще что-то произошло")]
        public float minImpactSpeed = 1.4f;
        [Tooltip("Скорость, после которой летят искры/пыль")]
        public float dustSpeed = 3.2f;
        [Tooltip("Масштаб урона от скорости предмета")]
        public float damageScale = SandboxConfig.ThrowImpactDamageScale;

        public bool SuppressWhileHeld { get; set; }

        Rigidbody m_Body;
        Grabbable m_Grabbable;
        float m_ThrownTimer;
        float m_LastSoundTime;

        public bool WasRecentlyThrown => m_ThrownTimer > 0f;

        protected virtual void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Grabbable = GetComponent<Grabbable>();
        }

        void Update()
        {
            if (m_ThrownTimer > 0f) m_ThrownTimer -= Time.deltaTime;
        }

        public void MarkAsThrown(float seconds)
        {
            m_ThrownTimer = Mathf.Max(m_ThrownTimer, seconds);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (SuppressWhileHeld) return;
            if (collision == null || collision.contactCount == 0) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;

            ContactPoint contact = collision.GetContact(0);
            Vector3 point = contact.point;
            Vector3 normal = contact.normal;

            // --- звук (не чаще 12 раз в секунду, чтобы не строчило) ---
            if (Time.unscaledTime - m_LastSoundTime > 0.06f)
            {
                m_LastSoundTime = Time.unscaledTime;
                AudioPool.PlayImpact(SoundFor(collision), point, Mathf.Clamp01((speed - minImpactSpeed) / 12f));
            }

            // --- пыль/искры ---
            if (speed > dustSpeed)
            {
                FxPool.SpawnDust(point, normal, Mathf.Clamp01(speed / 14f));
            }

            // --- урон тому, во что попали ---
            float damage = speed * damageScale * (m_Grabbable != null ? m_Grabbable.damageMultiplier : 1f);
            if (WasRecentlyThrown) damage *= 1.35f;

            var target = collision.collider != null ? collision.collider.GetComponentInParent<IDamageable>() : null;
            if (target != null && damage > 1f)
            {
                Vector3 impulse = collision.impulse;
                if (impulse.sqrMagnitude < 0.0001f && m_Body != null)
                    impulse = m_Body.velocity * Mathf.Max(0.1f, m_Body.mass);

                target.ApplyDamage(new DamageInfo
                {
                    amount = damage,
                    point = point,
                    impulse = impulse,
                    source = gameObject,
                    fromHeldItem = m_Grabbable != null && m_Grabbable.IsHeld,
                    fromPlayer = false
                });
            }

            // --- разрушаемые предметы ---
            var breakable = GetComponent<Breakable>();
            if (breakable != null) breakable.TryBreak(speed, point, collision.impulse);
        }

        ImpactSound SoundFor(Collision collision)
        {
            if (m_Grabbable != null) return m_Grabbable.impactSound;
            // если предмета нет — определяем по тому, во что ударили
            var other = collision.collider != null ? collision.collider.GetComponentInParent<Grabbable>() : null;
            if (other != null) return other.impactSound;
            return ImpactSound.Concrete;
        }
    }
}
