using System.Text;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Оркестратор игрока. Все подсистемы опрашиваются из одного места в строгом
    /// порядке (ввод → движение → камера → взаимодействие → руки → HUD),
    /// поэтому не зависит от Script Execution Order и ведёт себя одинаково
    /// в редакторе и на устройстве.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public InputService input;
        public PlayerMotor motor;
        public PlayerView view;
        public PlayerHands hands;
        public PlayerBody body;
        public PlayerHealth health;
        public Interactor interactor;
        public GrabController grab;
        public HudController hud;
        public PhysicsStressTester stressTester;

        [Tooltip("Обновлять панель статистики N раз в секунду")]
        public float hudRefreshRate = 6f;

        float m_HudTimer;
        readonly StringBuilder m_Sb = new StringBuilder(256);
        InteractionKind m_LastKind;
        bool m_LastSprint;

        void Update()
        {
            float dt = Time.deltaTime;
            if (input != null) input.Tick();

            if (input != null && input.ResetPressed)
            {
                if (health != null) health.Respawn();
                if (stressTester != null) stressTester.ResetAll();
                if (hud != null) hud.ShowMessage("Полигон сброшен");
            }

            if (input != null && input.StressTestPressed && stressTester != null)
            {
                stressTester.Toggle();
                if (hud != null) hud.ShowMessage(stressTester.IsRunning ? "Стресс-тест физики: СТАРТ" : "Стресс-тест остановлен");
            }

            if (body != null && body.IsRagdolled)
            {
                if (motor != null) motor.StopAll();
                if (input != null && input.ActionPressed) body.GetUp(false);
                if (input != null && input.DropPressed && grab != null) grab.ForceClear();
            }
            else
            {
                if (motor != null)
                    motor.Tick(input != null ? input.MoveAxis : Vector2.zero,
                        input != null && input.SprintHeld,
                        input != null && input.JumpPressed,
                        grab != null ? grab.HeldMass : 0f);

                if (view != null && input != null) view.ApplyLook(input.LookDelta);
                if (interactor != null) interactor.Tick(input);
            }

            if (hands != null)
            {
                hands.Tick(motor != null ? motor.Speed01 : 0f,
                    input != null ? input.LookDelta : Vector2.zero,
                    dt,
                    grab != null && grab.HasItem,
                    view != null ? view.BobPhase : 0f);
            }

            UpdateHud(dt);
            if (input != null) input.EndFrame();
        }

        void LateUpdate()
        {
            if (view != null) view.LateTick(motor != null ? motor.Speed01 : 0f, Time.deltaTime);
        }

        void UpdateHud(float dt)
        {
            if (hud == null) return;

            if (interactor != null)
            {
                if (interactor.Kind != m_LastKind)
                {
                    m_LastKind = interactor.Kind;
                    hud.SetActionLabel(ActionLabel(interactor.Kind));
                }
                hud.SetCrosshair(interactor.HasTarget, interactor.Kind);
            }

            if (health != null)
            {
                hud.SetHealth(health.Health / Mathf.Max(1f, health.maxHealth), Mathf.CeilToInt(health.Health).ToString());
            }

            m_HudTimer += dt;
            if (m_HudTimer < 1f / Mathf.Max(1f, hudRefreshRate)) return;
            m_HudTimer = 0f;

            m_Sb.Length = 0;
            if (FpsMeter.Instance != null)
            {
                m_Sb.Append(FpsMeter.Instance.Text());
                m_Sb.Append("  |  scale ");
                m_Sb.Append((PerformanceTuner.Instance != null ? PerformanceTuner.Instance.CurrentScale : 1f).ToString("F2"));
                m_Sb.Append('\n');
            }
            if (PerformanceTuner.Instance != null)
            {
                m_Sb.Append(PerformanceTuner.Instance.DescribeDevice());
                m_Sb.Append('\n');
            }
            m_Sb.Append("В руках: ");
            m_Sb.Append(grab != null ? grab.Describe() : "пусто");
            m_Sb.Append("  |  ");
            m_Sb.Append(interactor != null && !string.IsNullOrEmpty(interactor.Prompt) ? interactor.Prompt : "наведи прицел на предмет");
            m_Sb.Append('\n');

            if (health != null)
            {
                m_Sb.Append(health.Describe());
                if (body != null && body.IsRagdolled)
                    m_Sb.Append($"  {body.RagdollTime:F1} с — «Действие» чтобы встать");
                m_Sb.Append('\n');
            }

            if (stressTester != null && stressTester.Dummies != null)
            {
                foreach (var d in stressTester.Dummies)
                {
                    if (d == null) continue;
                    m_Sb.Append(d.Describe());
                    m_Sb.Append('\n');
                }
            }

            if (OutOfBoundsRecovery.Instance != null)
            {
                m_Sb.Append("Тел в симуляции: ");
                m_Sb.Append(OutOfBoundsRecovery.Instance.RegisteredCount.ToString());
                m_Sb.Append("  |  провалилось сквозь пол: ");
                m_Sb.Append(OutOfBoundsRecovery.Instance.Recoveries.ToString());
                m_Sb.Append(Breakable.LiveShardCount > 0 ? "  |  осколков: " + Breakable.LiveShardCount : string.Empty);
                if (stressTester != null && stressTester.IsRunning)
                {
                    m_Sb.Append("\nСТРЕСС-ТЕСТ: ");
                    m_Sb.Append(stressTester.StatusText);
                }
            }

            hud.SetPrompt(interactor != null ? interactor.Prompt : string.Empty,
                interactor != null ? interactor.Kind : InteractionKind.None);
            hud.SetStats(m_Sb.ToString());

            bool sprint = input != null && input.SprintHeld;
            if (sprint != m_LastSprint)
            {
                m_LastSprint = sprint;
                hud.SetSprintVisual(sprint);
            }
        }

        string ActionLabel(InteractionKind kind)
        {
            if (body != null && body.IsRagdolled) return "ВСТАТЬ";
            switch (kind)
            {
                case InteractionKind.Pickup: return "ВЗЯТЬ";
                case InteractionKind.Throw: return "БРОСИТЬ";
                case InteractionKind.Punch: return "УДАР";
                case InteractionKind.StrikeWithItem: return "УДАР ПРЕДМЕТОМ";
                default: return "ДЕЙСТВИЕ";
            }
        }
    }
}
