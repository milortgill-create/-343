using System;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Все звуки синтезируются кодом при старте (44.1 кГц, моно).
    /// В проекте нет аудио-файлов — APK остаётся маленьким, а офлайн-режим
    /// гарантирован: ничего не загружается из сети и из ресурсов.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        public static AudioClip Wood;
        public static AudioClip Metal;
        public static AudioClip Glass;
        public static AudioClip Soft;
        public static AudioClip Concrete;
        public static AudioClip Punch;
        public static AudioClip Whoosh;
        public static AudioClip Pickup;
        public static AudioClip Footstep;
        public static AudioClip RagdollHit;
        public static AudioClip UiClick;
        public static AudioClip Break;

        static bool s_Init;
        static readonly System.Random Rng = new System.Random(20260122);

        public static void Init()
        {
            if (s_Init) return;
            s_Init = true;

            Wood = MakeThud("wood_thud", 0.26f, 150f, 0.055f, 0.55f, 0.28f);
            Concrete = MakeThud("concrete_thud", 0.30f, 95f, 0.070f, 0.45f, 0.42f);
            Soft = MakeThud("soft_thud", 0.20f, 70f, 0.060f, 0.70f, 0.12f);
            Metal = MakeClang("metal_clang", 0.55f, 420f, 0.20f, 0.55f);
            Glass = MakeClink("glass_clink", 0.22f, 2400f, 0.055f, 0.35f);
            Break = MakeGlassBreak("glass_break", 0.55f);
            Punch = MakePunch("punch", 0.24f);
            Whoosh = MakeWhoosh("whoosh", 0.22f);
            Pickup = MakeBlip("pickup", 0.10f, 660f, 990f);
            UiClick = MakeBlip("ui_click", 0.05f, 880f, 880f);
            Footstep = MakeFootstep("footstep", 0.13f);
            RagdollHit = MakeThud("body_thud", 0.38f, 60f, 0.110f, 0.85f, 0.35f);
        }

        public static AudioClip ForImpact(ImpactSound kind)
        {
            Init();
            switch (kind)
            {
                case ImpactSound.Wood: return Wood;
                case ImpactSound.Metal: return Metal;
                case ImpactSound.Glass: return Glass;
                case ImpactSound.Soft: return Soft;
                case ImpactSound.Concrete: return Concrete;
                default: return Concrete;
            }
        }

        // ------------------------------------------------------------------
        // Генераторы
        // ------------------------------------------------------------------
        static AudioClip MakeClip(string name, float duration, Func<float, float> sample)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(duration * SampleRate));
            var data = new float[n];
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float v = sample(t);
                if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                v = Mathf.Clamp(v, -1f, 1f);
                data[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            // нормализация, чтобы разные звуки не «стреляли» по громкости
            if (peak > 0.0001f)
            {
                float k = 0.92f / peak;
                for (int i = 0; i < n; i++) data[i] *= k;
            }
            // короткий фейд в конце, чтобы не было щелчка
            int fade = Mathf.Min(n, SampleRate / 200);
            for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;

            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise()
        {
            return (float)(Rng.NextDouble() * 2.0 - 1.0);
        }

        /// <summary>Глухой удар: синус со снижающейся частотой + шумовая атака.</summary>
        static AudioClip MakeThud(string name, float dur, float freq, float decay, float noiseMix, float noiseDecay)
        {
            float phase = 0f;
            float lp = 0f;
            return MakeClip(name, dur, t =>
            {
                float pitch = freq * (1f + 2.2f * Mathf.Exp(-t * 45f));
                phase += 2f * Mathf.PI * pitch / SampleRate;
                if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t / decay);
                // однополюсный ФНЧ на шуме — получается «пыльный» призвук
                lp += (Noise() - lp) * 0.22f;
                float n = lp * noiseMix * Mathf.Exp(-t / noiseDecay);
                return body + n;
            });
        }

        /// <summary>Металлический лязг: набор негармонических обертонов.</summary>
        static AudioClip MakeClang(string name, float dur, float freq, float decay, float brightness)
        {
            float[] ratios = { 1f, 2.41f, 3.77f, 5.13f, 6.82f };
            float[] decays = { 1f, 0.62f, 0.41f, 0.27f, 0.18f };
            float[] phases = new float[ratios.Length];
            float lp = 0f;
            return MakeClip(name, dur, t =>
            {
                float sum = 0f;
                for (int i = 0; i < ratios.Length; i++)
                {
                    phases[i] += 2f * Mathf.PI * freq * ratios[i] / SampleRate;
                    sum += Mathf.Sin(phases[i]) * Mathf.Exp(-t / (decay * decays[i])) * (1f / (1f + i * 0.7f));
                }
                lp += (Noise() - lp) * 0.5f;
                sum += lp * brightness * Mathf.Exp(-t / 0.012f);
                return sum * 0.7f;
            });
        }

        static AudioClip MakeClink(string name, float dur, float freq, float decay, float brightness)
        {
            float[] ratios = { 1f, 1.93f, 3.11f };
            float[] phases = new float[ratios.Length];
            return MakeClip(name, dur, t =>
            {
                float sum = 0f;
                for (int i = 0; i < ratios.Length; i++)
                {
                    phases[i] += 2f * Mathf.PI * freq * ratios[i] / SampleRate;
                    sum += Mathf.Sin(phases[i]) * Mathf.Exp(-t / (decay * (1f - i * 0.25f)));
                }
                return sum * 0.6f + Noise() * brightness * Mathf.Exp(-t / 0.006f);
            });
        }

        static AudioClip MakeGlassBreak(string name, float dur)
        {
            float lp = 0f;
            return MakeClip(name, dur, t =>
            {
                // серия коротких "осколочных" щелчков
                float env = Mathf.Exp(-t / 0.16f);
                lp += (Noise() - lp) * 0.65f;
                float sh = lp * env;
                int idx = Mathf.FloorToInt(t * 26f);
                float clickT = t - idx / 26f;
                float click = Mathf.Sin(2f * Mathf.PI * (1800f + (idx % 5) * 620f) * clickT) * Mathf.Exp(-clickT / 0.012f);
                return sh * 0.8f + click * 0.55f * env;
            });
        }

        static AudioClip MakePunch(string name, float dur)
        {
            float phase = 0f;
            float lp = 0f;
            return MakeClip(name, dur, t =>
            {
                float pitch = 110f * (1f + 3f * Mathf.Exp(-t * 60f));
                phase += 2f * Mathf.PI * pitch / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t / 0.045f);
                lp += (Noise() - lp) * 0.12f;
                float air = lp * Mathf.Exp(-t / 0.09f) * 0.5f;
                float impact = Noise() * Mathf.Exp(-t / 0.008f) * 0.6f;
                return body * 0.8f + air + impact;
            });
        }

        static AudioClip MakeWhoosh(string name, float dur)
        {
            float lp = 0f, hp = 0f;
            return MakeClip(name, dur, t =>
            {
                lp += (Noise() - lp) * (0.08f + 0.5f * (t / dur));
                hp = Noise() - lp;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                return hp * env * 0.55f;
            });
        }

        static AudioClip MakeFootstep(string name, float dur)
        {
            float lp = 0f;
            float phase = 0f;
            return MakeClip(name, dur, t =>
            {
                lp += (Noise() - lp) * 0.35f;
                float grit = lp * Mathf.Exp(-t / 0.035f);
                phase += 2f * Mathf.PI * 72f / SampleRate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t / 0.028f) * 0.7f;
                return grit * 0.55f + thump;
            });
        }

        static AudioClip MakeBlip(string name, float dur, float f0, float f1)
        {
            float phase = 0f;
            return MakeClip(name, dur, t =>
            {
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / dur));
                phase += 2f * Mathf.PI * f / SampleRate;
                float env = Mathf.Exp(-t / (dur * 0.5f));
                return Mathf.Sin(phase) * env * 0.5f;
            });
        }
    }
}
