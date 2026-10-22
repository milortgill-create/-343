using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sandbox
{
    /// <summary>
    /// Ввод для телефона: левая половина экрана — плавающий джойстик движения,
    /// правая половина — свайпы камеры, кнопки UI — действия.
    /// На ПК работает мышь и WASD (удобно проверять в редакторе).
    ///
    /// Специально НЕ использует Update(): опрашивается из PlayerController.Tick(),
    /// поэтому порядок обработки ввода и реакции на него детерминирован.
    /// </summary>
    public class InputService : MonoBehaviour
    {
        public static InputService Instance { get; private set; }

        [Header("Настройки")]
        [Range(0.2f, 1f)] public float joystickZoneWidth = 0.45f;
        public float lookSensitivity = SandboxConfig.LookSensitivity;
        public bool invertY = false;
        public float deadZone = 0.12f;
        public bool lockMouseOnDesktop = true;

        [Header("Ссылки")]
        public VirtualJoystick joystick;
        public List<RectTransform> uiBlockers = new List<RectTransform>();

        public Vector2 MoveAxis { get; private set; }
        public Vector2 LookDelta { get; private set; }
        public bool ActionPressed { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool DropPressed { get; private set; }
        public bool PhonePressed { get; private set; }
        public bool ResetPressed { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool StressTestPressed { get; private set; }
        public bool IsRagdolled { get; set; }

        int m_LookFingerId = int.MinValue;
        int m_JoystickFingerId = int.MinValue;
        Vector3 m_LastMousePos;
        bool m_MouseTracked;
        bool m_ActionQueued;
        bool m_JumpQueued;
        bool m_DropQueued;
        bool m_PhoneQueued;
        bool m_ResetQueued;
        bool m_StressQueued;
        bool m_SprintQueued;

        void Awake()
        {
            Instance = this;
            Input.multiTouchEnabled = true;
            Input.simulateMouseWithTouches = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------- вызывается кнопками HUD ----------------
        public void QueueAction() { m_ActionQueued = true; }
        public void QueueJump() { m_JumpQueued = true; }
        public void QueueDrop() { m_DropQueued = true; }
        public void QueuePhone() { m_PhoneQueued = true; }
        public void QueueReset() { m_ResetQueued = true; }
        public void QueueStressTest() { m_StressQueued = true; }
        public void SetSprint(bool on) { m_SprintQueued = on; }

        /// <summary>Основной опрос ввода. Вызывается один раз за кадр.</summary>
        public void Tick()
        {
            LookDelta = Vector2.zero;
            bool moveFromJoystick = false;

            if (Input.touchSupported && Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    switch (t.phase)
                    {
                        case TouchPhase.Began:
                            if (IsOverUi(t.position)) break;
                            if (t.position.x < Screen.width * joystickZoneWidth && m_JoystickFingerId == int.MinValue)
                            {
                                m_JoystickFingerId = t.fingerId;
                                if (joystick != null) { joystick.Begin(t.position); moveFromJoystick = true; }
                            }
                            else if (m_LookFingerId == int.MinValue)
                            {
                                m_LookFingerId = t.fingerId;
                            }
                            break;

                        case TouchPhase.Moved:
                        case TouchPhase.Stationary:
                            if (t.fingerId == m_JoystickFingerId && joystick != null)
                            {
                                joystick.Drag(t.position);
                                moveFromJoystick = true;
                            }
                            else if (t.fingerId == m_LookFingerId)
                            {
                                LookDelta += t.deltaPosition;
                            }
                            break;

                        case TouchPhase.Ended:
                        case TouchPhase.Canceled:
                            if (t.fingerId == m_JoystickFingerId)
                            {
                                m_JoystickFingerId = int.MinValue;
                                if (joystick != null) joystick.End();
                            }
                            else if (t.fingerId == m_LookFingerId)
                            {
                                m_LookFingerId = int.MinValue;
                            }
                            break;
                    }
                }
            }

            // ---------------- клавиатура/мышь (ПК и редактор) ----------------
            Vector2 keys = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) keys.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) keys.y -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keys.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keys.x -= 1f;
            if (Input.GetKey(KeyCode.LeftShift)) m_SprintQueued = true;

            if (keys.sqrMagnitude > 0.001f)
            {
                MoveAxis = Vector2.ClampMagnitude(keys, 1f);
            }
            else if (moveFromJoystick && joystick != null)
            {
                MoveAxis = joystick.Value;
            }
            else
            {
                if (joystick != null) joystick.End();
                MoveAxis = Vector2.MoveTowards(MoveAxis, Vector2.zero, Time.unscaledDeltaTime * 8f);
            }

            // мёртвая зона с перенормировкой, чтобы движение стартовало плавно
            float mag = MoveAxis.magnitude;
            if (mag < deadZone || mag < 0.0001f) MoveAxis = Vector2.zero;
            else MoveAxis = MoveAxis.normalized * Mathf.Clamp01((mag - deadZone) / (1f - deadZone));

            bool desktop = !Application.isMobilePlatform;
            if (desktop)
            {
                if (lockMouseOnDesktop && Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                LookDelta += ReadMouseDelta() * 12f;

                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUi && Input.GetMouseButtonDown(0)) m_ActionQueued = true;
                if (Input.GetKeyDown(KeyCode.E)) m_ActionQueued = true;
                if (Input.GetKeyDown(KeyCode.Space)) m_JumpQueued = true;
                if (Input.GetKeyDown(KeyCode.Q)) m_DropQueued = true;
                if (Input.GetKeyDown(KeyCode.F)) m_PhoneQueued = true;
                if (Input.GetKeyDown(KeyCode.R)) m_ResetQueued = true;
                if (Input.GetKeyDown(KeyCode.T)) m_StressQueued = true;
            }
            else
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = false;
                }
            }

            // ---------------- одноразовые флаги ----------------
            if (invertY) LookDelta.y = -LookDelta.y;
            ActionPressed = m_ActionQueued;
            JumpPressed = m_JumpQueued;
            DropPressed = m_DropQueued;
            PhonePressed = m_PhoneQueued;
            ResetPressed = m_ResetQueued;
            StressTestPressed = m_StressQueued;
            SprintHeld = m_SprintQueued;
        }

        /// <summary>Сброс одноразовых флагов в конце кадра.</summary>
        public void EndFrame()
        {
            m_ActionQueued = false;
            m_JumpQueued = false;
            m_DropQueued = false;
            m_PhoneQueued = false;
            m_ResetQueued = false;
            m_StressQueued = false;
            if (!Input.GetKey(KeyCode.LeftShift)) m_SprintQueued = false;
            ActionPressed = false;
            JumpPressed = false;
            DropPressed = false;
            PhonePressed = false;
            ResetPressed = false;
            StressTestPressed = false;
        }

        static bool s_MouseAxisOk = true;

        /// <summary>
        /// Дельта мыши. Если в проекте нет осей Mouse X/Y (InputManager.asset),
        /// считаем по screenPosition — игра не падает в любом случае.
        /// </summary>
        Vector2 ReadMouseDelta()
        {
            if (s_MouseAxisOk)
            {
                try
                {
                    return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                }
                catch (System.Exception)
                {
                    s_MouseAxisOk = false;
                }
            }

            Vector3 p = Input.mousePosition;
            if (!m_MouseTracked)
            {
                m_MouseTracked = true;
                m_LastMousePos = p;
                return Vector2.zero;
            }
            Vector2 delta = p - m_LastMousePos;
            m_LastMousePos = p;
            return delta;
        }

        bool IsOverUi(Vector2 screenPos)
        {
            for (int i = 0; i < uiBlockers.Count; i++)
            {
                var rt = uiBlockers[i];
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null)) return true;
            }
            var es = EventSystem.current;
            if (es != null)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (es.IsPointerOverGameObject(t.fingerId)) return true;
                }
            }
            return false;
        }
    }
}
