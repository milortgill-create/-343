using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Пул AudioSources для 3D-звуков. Никаких внешних файлов — клипы
    /// генерируются ProceduralAudio.
    /// </summary>
    public class AudioPool : MonoBehaviour
    {
        public static AudioPool Instance { get; private set; }

        [Range(1, 16)] public int voiceCount = 10;
        [Range(0f, 1f)] public float masterVolume = 0.85f;
        public float minDistance = 1.2f;
        public float maxDistance = 26f;

        AudioSource[] m_Voices;
        int m_Next;
        float m_LastImpactTime;

        void Awake()
        {
            Instance = this;
            ProceduralAudio.Init();
            m_Voices = new AudioSource[Mathf.Clamp(voiceCount, 1, 16)];
            for (int i = 0; i < m_Voices.Length; i++)
            {
                var go = new GameObject("Voice_" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = minDistance;
                src.maxDistance = maxDistance;
                src.dopplerLevel = 0f;
                src.priority = 96;
                m_Voices[i] = src;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource NextVoice()
        {
            if (m_Voices == null || m_Voices.Length == 0) return null;
            for (int i = 0; i < m_Voices.Length; i++)
            {
                m_Next = (m_Next + 1) % m_Voices.Length;
                if (!m_Voices[m_Next].isPlaying) return m_Voices[m_Next];
            }
            return m_Voices[m_Next];
        }

        public static void PlayImpact(ImpactSound kind, Vector3 worldPos, float intensity)
        {
            if (Instance == null) return;
            AudioClip clip = ProceduralAudio.ForImpact(kind);
            if (clip == null) return;

            // не даём одному и тому же звуку строчить десятки раз за кадр
            if (Time.unscaledTime - Instance.m_LastImpactTime < 0.012f) return;
            Instance.m_LastImpactTime = Time.unscaledTime;

            Instance.Play3D(clip, worldPos,
                Mathf.Clamp(0.25f + intensity * 0.85f, 0f, 1f) * Instance.masterVolume,
                1f + (1f - Mathf.Clamp01(intensity)) * 0.18f + Random.Range(-0.05f, 0.05f));
        }

        public static void PlayClip(AudioClip clip, Vector3 worldPos, float volume = 0.8f, float pitch = 1f)
        {
            if (Instance == null || clip == null) return;
            Instance.Play3D(clip, worldPos, volume * Instance.masterVolume, pitch);
        }

        public static void PlayUi(AudioClip clip, float volume = 0.6f)
        {
            if (Instance == null || clip == null) return;
            var src = Instance.NextVoice();
            if (src == null) return;
            src.transform.position = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            src.spatialBlend = 0f;
            src.pitch = 1f;
            src.PlayOneShot(clip, volume * Instance.masterVolume);
        }

        void Play3D(AudioClip clip, Vector3 worldPos, float volume, float pitch)
        {
            var src = NextVoice();
            if (src == null) return;
            src.transform.position = worldPos;
            src.spatialBlend = 1f;
            src.pitch = Mathf.Clamp(pitch, 0.4f, 2.2f);
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}
