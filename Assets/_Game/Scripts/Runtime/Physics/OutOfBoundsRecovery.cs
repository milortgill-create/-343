using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Страховка от "предмет провалился сквозь пол".
    /// Раз в несколько кадров проверяет все зарегистрированные тела: если объект
    /// ушёл ниже KillY или его координаты стали NaN — возвращает на точку спавна
    /// и увеличивает счётчик. В нормальной работе счётчик всегда равен нулю;
    /// HUD показывает его, чтобы проблему было видно сразу.
    /// </summary>
    public class OutOfBoundsRecovery : MonoBehaviour
    {
        public float checkInterval = 0.5f;
        public float killY = SandboxConfig.KillY;
        public float arenaMargin = 8f;

        public int Recoveries { get; private set; }
        public int SpeedClamps { get; private set; }
        public int RegisteredCount => m_Items.Count;

        readonly List<Grabbable> m_Items = new List<Grabbable>();
        readonly List<DamageableDummy> m_Dummies = new List<DamageableDummy>();
        float m_Timer;

        public static OutOfBoundsRecovery Instance { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        /// <summary>Обнулить счётчик (после полного сброса полигона).</summary>
        public void ClearCounter()
        {
            Recoveries = 0;
        }

        public void Register(Grabbable item)
        {
            if (item != null && !m_Items.Contains(item)) m_Items.Add(item);
        }

        public void Register(DamageableDummy dummy)
        {
            if (dummy != null && !m_Dummies.Contains(dummy)) m_Dummies.Add(dummy);
        }

        void Update()
        {
            m_Timer += Time.deltaTime;
            if (m_Timer < checkInterval) return;
            m_Timer = 0f;
            Sweep();
        }

        public int Sweep()
        {
            int recovered = 0;
            float limit = Mathf.Max(SandboxConfig.ArenaWidth, SandboxConfig.ArenaDepth) * 0.5f + arenaMargin;

            for (int i = m_Items.Count - 1; i >= 0; i--)
            {
                var item = m_Items[i];
                if (item == null) { m_Items.RemoveAt(i); continue; }
                if (item.IsHeld) continue;

                var rb = item.Body;
                Vector3 p = rb != null ? rb.position : item.transform.position;

                bool bad = float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
                           || p.y < killY
                           || Mathf.Abs(p.x) > limit
                           || Mathf.Abs(p.z) > limit;
                if (!bad)
                {
                    // Страховка от пробоя пола: ничто не летит быстрее порога,
                    // при котором тело за один такт проходит сквозь плиту.
                    ClampSpeed(rb);
                    continue;
                }

                Debug.LogWarning($"[Sandbox] Предмет «{item.displayName}» вышел за пределы полигона (y={p.y:F2}) — возвращён на место.");
                item.ResetToSpawn();
                Recoveries++;
                recovered++;
            }

            foreach (var dummy in m_Dummies)
            {
                if (dummy == null) continue;
                if (dummy.LowestPoint() < killY) dummy.Recover();
            }

            return recovered;
        }

        void ClampSpeed(Rigidbody rb)
        {
            if (rb == null || rb.isKinematic) return;
            float max = SandboxConfig.MaxSafeSpeed;
            Vector3 v = rb.velocity;
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z))
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                SpeedClamps++;
                return;
            }
            if (v.sqrMagnitude > max * max)
            {
                rb.velocity = v.normalized * max;
                SpeedClamps++;
            }
            float maxSpin = SandboxConfig.MaxAngularVelocity * 1.5f;
            Vector3 w = rb.angularVelocity;
            if (w.sqrMagnitude > maxSpin * maxSpin)
            {
                rb.angularVelocity = w.normalized * maxSpin;
                SpeedClamps++;
            }
        }

        /// <summary>Проверка "ничего не провалилось" — используется самодиагностикой.</summary>
        public bool AllItemsInBounds(out string report)
        {
            int below = 0;
            float lowest = float.MaxValue;
            foreach (var item in m_Items)
            {
                if (item == null || item.IsHeld) continue;
                var cols = item.GetComponentsInChildren<Collider>();
                float minY = float.MaxValue;
                foreach (var c in cols) minY = Mathf.Min(minY, c.bounds.min.y);
                if (minY == float.MaxValue) continue;
                lowest = Mathf.Min(lowest, minY);
                if (minY < SandboxConfig.FloorTopY - 0.35f) below++;
            }
            report = $"предметов={m_Items.Count}, ниже пола={below}, самая низкая точка={(lowest == float.MaxValue ? 0f : lowest):F3} м, восстановлений={Recoveries}, ограничений скорости={SpeedClamps}";
            return below == 0 && Recoveries == 0;
        }
    }
}
