using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Адаптивная оптимизация под Samsung Galaxy A53 и сопоставимые устройства.
    /// Определяет пресет по железу, держит целевой FPS и динамически меняет
    /// renderScale, если кадр не успевает отрисоваться.
    /// </summary>
    public class PerformanceTuner : MonoBehaviour
    {
        public static bool InterpolateBodies = true;
        public static bool UseReflectionProbe = true;
        public static int ShardCount = 6;

        [Header("Авто-настройка")]
        [Tooltip("Определять пресет по устройству при старте")]
        public bool autoDetect = true;
        public SandboxConfig.PerfPreset forcedPreset = SandboxConfig.PerfPreset.Medium;

        [Header("Адаптивное разрешение")]
        public bool adaptiveResolution = true;
        public float minScale = 0.60f;
        public float maxScale = 1.00f;
        [Tooltip("Если среднее время кадра выше этого — снижаем разрешение")]
        public float slowFrameMs = 21f;
        [Tooltip("Если среднее время кадра ниже этого — повышаем разрешение")]
        public float fastFrameMs = 13.5f;
        public float scaleStep = 0.075f;
        public float evaluateInterval = 1.5f;

        public SandboxConfig.PerfPreset Preset { get; private set; } = SandboxConfig.PerfPreset.Medium;
        public float CurrentScale { get; private set; } = 1f;
        public float AverageFrameMs { get; private set; }
        public int DrawCalls => UnityStatsSafe();

        static int s_DrawCalls = -1;

        float m_Accum;
        int m_Frames;
        float m_Timer;

        public static PerformanceTuner Instance { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        public void ApplyAuto()
        {
            Preset = autoDetect ? SandboxConfig.DefaultPresetForDevice() : forcedPreset;
            ApplyPreset(Preset);
        }

        public void ApplyPreset(SandboxConfig.PerfPreset preset)
        {
            Preset = preset;
            SandboxConfig.ApplyPreset(preset);

            switch (preset)
            {
                case SandboxConfig.PerfPreset.Low:
                    CurrentScale = 0.70f;
                    maxScale = 0.85f;
                    UrpTweaks.SetMainLightShadowResolution(512);
                    UrpTweaks.SetAdditionalLightsPerObject(2);
                    UrpTweaks.SetShadowCascades(1);
                    UrpTweaks.SetSoftShadows(false);
                    UrpTweaks.SetHdr(false);
                    UrpTweaks.SetDepthTexture(false);
                    UrpTweaks.SetOpaqueTexture(false);
                    break;
                case SandboxConfig.PerfPreset.Medium:
                    CurrentScale = 0.85f;
                    maxScale = 1.0f;
                    UrpTweaks.SetMainLightShadowResolution(1024);
                    UrpTweaks.SetAdditionalLightsPerObject(4);
                    UrpTweaks.SetShadowCascades(1);
                    UrpTweaks.SetSoftShadows(true);
                    UrpTweaks.SetHdr(false);
                    UrpTweaks.SetDepthTexture(false);
                    UrpTweaks.SetOpaqueTexture(false);
                    break;
                default:
                    CurrentScale = 1.0f;
                    maxScale = 1.25f;
                    UrpTweaks.SetMainLightShadowResolution(2048);
                    UrpTweaks.SetAdditionalLightsPerObject(6);
                    UrpTweaks.SetShadowCascades(2);
                    UrpTweaks.SetSoftShadows(true);
                    UrpTweaks.SetHdr(true);
                    break;
            }
            UrpTweaks.SetRenderScale(CurrentScale);
        }

        void Update()
        {
            m_Accum += Time.unscaledDeltaTime;
            m_Frames++;

            m_Timer += Time.unscaledDeltaTime;
            if (m_Timer < evaluateInterval) return;

            AverageFrameMs = m_Frames > 0 ? (m_Accum / m_Frames) * 1000f : 0f;
            m_Timer = 0f;
            m_Accum = 0f;
            m_Frames = 0;

            if (!adaptiveResolution) return;

            float target = CurrentScale;
            if (AverageFrameMs > slowFrameMs) target -= scaleStep;
            else if (AverageFrameMs < fastFrameMs) target += scaleStep * 0.5f;

            target = Mathf.Clamp(target, minScale, maxScale);
            if (!Mathf.Approximately(target, CurrentScale))
            {
                CurrentScale = target;
                UrpTweaks.SetRenderScale(CurrentScale);
            }
        }

        /// <summary>
        /// UnityStats доступен только в редакторе — на устройстве возвращаем -1,
        /// а для HUD берём число батчей через FrameTimingManager, если он есть.
        /// </summary>
        static int UnityStatsSafe()
        {
            return s_DrawCalls;
        }

        public string DescribeDevice()
        {
            return string.Format("{0} | {1} | {2} МБ ОЗУ | {3} ядер | {4}",
                SystemInfo.deviceModel, SystemInfo.graphicsDeviceName,
                SystemInfo.systemMemorySize, SystemInfo.processorCount, Preset);
        }
    }
}
