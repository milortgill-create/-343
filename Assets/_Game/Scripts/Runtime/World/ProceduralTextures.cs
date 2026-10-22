using System;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Все текстуры генерируются процедурно в рантайме — в проекте нет ни одного
    /// бинарного ассета, поэтому APK собирается из чистого кода и весит мало.
    /// </summary>
    public static class ProceduralTextures
    {
        public class Set
        {
            public Texture2D Albedo;
            public Texture2D Normal;
        }

        static readonly System.Random Rng = new System.Random(1337);

        // ------------------------------------------------------------------
        // Базовые хелперы
        // ------------------------------------------------------------------
        static Texture2D NewTex(int w, int h, bool linear)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, linear);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>Фрактальный шум Перлина (4 октавы), значения 0..1.</summary>
        static float Fbm(float x, float y, float baseFreq, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, freq = baseFreq, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / Mathf.Max(0.0001f, norm);
        }

        /// <summary>Преобразует карту высот в тангенс-пространственную normal map.</summary>
        static Texture2D HeightToNormal(float[] heights, int size, float strength)
        {
            var tex = NewTex(size, size, true);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float left = heights[Wrap(x - 1, size) + y * size];
                    float right = heights[Wrap(x + 1, size) + y * size];
                    float down = heights[x + Wrap(y - 1, size) * size];
                    float up = heights[x + Wrap(y + 1, size) * size];

                    float dx = (left - right) * strength;
                    float dy = (down - up) * strength;
                    var n = new Vector3(dx, dy, 1f).normalized;

                    pixels[x + y * size] = new Color32(
                        (byte)((n.x * 0.5f + 0.5f) * 255f),
                        (byte)((n.y * 0.5f + 0.5f) * 255f),
                        (byte)((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }

        static int Wrap(int v, int size)
        {
            v %= size;
            if (v < 0) v += size;
            return v;
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        // ------------------------------------------------------------------
        // Материалы мира
        // ------------------------------------------------------------------
        public static Set Concrete(int size = 256, Color? tint = null, float grain = 1f)
        {
            var albedo = NewTex(size, size, false);
            var px = new Color32[size * size];
            var heights = new float[size * size];
            Color baseCol = tint ?? new Color(0.52f, 0.52f, 0.53f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float n = Fbm(u, v, 6f, 4);
                    float fine = Fbm(u, v, 42f, 3);
                    float speck = Hash(x, y) > 0.985f ? 0.22f : 0f;
                    float crack = Mathf.Pow(1f - Mathf.Abs(Fbm(u + 3.7f, v + 9.1f, 3f, 3) - 0.5f) * 2f, 12f) * 0.35f;

                    float lum = 0.86f + (n - 0.5f) * 0.30f * grain + (fine - 0.5f) * 0.14f * grain + speck - crack;
                    lum = Mathf.Clamp01(lum);
                    heights[x + y * size] = n * 0.6f + fine * 0.4f - crack;
                    px[x + y * size] = new Color32(
                        (byte)(baseCol.r * lum * 255f),
                        (byte)(baseCol.g * lum * 255f),
                        (byte)(baseCol.b * lum * 255f),
                        255);
                }
            }
            albedo.SetPixels32(px);
            albedo.Apply(true, false);
            return new Set { Albedo = albedo, Normal = HeightToNormal(heights, size, 1.6f) };
        }

        public static Set Wood(int size = 256, Color? tint = null)
        {
            var albedo = NewTex(size, size, false);
            var px = new Color32[size * size];
            var heights = new float[size * size];
            Color baseCol = tint ?? new Color(0.62f, 0.44f, 0.26f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float warp = Fbm(u, v, 3f, 3);
                    // годовые кольца вдоль оси U
                    float rings = Mathf.Abs(Mathf.Sin((v * 9f + warp * 1.6f) * Mathf.PI));
                    rings = Mathf.Pow(rings, 0.6f);
                    float fine = Fbm(u, v, 60f, 2);
                    float knot = Mathf.Clamp01(1f - Mathf.Abs(warp - 0.5f) * 8f) * 0.25f;

                    float lum = 0.72f + rings * 0.34f + (fine - 0.5f) * 0.16f - knot;
                    lum = Mathf.Clamp01(lum);
                    heights[x + y * size] = rings * 0.5f + fine * 0.5f;
                    px[x + y * size] = new Color32(
                        (byte)(baseCol.r * lum * 255f),
                        (byte)(baseCol.g * lum * 255f),
                        (byte)(baseCol.b * lum * 255f),
                        255);
                }
            }
            albedo.SetPixels32(px);
            albedo.Apply(true, false);
            return new Set { Albedo = albedo, Normal = HeightToNormal(heights, size, 1.1f) };
        }

        public static Set Metal(int size = 256, Color? tint = null, float rust = 0.35f)
        {
            var albedo = NewTex(size, size, false);
            var px = new Color32[size * size];
            var heights = new float[size * size];
            Color baseCol = tint ?? new Color(0.60f, 0.62f, 0.66f);
            var rustCol = new Color(0.42f, 0.24f, 0.14f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    // шлифовка — вытянутый шум по горизонтали
                    float brush = Fbm(u * 0.25f, v, 40f, 3);
                    float dent = Fbm(u, v, 5f, 3);
                    float rustMask = Mathf.Clamp01((Fbm(u + 11f, v + 4f, 4f, 4) - (1f - rust)) / Mathf.Max(0.05f, rust));

                    float lum = 0.88f + (brush - 0.5f) * 0.22f + (dent - 0.5f) * 0.18f;
                    Color c = Color.Lerp(baseCol * lum, rustCol * (0.7f + lum * 0.4f), rustMask);
                    heights[x + y * size] = dent * 0.7f + brush * 0.3f - rustMask * 0.2f;
                    px[x + y * size] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                }
            }
            albedo.SetPixels32(px);
            albedo.Apply(true, false);
            return new Set { Albedo = albedo, Normal = HeightToNormal(heights, size, 0.8f) };
        }

        /// <summary>Кирпичная кладка (для стен).</summary>
        public static Set Bricks(int size = 256)
        {
            var albedo = NewTex(size, size, false);
            var px = new Color32[size * size];
            var heights = new float[size * size];

            const int rows = 8;
            const int cols = 4;
            float rowH = (float)size / rows;
            float colW = (float)size / cols;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int row = Mathf.FloorToInt(y / rowH);
                    float offset = (row % 2) * colW * 0.5f;
                    float fx = (x + offset) % size;
                    int col = Mathf.FloorToInt(fx / colW);

                    float mortarX = Mathf.Abs(((fx % colW) / colW) - 0.5f);
                    float mortarY = Mathf.Abs((((y % rowH) / rowH)) - 0.5f);
                    bool isMortar = mortarX > 0.455f || mortarY > 0.44f;

                    float n = Fbm((float)x / size, (float)y / size, 30f, 3);
                    float brickTone = 0.75f + Hash(col, row) * 0.35f + (n - 0.5f) * 0.22f;

                    Color c;
                    if (isMortar)
                    {
                        c = new Color(0.55f, 0.54f, 0.51f) * (0.9f + (n - 0.5f) * 0.2f);
                        heights[x + y * size] = 0.15f + n * 0.1f;
                    }
                    else
                    {
                        c = new Color(0.55f * brickTone, 0.30f * brickTone, 0.24f * brickTone);
                        heights[x + y * size] = 0.6f + n * 0.25f;
                    }
                    px[x + y * size] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                }
            }
            albedo.SetPixels32(px);
            albedo.Apply(true, false);
            return new Set { Albedo = albedo, Normal = HeightToNormal(heights, size, 2.4f) };
        }

        /// <summary>Однотонная текстура (для мелких объектов).</summary>
        public static Texture2D Flat(Color color, int size = 8)
        {
            var tex = NewTex(size, size, false);
            var px = new Color32[size * size];
            var c = (Color32)color;
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>Плотная резина/пена для манекена.</summary>
        public static Set Foam(int size = 128, Color? tint = null)
        {
            var albedo = NewTex(size, size, false);
            var px = new Color32[size * size];
            var heights = new float[size * size];
            Color baseCol = tint ?? new Color(0.78f, 0.66f, 0.52f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Fbm((float)x / size, (float)y / size, 24f, 3);
                    float cell = Mathf.Abs(Fbm((float)x / size + 5f, (float)y / size + 2f, 12f, 2) - 0.5f);
                    float lum = 0.85f + (n - 0.5f) * 0.28f - Mathf.Clamp01(0.3f - cell) * 0.5f;
                    heights[x + y * size] = n * 0.7f + cell * 0.3f;
                    px[x + y * size] = new Color32(
                        (byte)(baseCol.r * lum * 255f), (byte)(baseCol.g * lum * 255f), (byte)(baseCol.b * lum * 255f), 255);
                }
            }
            albedo.SetPixels32(px);
            albedo.Apply(true, false);
            return new Set { Albedo = albedo, Normal = HeightToNormal(heights, size, 1.2f) };
        }

        /// <summary>Мишень на груди манекена.</summary>
        public static Texture2D TargetRings(int size = 256)
        {
            var tex = NewTex(size, size, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = new Color(0.85f, 0.82f, 0.75f);
                    if (r < 1f)
                    {
                        int ring = Mathf.FloorToInt(r * 4f);
                        c = (ring % 2 == 0) ? new Color(0.80f, 0.18f, 0.15f) : new Color(0.92f, 0.90f, 0.86f);
                        if (r < 0.16f) c = new Color(0.95f, 0.80f, 0.15f);
                    }
                    float a = Mathf.Clamp01((1f - r) * 6f);
                    px[x + y * size] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>Жёлто-чёрная предупреждающая разметка.</summary>
        public static Set Hazard(int size = 128)
        {
            var albedo = NewTex(size, size, false);
            var px = new Color32[size * size];
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float s = ((x + y) / (float)size * 8f) % 2f;
                    float n = Fbm((float)x / size, (float)y / size, 20f, 2);
                    Color c = s < 1f ? new Color(0.92f, 0.75f, 0.10f) : new Color(0.12f, 0.12f, 0.12f);
                    c *= 0.9f + (n - 0.5f) * 0.2f;
                    heights[x + y * size] = n;
                    px[x + y * size] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                }
            }
            albedo.SetPixels32(px);
            albedo.Apply(true, false);
            return new Set { Albedo = albedo, Normal = HeightToNormal(heights, size, 0.5f) };
        }

        // ------------------------------------------------------------------
        // UI-спрайты
        // ------------------------------------------------------------------
        public static Texture2D Circle(int size, Color color, float edgeSoftness = 0.08f)
        {
            var tex = NewTex(size, size, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((1f - r) / Mathf.Max(0.001f, edgeSoftness));
                    px[x + y * size] = new Color32((byte)(color.r * 255f), (byte)(color.g * 255f),
                        (byte)(color.b * 255f), (byte)(color.a * a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        public static Texture2D Ring(int size, Color color, float thickness = 0.09f)
        {
            var tex = NewTex(size, size, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float d = Mathf.Abs(r - (1f - thickness));
                    float a = Mathf.Clamp01((thickness * 0.5f - d) / 0.02f);
                    px[x + y * size] = new Color32((byte)(color.r * 255f), (byte)(color.g * 255f),
                        (byte)(color.b * 255f), (byte)(color.a * a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        public static Texture2D RoundedRect(int w, int h, int radius, Color color)
        {
            var tex = NewTex(w, h, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Max(radius - x, Mathf.Max(0, x - (w - 1 - radius)));
                    float dy = Mathf.Max(radius - y, Mathf.Max(0, y - (h - 1 - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d);
                    px[y * w + x] = new Color32((byte)(color.r * 255f), (byte)(color.g * 255f),
                        (byte)(color.b * 255f), (byte)(color.a * a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>
        /// Экран смартфона: рисуем пикселями "домашний экран" с часами, батареей и иконками.
        /// Перерисовывается раз в секунду (время + FPS). Если передать existing —
        /// текстура перерисовывается на месте (без аллокаций, важно для мобильных).
        /// </summary>
        public static Texture2D PhoneScreen(int w, int h, string clock, string sub, float battery, bool cracked,
            Texture2D existing = null)
        {
            var tex = existing != null && existing.width == w && existing.height == h
                ? existing
                : NewTex(w, h, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[w * h];

            void Rect(int x0, int y0, int x1, int y1, Color c)
            {
                x0 = Mathf.Clamp(x0, 0, w - 1); x1 = Mathf.Clamp(x1, 0, w - 1);
                y0 = Mathf.Clamp(y0, 0, h - 1); y1 = Mathf.Clamp(y1, 0, h - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        px[y * w + x] = c;
            }

            void Rounded(int cx, int cy, int rw, int rh, Color c)
            {
                for (int y = cy - rh; y <= cy + rh; y++)
                {
                    for (int x = cx - rw; x <= cx + rw; x++)
                    {
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        float dx = Mathf.Abs(x - cx) / (float)rw;
                        float dy = Mathf.Abs(y - cy) / (float)rh;
                        if (dx * dx + dy * dy <= 1f) px[y * w + x] = c;
                    }
                }
            }

            // фон — тёмно-синий градиент
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)h;
                var c = Color.Lerp(new Color(0.06f, 0.09f, 0.16f), new Color(0.12f, 0.20f, 0.32f), t);
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }

            // строка состояния
            Rect(0, h - 26, w - 1, h - 1, new Color(0f, 0f, 0f, 0.35f));
            int bw = Mathf.RoundToInt((w - 40) * Mathf.Clamp01(battery));
            Rect(w - 34, h - 20, w - 8, h - 8, new Color(1f, 1f, 1f, 0.35f));
            Rect(w - 32, h - 18, w - 32 + Mathf.Max(0, bw * 26 / Mathf.Max(1, w - 40)), h - 10,
                battery > 0.2f ? new Color(0.35f, 0.85f, 0.4f) : new Color(0.9f, 0.3f, 0.25f));

            // часы
            DrawText(px, w, h, clock, 12, (int)(h * 0.74f), new Color(0.95f, 0.97f, 1f), 2);
            DrawText(px, w, h, sub, 10, (int)(h * 0.68f), new Color(0.65f, 0.75f, 0.9f), 1);

            // иконки "приложений"
            Color[] icons =
            {
                new Color(0.30f, 0.65f, 0.95f), new Color(0.95f, 0.60f, 0.25f),
                new Color(0.45f, 0.85f, 0.50f), new Color(0.85f, 0.40f, 0.55f),
                new Color(0.70f, 0.55f, 0.95f), new Color(0.95f, 0.85f, 0.35f)
            };
            for (int i = 0; i < icons.Length; i++)
            {
                int col = i % 3, row = i / 3;
                int cx = (col + 1) * w / 4;
                int cy = (int)(h * 0.42f) - row * (h / 7);
                Rounded(cx, cy, w / 12, w / 12, icons[i]);
                Rounded(cx, cy, w / 12 - 3, w / 12 - 3, icons[i] * 1.15f);
            }

            // нижняя "док"-панель
            Rect(6, 10, w - 7, 10 + h / 14, new Color(1f, 1f, 1f, 0.10f));
            for (int i = 0; i < 3; i++) Rounded((i + 1) * w / 4, 10 + h / 28, w / 16, w / 16, new Color(1f, 1f, 1f, 0.45f));

            if (cracked)
            {
                // трещины после сильного удара
                var rnd = new System.Random(7);
                for (int i = 0; i < 9; i++)
                {
                    int x = rnd.Next(w), y = rnd.Next(h);
                    float ang = (float)(rnd.NextDouble() * Math.PI * 2);
                    for (int s = 0; s < 90; s++)
                    {
                        ang += (float)(rnd.NextDouble() - 0.5) * 0.5f;
                        x += (int)Math.Round(Math.Cos(ang)); y += (int)Math.Round(Math.Sin(ang));
                        if (x < 0 || y < 0 || x >= w || y >= h) break;
                        px[y * w + x] = new Color32(235, 240, 245, 200);
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
        static readonly string[] GlyphChars =
        {
            "0123456789", "ABCDEFGHIJKLMNOPQRSTUVWXYZ", ":. -/"
        };

        // Каждый глиф — 5 строк по 3 бита (1 = пиксель).
        static readonly string[] Glyphs3x5 =
        {
            "111101101101111", // 0
            "010110010010111", // 1
            "111001111100111", // 2
            "111001111001111", // 3
            "101101111001001", // 4
            "111100111001111", // 5
            "111100111101111", // 6
            "111001001001001", // 7
            "111101111101111", // 8
            "111101111001111", // 9
            "010101111101101", // A
            "110101110101110", // B
            "011100100100011", // C
            "110101101101110", // D
            "111100111100111", // E
            "111100111100100", // F
            "011100101101011", // G
            "101101111101101", // H
            "111010010010111", // I
            "001001001101110", // J
            "101101110101101", // K
            "100100100100111", // L
            "101111111101101", // M
            "110101101101101", // N
            "010101101101010", // O
            "111101111100100", // P
            "010101101110011", // Q
            "111101111101101", // R
            "111100111001111", // S
            "111010010010010", // T
            "101101101101111", // U
            "101101101101010", // V
            "101101111111101", // W
            "101101010101101", // X
            "101101111010010", // Y
            "111001010100111", // Z
            "000010000010000", // :
            "000000000000010", // .
            "000000000000000", // (space)
            "000001000000000", // -
            "001010100100000", // /
        };

        static int GlyphIndex(char c)
        {
            c = char.ToUpperInvariant(c);
            int idx = GlyphChars[0].IndexOf(c);
            if (idx >= 0) return idx;
            idx = GlyphChars[1].IndexOf(c);
            if (idx >= 0) return 10 + idx;
            idx = GlyphChars[2].IndexOf(c);
            if (idx >= 0) return 36 + idx;
            return 38; // space
        }

        /// <summary>Рисует текст растровым шрифтом прямо в массив пикселей.</summary>
        public static void DrawText(Color32[] px, int w, int h, string text, int x, int y, Color color, int scale = 1)
        {
            if (string.IsNullOrEmpty(text)) return;
            int cursor = x;
            var c32 = (Color32)color;
            foreach (char ch in text)
            {
                string g = Glyphs3x5[GlyphIndex(ch)];
                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        if (g[(4 - row) * 3 + col] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int pxx = cursor + col * scale + sx;
                                int pyy = y + row * scale + sy;
                                if (pxx < 0 || pyy < 0 || pxx >= w || pyy >= h) continue;
                                px[pyy * w + pxx] = c32;
                            }
                        }
                    }
                }
                cursor += 4 * scale;
            }
        }

        /// <summary>Отдельная текстура с текстом (для табличек в мире).</summary>
        public static Texture2D Label(string text, int w = 256, int h = 64, Color? bg = null, Color? fg = null)
        {
            var tex = NewTex(w, h, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            Color b = bg ?? new Color(0.10f, 0.10f, 0.12f);
            Color f = fg ?? new Color(0.92f, 0.92f, 0.90f);
            var b32 = (Color32)b;
            for (int i = 0; i < px.Length; i++) px[i] = b32;
            int scale = Mathf.Max(1, Mathf.Min(h / 7, w / Mathf.Max(1, text.Length * 4)));
            int tw = text.Length * 4 * scale;
            DrawText(px, w, h, text, Mathf.Max(2, (w - tw) / 2), (h - 5 * scale) / 2, f, scale);
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}
