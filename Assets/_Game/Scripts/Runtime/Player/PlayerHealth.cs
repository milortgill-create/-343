using UnityEngine;

namespace Sandbox
{
    /// <summary>Здоровье игрока: тряска камеры, урон и падение в рэгдолл.</summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public float maxHealth = SandboxConfig.PlayerMaxHealth;
        public PlayerBody body;
        public PlayerView view;

        [Tooltip("Импульс (Н·с), после которого игрок падает сразу")]
        public float instantFallImpulse = 950f;
        [Tooltip("Скорость падения (м/с), после которой считается урон")]
        public float fallDamageSpeed = 12.5f;
        public float fallDamageScale = 6.5f;
        [Tooltip("Сколько секунд лежать, прежде чем встать автоматически")]
        public float autoGetUpDelay = 4.5f;

        public float Health { get; private set; }
        public bool IsDead => body != null && body.IsRagdolled;
        public int HitsTaken { get; private set; }

        float m_AutoTimer;

        public System.Action<float, DamageInfo> OnDamaged;
        public System.Action OnRespawned;

        void Start()
        {
            Health = maxHealth;
            if (body != null)
            {
                body.OnFallen += () => m_AutoTimer = autoGetUpDelay;
                body.OnGotUp += OnGotUp;
            }
            if (view != null)
            {
                view.OnLandedHook = OnLanded;
            }
        }

        void OnGotUp()
        {
            Health = maxHealth;
            m_AutoTimer = 0f;
            OnRespawned?.Invoke();
        }

        void Update()
        {
            if (!IsDead || m_AutoTimer <= 0f) return;
            m_AutoTimer -= Time.deltaTime;
            if (m_AutoTimer <= 0f && body != null) body.GetUp(false);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (IsDead) return;

            HitsTaken++;
            float amount = Mathf.Max(0f, info.amount);
            Health = Mathf.Clamp(Health - amount, 0f, maxHealth);

            if (view != null)
            {
                view.AddTrauma(Mathf.Clamp01(0.10f + amount / 55f));
                view.OnHit(info.impulse);
            }

            OnDamaged?.Invoke(amount, info);

            bool heavy = info.ImpulseMagnitude >= instantFallImpulse || Health <= 0f;
            if (heavy && body != null)
            {
                Vector3 impulse = Vector3.ClampMagnitude(info.impulse, 1600f) * (SandboxConfig.PlayerMass * 0.012f) + Vector3.up * 60f;
                body.Fall(impulse, info.point);
            }
        }

        void OnLanded(float fallSpeed)
        {
            if (fallSpeed <= fallDamageSpeed) return;
            float dmg = (fallSpeed - fallDamageSpeed) * fallDamageScale;
            ApplyDamage(new DamageInfo
            {
                amount = dmg,
                point = transform.position,
                impulse = Vector3.down * (fallSpeed * 12f),
                source = gameObject
            });
        }

        public void Respawn()
        {
            if (body != null) body.GetUp(true);
            Health = maxHealth;
        }

        public string Describe()
        {
            return $"HP {Health:F0}/{maxHealth:F0}{(IsDead ? " (ЛЕЖИТ)" : string.Empty)}";
        }
    }

    /// <summary>
    /// Триггер-«хитбокс» игрока.CharacterController сам по себе не получает
    /// OnCollisionEnter от летящих тел, поэтому урон считается через триггер:
    /// смотрим на скорость и массу влетевшего в нас предмета.
    /// </summary>
    public class PlayerHurtBox : MonoBehaviour
    {
        public PlayerHealth health;
        public float minSpeed = 3.4f;
        public float damageScale = 2.4f;
        public float impulseScale = 1.0f;
        public float cooldown = 0.28f;
        public float stayScale = 0.35f;

        float m_LastHit = -10f;

        void OnTriggerEnter(Collider other)
        {
            Evaluate(other, 1f);
        }

        void OnTriggerStay(Collider other)
        {
            Evaluate(other, stayScale);
        }

        void Evaluate(Collider other, float scale)
        {
            if (other == null || health == null || health.IsDead) return;
            if (other.gameObject.layer == Layers.Held) return;
            if (other.attachedRigidbody == null) return;
            if (other.isTrigger) return;
            if (Time.unscaledTime - m_LastHit < cooldown) return;

            var rb = other.attachedRigidbody;
            float speed = rb.velocity.magnitude;
            if (speed < minSpeed) return;

            m_LastHit = Time.unscaledTime;

            Vector3 point;
            try
            {
                point = other.ClosestPoint(transform.position);
            }
            catch (System.Exception)
            {
                point = other.bounds.center;
            }

            float mass = rb.mass;
            Vector3 impulse = rb.velocity * Mathf.Min(mass, 40f) * impulseScale;

            var info = new DamageInfo
            {
                amount = speed * damageScale * Mathf.Clamp(mass * 0.18f, 0.4f, 3.5f) * scale,
                point = point,
                impulse = impulse,
                source = other.gameObject,
                fromHeldItem = false,
                fromPlayer = false
            };
            health.ApplyDamage(info);
        }
    }
}
