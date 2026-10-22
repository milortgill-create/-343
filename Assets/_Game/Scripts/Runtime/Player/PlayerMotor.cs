using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Движение игрока на CharacterController: ходьба/бег, гравитация, прыжок,
    /// шаги и определение приземления (для «живой» камеры).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        public float walkSpeed = SandboxConfig.WalkSpeed;
        public float runSpeed = SandboxConfig.RunSpeed;
        public float acceleration = SandboxConfig.MoveAcceleration;
        public float airControl = SandboxConfig.AirControl;
        public float jumpSpeed = SandboxConfig.JumpSpeed;
        public float gravity = SandboxConfig.Gravity;
        public float maxFallSpeed = -42f;
        public float stepDistance = 1.85f;
        [Tooltip("Штраф к скорости за каждый килограмм в руках")]
        public float carryPenaltyPerKg = 0.010f;
        public float maxCarryPenalty = 0.45f;

        public bool ControlEnabled { get; set; } = true;
        public bool Grounded { get; private set; }
        public float PlanarSpeed { get; private set; }
        public float VerticalSpeed { get; private set; }
        public float Speed01 { get; private set; }
        public float LastFallSpeed { get; private set; }

        public System.Action<float> OnLanded;
        public System.Action<float> OnFootstep;

        CharacterController m_Cc;
        Vector3 m_Planar;
        float m_Vertical = -2f;
        float m_WalkAccum;
        bool m_WasGrounded;

        public CharacterController Controller
        {
            get
            {
                if (m_Cc == null) m_Cc = GetComponent<CharacterController>();
                return m_Cc;
            }
        }

        void Awake()
        {
            m_Cc = Controller;
            m_Cc.height = SandboxConfig.PlayerHeight;
            m_Cc.radius = SandboxConfig.PlayerRadius;
            m_Cc.center = new Vector3(0f, SandboxConfig.PlayerHeight * 0.5f, 0f);
            m_Cc.slopeLimit = 55f;
            m_Cc.stepOffset = 0.34f;
            m_Cc.skinWidth = 0.02f;
            m_Cc.minMoveDistance = 0f;
            m_Cc.detectCollisions = true;
        }

        public void Tick(Vector2 move, bool sprint, bool jump, float carryMass)
        {
            float dt = Time.deltaTime;
            var cc = Controller;
            if (cc == null) return;

            if (!ControlEnabled)
            {
                cc.enabled = false;
                return;
            }
            if (!cc.enabled) cc.enabled = true;

            // направление относительно взгляда (yaw корня)
            Vector3 forward = transform.forward;
            forward.y = 0f;
            Vector3 right = transform.right;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            forward.Normalize();
            right.Normalize();

            Vector3 wish = (forward * move.y + right * move.x);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float penalty = Mathf.Min(maxCarryPenalty, carryMass * carryPenaltyPerKg);
            float targetSpeed = (sprint ? runSpeed : walkSpeed) * (1f - penalty);
            Vector3 targetPlanar = wish * targetSpeed;

            float accel = acceleration * (Grounded ? 1f : airControl);
            m_Planar = Vector3.MoveTowards(m_Planar, targetPlanar, accel * dt);

            // гравитация + «прилипание» к полу
            if (Grounded && m_Vertical < 0f) m_Vertical = -2.5f;
            m_Vertical += gravity * dt;
            if (m_Vertical < maxFallSpeed) m_Vertical = maxFallSpeed;

            if (jump && Grounded)
            {
                m_Vertical = jumpSpeed;
                Grounded = false;
            }

            Vector3 displacement = (m_Planar + Vector3.up * m_Vertical) * dt;
            m_WasGrounded = Grounded;
            cc.Move(displacement);
            Grounded = cc.isGrounded;

            PlanarSpeed = new Vector2(m_Planar.x, m_Planar.z).magnitude;
            VerticalSpeed = m_Vertical;
            Speed01 = Mathf.Clamp01(PlanarSpeed / Mathf.Max(0.1f, runSpeed));

            // приземление
            if (Grounded && !m_WasGrounded)
            {
                LastFallSpeed = m_Vertical;
                if (m_Vertical < -4.5f) OnLanded?.Invoke(-m_Vertical);
                m_Vertical = -2.5f;
                m_WalkAccum = 0f;
            }

            // шаги
            if (Grounded)
            {
                m_WalkAccum += PlanarSpeed * dt;
                if (m_WalkAccum >= stepDistance)
                {
                    m_WalkAccum = 0f;
                    OnFootstep?.Invoke(Mathf.Clamp01(PlanarSpeed / runSpeed));
                }
            }
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            var cc = Controller;
            if (cc != null) cc.enabled = false;
            transform.position = position;
            transform.rotation = rotation;
            m_Planar = Vector3.zero;
            m_Vertical = -2f;
            if (cc != null)
            {
                cc.enabled = ControlEnabled;
                Physics.SyncTransforms();
            }
        }

        public void StopAll()
        {
            m_Planar = Vector3.zero;
            m_Vertical = -2f;
        }

        /// <summary>Скорость точки на уровне головы — для расчёта броска.</summary>
        public Vector3 HeadVelocity(Vector3 eyeOffset)
        {
            return m_Planar + Vector3.up * m_Vertical;
        }
    }
}
