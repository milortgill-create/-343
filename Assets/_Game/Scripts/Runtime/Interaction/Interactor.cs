using System.Collections;
using UnityEngine;

namespace Sandbox
{
    public enum InteractionKind { None, Pickup, Throw, Punch, StrikeWithItem }

    /// <summary>
    /// Главное взаимодействие песочницы: наводишь прицел — видишь подсказку,
    /// жмёшь «Действие» — берёшь / бросаешь / бьёшь.
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        [Header("Ссылки")]
        public Camera cam;
        public GrabController grab;
        public PlayerHands hands;
        public PlayerView view;
        public PlayerBody body;
        public PhoneProp phone;

        [Header("Параметры")]
        public float reach = SandboxConfig.InteractReach;
        public float aimAssistRadius = SandboxConfig.InteractRadius;
        public float punchCooldown = 0.40f;
        public float punchImpactDelay = 0.085f;
        public float punchImpulse = SandboxConfig.PunchImpulse;
        public float heldPunchBonus = SandboxConfig.HeldItemPunchBonus;
        public float punchReachMultiplier = 1.18f;
        public LayerMask mask;

        public RaycastHit Hit { get; private set; }
        public GameObject TargetObject { get; private set; }
        public Grabbable TargetGrabbable { get; private set; }
        public IDamageable TargetDamageable { get; private set; }
        public InteractionKind Kind { get; private set; }
        public string Prompt { get; private set; }
        public string TargetName { get; private set; }
        public bool HasTarget => Kind != InteractionKind.None;

        readonly TargetHighlighter m_Highlight = new TargetHighlighter();
        float m_LastPunch = -10f;
        bool m_LastPunchRight = true;
        Coroutine m_PunchRoutine;

        public System.Action<InteractionKind, string> OnPromptChanged;
        public System.Action<float> OnThrown;
        public System.Action<float, bool> OnPunch;   // (damage, withItem)

        void Awake()
        {
            if (mask.value == 0) mask = Layers.InteractableMask;
        }

        public void Tick(InputService input)
        {
            Scan();
            m_Highlight.Tick(Time.unscaledDeltaTime);

            if (input == null) return;

            if (input.DropPressed && grab != null && grab.HasItem)
            {
                grab.Drop();
                AudioPool.PlayUi(ProceduralAudio.UiClick, 0.35f);
            }

            if (input.PhonePressed) PhoneAction();

            if (input.ActionPressed) DoAction();
        }

        // ------------------------------------------------------------------
        // Прицеливание
        // ------------------------------------------------------------------
        public void Scan()
        {
            TargetObject = null;
            TargetGrabbable = null;
            TargetDamageable = null;
            Kind = InteractionKind.None;
            Prompt = string.Empty;
            TargetName = string.Empty;

            if (cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore);
            if (!hasHit)
            {
                // «прощающий» прицел для пальца: SphereCast малым радиусом
                hasHit = Physics.SphereCast(ray, aimAssistRadius, out hit, reach, mask, QueryTriggerInteraction.Ignore);
            }

            if (!hasHit)
            {
                UpdatePrompt();
                return;
            }

            Hit = hit;
            TargetObject = hit.collider != null ? hit.collider.gameObject : null;
            if (TargetObject == null) { UpdatePrompt(); return; }

            TargetGrabbable = hit.collider.GetComponentInParent<Grabbable>();
            TargetDamageable = hit.collider.GetComponentInParent<IDamageable>();

            bool holding = grab != null && grab.HasItem;
            if (holding)
            {
                if (TargetDamageable != null)
                {
                    Kind = InteractionKind.StrikeWithItem;
                    TargetName = DummyName(TargetDamageable);
                    Prompt = "Ударить предметом: " + TargetName;
                }
                else
                {
                    Kind = InteractionKind.Throw;
                    TargetName = grab.Held.displayName;
                    Prompt = "Бросить: " + TargetName;
                }
            }
            else
            {
                if (TargetGrabbable != null)
                {
                    Kind = InteractionKind.Pickup;
                    TargetName = TargetGrabbable.displayName;
                    Prompt = "Взять: " + TargetName;
                }
                else if (TargetDamageable != null)
                {
                    Kind = InteractionKind.Punch;
                    TargetName = DummyName(TargetDamageable);
                    Prompt = "Ударить: " + TargetName;
                }
            }

            UpdatePrompt();
        }

        static string DummyName(IDamageable d)
        {
            var dummy = d as DamageableDummy;
            return dummy != null ? dummy.displayName : "Манекен";
        }

        GameObject m_LastHighlighted;

        void UpdatePrompt()
        {
            GameObject toHighlight = null;
            if (TargetGrabbable != null && Kind == InteractionKind.Pickup) toHighlight = TargetGrabbable.gameObject;
            else if (TargetDamageable != null && (Kind == InteractionKind.Punch || Kind == InteractionKind.StrikeWithItem))
                toHighlight = (TargetDamageable as Component) != null ? ((Component)TargetDamageable).gameObject : null;

            if (toHighlight != m_LastHighlighted)
            {
                m_LastHighlighted = toHighlight;
                m_Highlight.SetTarget(toHighlight);
                m_Highlight.SetColor(Kind == InteractionKind.StrikeWithItem || Kind == InteractionKind.Punch
                    ? new Color(1f, 0.35f, 0.25f)
                    : new Color(0.25f, 0.85f, 1f));
            }

            OnPromptChanged?.Invoke(Kind, Prompt);
        }

        // ------------------------------------------------------------------
        // Действие
        // ------------------------------------------------------------------
        public void DoAction()
        {
            if (body != null && body.IsRagdolled)
            {
                body.GetUp(false);
                return;
            }

            switch (Kind)
            {
                case InteractionKind.Pickup:
                    if (grab != null && TargetGrabbable != null)
                    {
                        if (grab.TryGrab(TargetGrabbable, Hit.point))
                        {
                            AudioPool.PlayUi(ProceduralAudio.Pickup, 0.45f);
                            if (hands != null) hands.Punch(false);
                            if (view != null) view.AddTrauma(0.05f);
                        }
                    }
                    break;

                case InteractionKind.Throw:
                    if (grab != null)
                    {
                        float speed = grab.Throw(cam.transform.forward);
                        AudioPool.PlayClip(ProceduralAudio.Whoosh, cam.transform.position + cam.transform.forward * 0.5f, 0.55f,
                            0.85f + speed / 26f);
                        if (hands != null) hands.Punch(true);
                        if (view != null) view.AddTrauma(0.10f);
                        OnThrown?.Invoke(speed);
                        Scan();
                    }
                    break;

                case InteractionKind.Punch:
                    StartPunch(false);
                    break;

                case InteractionKind.StrikeWithItem:
                    StartPunch(true);
                    break;

                default:
                    // холостой взмах — тоже с анимацией, чтобы управление читалось
                    StartPunch(false, true);
                    break;
            }
        }

        void StartPunch(bool withItem, bool whiff = false)
        {
            if (Time.unscaledTime - m_LastPunch < punchCooldown) return;
            m_LastPunch = Time.unscaledTime;
            m_LastPunchRight = !m_LastPunchRight;

            if (hands != null) hands.Punch(m_LastPunchRight);
            AudioPool.PlayClip(ProceduralAudio.Whoosh, cam.transform.position + cam.transform.forward * 0.4f, 0.4f, 1.05f);

            if (m_PunchRoutine != null) StopCoroutine(m_PunchRoutine);
            m_PunchRoutine = StartCoroutine(PunchImpact(withItem, whiff));
        }

        IEnumerator PunchImpact(bool withItem, bool whiff)
        {
            yield return new WaitForSecondsRealtime(punchImpactDelay);
            m_PunchRoutine = null;

            if (cam == null) return;

            // в момент «контакта» перепроверяем, что цель всё ещё под прицелом
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            float punchReach = reach * punchReachMultiplier;
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, punchReach, mask, QueryTriggerInteraction.Ignore);
            if (!hasHit)
                hasHit = Physics.SphereCast(ray, aimAssistRadius * 1.4f, out hit, punchReach, mask, QueryTriggerInteraction.Ignore);

            if (!hasHit) return;

            var damageable = hit.collider != null ? hit.collider.GetComponentInParent<IDamageable>() : null;
            if (damageable == null) return;

            Grabbable held = grab != null ? grab.Held : null;
            float massFactor = withItem && held != null && held.Body != null
                ? Mathf.Clamp(held.Body.mass * 0.42f, 0.7f, 3.4f)
                : 1f;

            float impulseMagnitude = punchImpulse * (withItem ? heldPunchBonus * massFactor : 1f);
            Vector3 dir = cam.transform.forward + Vector3.up * 0.18f + cam.transform.right * (m_LastPunchRight ? 0.10f : -0.10f);
            Vector3 impulse = dir.normalized * impulseMagnitude;

            var info = DamageInfo.Punch(hit.point, impulse, gameObject, withItem);
            info.amount = 9f + impulseMagnitude * 0.055f;
            damageable.ApplyDamage(info);

            AudioPool.PlayImpact(withItem && held != null ? held.impactSound : ImpactSound.Soft, hit.point,
                Mathf.Clamp01(impulseMagnitude / 520f));
            FxPool.SpawnDust(hit.point, -cam.transform.forward, Mathf.Clamp01(impulseMagnitude / 620f));
            if (view != null) view.AddTrauma(withItem ? 0.30f : 0.18f);

            OnPunch?.Invoke(info.amount, withItem);

            // бутылкой по манекену — стекло разлетается
            if (withItem && held != null)
            {
                var brk = held.GetComponent<Breakable>();
                if (brk != null)
                {
                    brk.Break(hit.point, impulse);
                    if (grab != null && grab.Held == null) { /* предмет уничтожен — руки пусты */ }
                }
            }
        }

        // ------------------------------------------------------------------
        // Телефон
        // ------------------------------------------------------------------
        public void PhoneAction()
        {
            if (phone == null) return;
            var phoneGrab = phone.Grabbable;
            if (phoneGrab == null) return;

            if (grab != null && grab.Held == phoneGrab) return; // уже в руке

            if (grab != null && grab.HasItem) grab.Drop();

            float dist = cam != null
                ? Vector3.Distance(cam.transform.position, phoneGrab.transform.position)
                : 0f;

            if (dist <= reach * 2.2f)
            {
                grab.TryGrab(phoneGrab, phoneGrab.transform.position);
            }
            else
            {
                // песочница: телефон «достаём из кармана», если он улетел далеко
                Vector3 spawn = cam != null
                    ? cam.transform.position + cam.transform.forward * 0.4f + Vector3.down * 0.25f
                    : transform.position + Vector3.up;
                if (phoneGrab.Body != null) phoneGrab.Body.position = spawn;
                else phoneGrab.transform.position = spawn;
                phoneGrab.Body.velocity = Vector3.zero;
                phoneGrab.Body.angularVelocity = Vector3.zero;
                Debug.Log($"[Sandbox] Смартфон был в {dist:F1} м — возвращён в руку.");
                grab.TryGrab(phoneGrab, phoneGrab.transform.position);
            }
            AudioPool.PlayUi(ProceduralAudio.Pickup, 0.5f);
            Scan();
        }

        /// <summary>Ориентировать телефон в руке так, чтобы экран был виден игроку.</summary>
        public void PlacePhoneInHand()
        {
            if (phone == null || grab == null) return;
            var anchor = grab.holdAnchor;
            if (anchor == null) return;

            phone.transform.position = anchor.position + anchor.forward * 0.02f + anchor.up * -0.01f;
            phone.transform.rotation = anchor.rotation * Quaternion.Euler(-72f, 0f, 0f);
            Physics.SyncTransforms();
            grab.TryGrab(phone.Grabbable, phone.transform.position);
        }
    }
}
