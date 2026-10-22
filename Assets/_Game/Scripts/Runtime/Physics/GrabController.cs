using System.Collections;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Держит предмет в руке и бросает его с реалистичной траекторией.
    ///
    /// Пока предмет в руках, его Rigidbody кинематический и ведётся к якорю через
    /// MovePosition (это стабильно на мобильных и не даёт дрожи, в отличие от
    /// ConfigurableJoint-захвата). Скорость якоря накапливается и добавляется к
    /// броску — поэтому предмет реально летит туда и так, как вы вели руку.
    /// На броске включается ContinuousDynamic CCD + жёсткий потолок скорости,
    /// что вместе с метровым коллайдером пола исключает проваливание.
    /// </summary>
    public class GrabController : MonoBehaviour
    {
        [Header("Точки")]
        [Tooltip("Якорь в руке (обычно дочерний объект камеры)")]
        public Transform holdAnchor;
        [Tooltip("Коллайдер игрока — с ним предмет временно не сталкивается после броска")]
        public Collider playerCollider;

        [Header("Параметры")]
        public float grabLerp = SandboxConfig.GrabLerp;
        public float baseThrowSpeed = SandboxConfig.BaseThrowSpeed;
        public float handVelocityShare = SandboxConfig.ThrowSpeedPerHandVel;
        public float maxThrowSpeed = SandboxConfig.MaxThrowSpeed;
        public float releaseDistance = 0.55f;
        [Tooltip("Дальше этого предмет не «висит» от ладони")]
        public float maxHoldDistance = 0.34f;
        public float spinImpulse = 3.2f;
        public float postThrowIgnoreTime = 0.30f;

        public Grabbable Held { get; private set; }
        public bool HasItem => Held != null && Held.Body != null;
        public Vector3 HeldVelocity { get; private set; }
        public Vector3 HoldPoint => holdAnchor != null ? holdAnchor.position : transform.position + transform.forward;

        Vector3 m_PosOffset;
        Quaternion m_RotOffset;
        Vector3 m_PrevHoldPoint;
        Vector3 m_SmoothedAnchorVelocity;
        Coroutine m_IgnoreRoutine;
        bool m_HoldingFlag;

        public System.Action<Grabbable> OnGrabbed;
        public System.Action<Grabbable, float> OnThrown;
        public System.Action<Grabbable> OnDropped;

        void OnEnable()
        {
            m_PrevHoldPoint = HoldPoint;
        }

        public bool TryGrab(Grabbable item, Vector3 grabWorldPoint)
        {
            if (item == null || item.Body == null) return false;
            if (Held != null) Drop();

            Held = item;
            m_HoldingFlag = true;
            item.OnGrabbed();

            Vector3 anchorPos = HoldPoint;
            Quaternion anchorRot = holdAnchor != null ? holdAnchor.rotation : transform.rotation;

            // Предмет подтягивается так, чтобы точка захвата оказалась в руке,
            // но не дальше maxHoldDistance — иначе дальнобойный захват выглядит
            // как "предмет висит в двух метрах от ладони".
            Vector3 toItem = item.transform.position - anchorPos;
            Vector3 desiredPos = toItem.magnitude > maxHoldDistance
                ? anchorPos + toItem.normalized * maxHoldDistance
                : item.transform.position + (anchorPos - grabWorldPoint);

            m_PosOffset = Quaternion.Inverse(anchorRot) * (desiredPos - anchorPos);
            // сохраняем поворот предмета относительно руки — он естественно
            // вращается вместе со взглядом
            m_RotOffset = Quaternion.Inverse(anchorRot) * item.transform.rotation;

            item.Body.position = desiredPos;
            item.Body.rotation = anchorRot * m_RotOffset;
            Physics.SyncTransforms();

            m_PrevHoldPoint = anchorPos;
            m_SmoothedAnchorVelocity = Vector3.zero;
            HeldVelocity = Vector3.zero;

            if (m_IgnoreRoutine != null) StopCoroutine(m_IgnoreRoutine);
            m_IgnoreRoutine = StartCoroutine(IgnorePlayerCollisions(postThrowIgnoreTime));

            OnGrabbed?.Invoke(item);
            return true;
        }

        /// <summary>Бросок: направление + добавка от движения руки, потолок скорости.</summary>
        public float Throw(Vector3 direction)
        {
            if (Held == null) return 0f;
            var item = Held;
            var rb = item.Body;

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

            // отпускаем чуть впереди камеры, чтобы предмет не оказался внутри игрока
            Vector3 releasePoint = HoldPoint + dir * releaseDistance;
            if (rb != null)
            {
                rb.position = releasePoint;
                rb.rotation = (holdAnchor != null ? holdAnchor.rotation : transform.rotation) * m_RotOffset;
            }
            Physics.SyncTransforms();

            float handSpeed = HeldVelocity.magnitude;
            float speed = Mathf.Min(maxThrowSpeed, baseThrowSpeed / Mathf.Max(0.35f, item.heaviness)
                                                      + handSpeed * handVelocityShare);

            item.OnReleased(true);
            if (rb != null)
            {
                rb.velocity = dir * speed + HeldVelocity * 0.25f;
                // ограничиваем итоговую скорость жёстко (защита от протыкания пола)
                if (rb.velocity.magnitude > maxThrowSpeed) rb.velocity = rb.velocity.normalized * maxThrowSpeed;
                rb.angularVelocity = Random.insideUnitSphere * spinImpulse;
                rb.WakeUp();
            }

            Held = null;
            m_HoldingFlag = false;
            HeldVelocity = Vector3.zero;
            if (m_IgnoreRoutine != null) StopCoroutine(m_IgnoreRoutine);
            m_IgnoreRoutine = StartCoroutine(IgnorePlayerCollisions(postThrowIgnoreTime));

            OnThrown?.Invoke(item, speed);
            return speed;
        }

        /// <summary>Просто выпустить из рук (без броска).</summary>
        public void Drop()
        {
            if (Held == null) return;
            var item = Held;
            var rb = item.Body;
            item.OnReleased(false);
            if (rb != null)
            {
                rb.velocity = HeldVelocity * 0.4f;
                rb.angularVelocity = Random.insideUnitSphere * 0.8f;
            }
            Held = null;
            m_HoldingFlag = false;
            HeldVelocity = Vector3.zero;
            if (m_IgnoreRoutine != null) StopCoroutine(m_IgnoreRoutine);
            m_IgnoreRoutine = StartCoroutine(IgnorePlayerCollisions(postThrowIgnoreTime));
            OnDropped?.Invoke(item);
        }

        void FixedUpdate()
        {
            Vector3 anchorPos = HoldPoint;
            float dt = Time.fixedDeltaTime;

            // скорость руки (нужна для реалистичного броска)
            if (dt > 0f)
            {
                Vector3 instant = (anchorPos - m_PrevHoldPoint) / dt;
                if (instant.sqrMagnitude > 40000f) instant = Vector3.zero; // защита от телепортов
                m_SmoothedAnchorVelocity = Vector3.Lerp(m_SmoothedAnchorVelocity, instant, 0.45f);
                HeldVelocity = m_SmoothedAnchorVelocity;
            }
            m_PrevHoldPoint = anchorPos;

            // предмет могли уничтожить (например, бутылка разбилась об манекена):
            // у Unity уничтоженный объект == null, поэтому просто чистим руку.
            if (Held == null)
            {
                if (m_HoldingFlag)
                {
                    m_HoldingFlag = false;
                    HeldVelocity = Vector3.zero;
                    OnDropped?.Invoke(null);
                }
                return;
            }

            if (Held.Body == null || holdAnchor == null) return;

            var rb = Held.Body;
            Vector3 targetPos = anchorPos + holdAnchor.rotation * m_PosOffset;
            Quaternion targetRot = holdAnchor.rotation * m_RotOffset;

            float t = 1f - Mathf.Exp(-grabLerp * dt);
            Vector3 newPos = Vector3.Lerp(rb.position, targetPos, Mathf.Clamp01(t));
            Quaternion newRot = Quaternion.Slerp(rb.rotation, targetRot, Mathf.Clamp01(t));

            rb.MovePosition(newPos);
            rb.MoveRotation(newRot);
        }

        IEnumerator IgnorePlayerCollisions(float seconds)
        {
            var item = Held;
            Collider[] itemColliders = null;
            if (item != null) itemColliders = item.GetComponentsInChildren<Collider>();

            if (playerCollider != null && itemColliders != null)
            {
                foreach (var c in itemColliders)
                {
                    if (c != null) Physics.IgnoreCollision(playerCollider, c, true);
                }
            }
            yield return new WaitForSeconds(seconds);
            if (playerCollider != null && itemColliders != null)
            {
                foreach (var c in itemColliders)
                {
                    if (c != null) Physics.IgnoreCollision(playerCollider, c, false);
                }
            }
            m_IgnoreRoutine = null;
        }

        /// <summary>Принудительно очистить руку (предмет уничтожен).</summary>
        public void ForceClear()
        {
            Held = null;
            m_HoldingFlag = false;
            HeldVelocity = Vector3.zero;
        }

        /// <summary>Масса того, что в руках (для штрафа к скорости ходьбы).</summary>
        public float HeldMass => Held != null && Held.Body != null ? Held.Body.mass : 0f;

        public string Describe()
        {
            return Held != null ? $"{Held.displayName} ({HeldMass:F1} кг)" : "пусто";
        }
    }
}
