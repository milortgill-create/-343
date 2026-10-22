using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Манекен-болванка для битья.
    /// Стоит за счёт приводов рэгдолла (таз закреплён), реагирует на удары,
    /// а при сильном ударе обмякает и улетает — падает и кувыркается.
    /// Через несколько секунд сам «восстаёт» на точке спавна, чтобы песочницу
    /// можно было тестировать без перезапуска.
    /// </summary>
    public class DamageableDummy : MonoBehaviour, IDamageable
    {
        [Header("Ссылки")]
        public RagdollController ragdoll;
        public string displayName = "Манекен";

        [Header("Живучесть")]
        public float maxHealth = 100f;
        [Tooltip("Урон, после которого манекен падает")]
        public float fallDamageThreshold = 42f;
        [Tooltip("Импульс (Н·с), после которого манекен отлетает")]
        public float fallImpulseThreshold = SandboxConfig.RagdollImpulseThreshold;
        [Tooltip("Секунд до автоматического «восстания» (0 = не вставать)")]
        public float autoResetDelay = 9f;

        [Header("Отклик")]
        [Range(0f, 3f)] public float impulseScale = 1.35f;
        public float flinchDuration = 0.26f;

        public float Health { get; private set; }
        public bool IsDead { get; private set; }
        public int HitsTaken { get; private set; }
        public float LastImpulse { get; private set; }

        Vector3 m_SpawnPos;
        Quaternion m_SpawnRot;
        float m_ResetTimer;

        public System.Action<DamageableDummy, DamageInfo> OnHit;
        public System.Action<DamageableDummy> OnFallen;
        public System.Action<DamageableDummy> OnRecovered;

        void Start()
        {
            m_SpawnPos = transform.position;
            m_SpawnRot = transform.rotation;
            Health = maxHealth;

            if (ragdoll != null && ragdoll.IsBuilt)
            {
                ragdoll.SetLayer(Layers.Dummy);
                ragdoll.SnapToBindPose(m_SpawnPos, m_SpawnRot);
                ragdoll.SetUpright(true);
            }
        }

        public void RememberSpawn()
        {
            m_SpawnPos = transform.position;
            m_SpawnRot = transform.rotation;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (ragdoll == null) return;

            HitsTaken++;
            LastImpulse = info.ImpulseMagnitude;
            Health = Mathf.Max(0f, Health - info.amount);

            Vector3 impulse = info.impulse * impulseScale;
            Vector3 point = info.point;

            if (!IsDead)
            {
                AudioPool.PlayImpact(ImpactSound.Soft, point, Mathf.Clamp01(info.amount / 45f));
                ragdoll.Flinch(flinchDuration, 0.22f);
                ragdoll.AddImpulseAt(point, impulse * 0.35f);
            }
            else
            {
                // уже лежит — просто пинаем дальше
                AudioPool.PlayImpact(ImpactSound.Soft, point, Mathf.Clamp01(info.amount / 45f));
                ragdoll.AddImpulseAt(point, impulse);
            }

            OnHit?.Invoke(this, info);

            if (!IsDead && (Health <= fallDamageThreshold || info.ImpulseMagnitude >= fallImpulseThreshold))
            {
                Fall(point, impulse);
            }
        }

        void Fall(Vector3 point, Vector3 impulse)
        {
            IsDead = true;
            m_ResetTimer = autoResetDelay;

            ragdoll.GoLimp(18f, 5f);
            // небольшой подъём, чтобы тело эффектно отлетело и кувыркнулось
            Vector3 launch = impulse + Vector3.up * Mathf.Min(90f, impulse.magnitude * 0.22f);
            ragdoll.AddImpulseAt(point, launch);
            AudioPool.PlayClip(ProceduralAudio.RagdollHit, point, 0.9f, Random.Range(0.9f, 1.1f));

            OnFallen?.Invoke(this);
        }

        void Update()
        {
            if (!IsDead || autoResetDelay <= 0f) return;

            // не «восстаём», пока тело ещё кувыркается
            if (ragdoll != null && ragdoll.MaxBodySpeed() > 1.6f) return;

            m_ResetTimer -= Time.deltaTime;
            if (m_ResetTimer <= 0f) Recover();
        }

        public void Recover()
        {
            if (ragdoll == null) return;
            ragdoll.StandUp(m_SpawnPos, m_SpawnRot, true);
            Health = maxHealth;
            IsDead = false;
            m_ResetTimer = 0f;
            OnRecovered?.Invoke(this);
        }

        /// <summary>Если тело ушло под пол — вернуть на место.</summary>
        public float LowestPoint()
        {
            if (ragdoll == null) return transform.position.y;
            float y = ragdoll.LowestPoint();
            if (y < SandboxConfig.KillY)
            {
                Debug.LogWarning($"[Sandbox] {displayName} провалился под пол (y={y:F2}) — восстановлен.");
                Recover();
                if (OutOfBoundsRecovery.Instance != null) return y;
            }
            return y;
        }

        public Vector3 ChestPoint()
        {
            if (ragdoll == null) return transform.position + Vector3.up * 1.3f;
            var chest = ragdoll.Chest;
            return chest != null ? chest.position : transform.position + Vector3.up * 1.3f;
        }

        public string Describe()
        {
            string state = IsDead ? "ЛЕЖИТ" : "СТОИТ";
            return $"{displayName}: HP {Health:F0}/{maxHealth:F0}, ударов {HitsTaken}, {state}";
        }
    }
}
