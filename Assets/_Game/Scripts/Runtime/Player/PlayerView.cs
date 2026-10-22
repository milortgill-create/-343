using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// «Живая» камера: повороты, покачивание при ходьбе, просадка при приземлении,
    /// тряска от ударов. В режиме рэгдолла камера жёстко сидит в кости головы и
    /// кувыркается вместе с телом.
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        [Header("Поворот")]
        public float sensitivity = SandboxConfig.LookSensitivity;
        public float maxPitch = SandboxConfig.MaxPitch;
        public float smoothTime = 0.045f;

        [Header("Покачивание при ходьбе")]
        public float bobAmount = SandboxConfig.HeadBobAmount;
        public float bobFrequency = SandboxConfig.HeadBobFrequency;
        public float bobRoll = 1.35f;
        public float idleBreath = 0.006f;

        [Header("Приземление")]
        public float landDip = SandboxConfig.LandDip;
        public float landRecover = 0.30f;

        [Header("Тряска")]
        public float traumaDecay = SandboxConfig.ShakeDecay;
        public float shakeAngle = 4.5f;
        public float shakeOffset = 0.055f;
        public float shakeFrequency = 22f;

        [Header("Ссылки")]
        public Transform yawRoot;        // корень игрока — на нём yaw
        public Transform offsetRoot;     // дочерний — на нём покачивание и тряска
        public Camera cam;

        public float Pitch { get; private set; }
        public float Yaw { get; private set; }
        public float Trauma { get; private set; }
        public bool LookEnabled { get; set; } = true;
        public float BobPhase { get; private set; }

        float m_Dip;
        float m_DipVel;
        Vector2 m_TargetAngles;
        Vector2 m_Angles;
        float m_ShakeSeed;

        void Awake()
        {
            m_ShakeSeed = Random.value * 100f;
            Yaw = yawRoot != null ? yawRoot.eulerAngles.y : 0f;
            m_TargetAngles = new Vector2(0f, Yaw);
            m_Angles = m_TargetAngles;
        }

        public void AddTrauma(float amount)
        {
            Trauma = Mathf.Clamp01(Trauma + amount);
        }

        /// <summary>Хук для PlayerHealth (урон от падения).</summary>
        public System.Action<float> OnLandedHook;

        public void OnLand(float fallSpeed)
        {
            OnLandedHook?.Invoke(fallSpeed);
            float k = Mathf.Clamp01((fallSpeed - 3f) / 14f);
            m_Dip = -landDip * k;
            m_DipVel = -0.6f * k;
            AddTrauma(0.18f + k * 0.45f);
        }

        public void OnHit(Vector3 worldDirection)
        {
            AddTrauma(0.55f);
        }

        /// <summary>Поворот по свайпу/мыши.</summary>
        public void ApplyLook(Vector2 pixelDelta)
        {
            if (!LookEnabled || pixelDelta.sqrMagnitude < 0.0001f) return;
            float dpiScale = Screen.dpi > 10f ? Mathf.Clamp(320f / Screen.dpi, 0.55f, 1.9f) : 1f;
            m_TargetAngles.y += pixelDelta.x * sensitivity * dpiScale;
            m_TargetAngles.x -= pixelDelta.y * sensitivity * dpiScale;
            m_TargetAngles.x = Mathf.Clamp(m_TargetAngles.x, -maxPitch, maxPitch);
        }

        public void SetAngles(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
            m_TargetAngles = new Vector2(Pitch, Yaw);
            m_Angles = m_TargetAngles;
            ApplyRootRotation();
        }

        void ApplyRootRotation()
        {
            if (yawRoot != null) yawRoot.rotation = Quaternion.Euler(0f, m_Angles.y, 0f);
            transform.localRotation = Quaternion.Euler(m_Angles.x, 0f, 0f);
            Yaw = m_Angles.y;
            Pitch = m_Angles.x;
        }

        /// <summary>Вызывается каждый кадр после движения: покачивание, тряска, просадка.</summary>
        public void LateTick(float speed01, float dt)
        {
            // плавный поворот (иначе свайп выглядит дёрганым)
            m_Angles = Vector2.SmoothDamp(m_Angles, m_TargetAngles, ref m_AngleVel, smoothTime, 3600f, dt);
            ApplyRootRotation();

            // --- покачивание при ходьбе ---
            BobPhase += dt * bobFrequency * Mathf.Lerp(0.35f, 1f, speed01) * Mathf.Max(0.0001f, speed01);
            float bobY = Mathf.Sin(BobPhase * 2f) * bobAmount * speed01;
            float bobX = Mathf.Cos(BobPhase) * bobAmount * 0.55f * speed01;
            float bobRollAngle = Mathf.Cos(BobPhase) * bobRoll * speed01;

            // --- дыхание в покое ---
            float breath = Mathf.Sin(Time.time * 1.6f) * idleBreath * (1f - speed01);

            // --- просадка после приземления ---
            m_Dip = Mathf.SmoothDamp(m_Dip, 0f, ref m_DipVel, landRecover, 4f, dt);

            // --- тряска ---
            Trauma = Mathf.Max(0f, Trauma - traumaDecay * dt);
            float shake = Trauma * Trauma;
            float t = Time.unscaledTime * shakeFrequency + m_ShakeSeed;
            float nx = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f;
            float nz = (Mathf.PerlinNoise(t * 0.5f, t * 0.25f) - 0.5f) * 2f;

            Vector3 pos = new Vector3(bobX + nx * shakeOffset * shake,
                bobY + breath + m_Dip + ny * shakeOffset * shake,
                nz * shakeOffset * 0.5f * shake);
            Vector3 rot = new Vector3(ny * shakeAngle * shake,
                nx * shakeAngle * shake,
                bobRollAngle + nz * shakeAngle * 1.6f * shake);

            if (offsetRoot != null)
            {
                offsetRoot.localPosition = pos;
                offsetRoot.localRotation = Quaternion.Euler(rot);
            }

            // отдача FOV при беге — ощущение скорости
            if (cam != null)
            {
                float targetFov = SandboxConfig.CameraFov + speed01 * 5.5f + shake * 3f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-8f * dt));
            }
        }

        Vector2 m_AngleVel;

        /// <summary>Камера переходит на кость головы рэгдолла.</summary>
        public void EnterRagdollMode(Transform headBone)
        {
            LookEnabled = false;
            Trauma = 1f;
            m_TargetAngles = Vector2.zero;
            m_Angles = Vector2.zero;
            transform.localRotation = Quaternion.identity;
            if (offsetRoot != null)
            {
                offsetRoot.localPosition = Vector3.zero;
                offsetRoot.localRotation = Quaternion.identity;
            }
            if (headBone != null)
            {
                transform.SetParent(headBone, false);
                transform.localPosition = new Vector3(0f, 0.055f, 0.075f);
                transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Камера возвращается на обычное крепление.</summary>
        public void ExitRagdollMode(Transform originalParent, Vector3 originalLocalPos)
        {
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPos;
            transform.localRotation = Quaternion.Euler(m_Angles.x, 0f, 0f);
            LookEnabled = true;
        }
    }
}
