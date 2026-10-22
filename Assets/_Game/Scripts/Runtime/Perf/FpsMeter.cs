using UnityEngine;

namespace Sandbox
{
    /// <summary>Счётчик FPS / времени кадра для HUD и адаптивной настройки.</summary>
    public class FpsMeter : MonoBehaviour
    {
        public static FpsMeter Instance { get; private set; }

        public float Fps { get; private set; }
        public float FrameMs { get; private set; }
        public float WorstFrameMs { get; private set; }
        public float UpdateInterval = 0.5f;

        float m_Accum;
        int m_Frames;
        float m_Timer;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            m_Accum += dt;
            m_Frames++;
            m_Timer += dt;

            FrameMs = dt * 1000f;
            if (FrameMs > WorstFrameMs && Time.frameCount > 30) WorstFrameMs = FrameMs;
            // постепенно «забываем» пик, чтобы HUD не показывал старое значение вечно
            WorstFrameMs = Mathf.Lerp(WorstFrameMs, FrameMs, Time.unscaledDeltaTime * 0.08f);

            if (m_Timer < UpdateInterval) return;
            Fps = m_Frames / Mathf.Max(0.0001f, m_Accum);
            m_Timer = 0f;
            m_Accum = 0f;
            m_Frames = 0;
        }

        public string Text()
        {
            return string.Format("{0:F0} FPS  {1:F1} ms", Fps, FrameMs);
        }
    }
}
