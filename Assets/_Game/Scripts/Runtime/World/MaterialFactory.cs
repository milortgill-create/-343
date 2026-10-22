using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sandbox
{
    /// <summary>
    /// Фабрика материалов. Работает и с URP (основной вариант), и со встроенным
    /// конвейером (запасной, если URP-ассет вдруг не подхватился) — поэтому в
    /// проекте не бывает "розовых" объектов.
    /// </summary>
    public static class MaterialFactory
    {
        static Shader s_Lit;
        static Shader s_Unlit;
        static bool s_Initialized;
        static bool s_Urp;

        static readonly List<Material> Created = new List<Material>();

        // Готовые общие материалы (создаются один раз в Init()).
        public static Material ConcreteFloor;
        public static Material ConcreteWall;
        public static Material BrickWall;
        public static Material BrickSolid;
        public static Material Wood;
        public static Material DarkWood;
        public static Material Metal;
        public static Material RustyMetal;
        public static Material Glass;
        public static Material Foam;
        public static Material Target;
        public static Material Hazard;
        public static Material Plastic;
        public static Material Rubber;
        public static Material Skin;
        public static Material PhoneBody;
        public static Material LightPanel;
        public static Material Grid;

        public static bool IsUrp
        {
            get { EnsureShader(); return s_Urp; }
        }

        public static Shader Lit
        {
            get { EnsureShader(); return s_Lit; }
        }

        static void EnsureShader()
        {
            if (s_Initialized && s_Lit != null) return;

            string[] litCandidates =
            {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Standard"
            };
            foreach (var name in litCandidates)
            {
                var sh = Shader.Find(name);
                if (sh != null)
                {
                    s_Lit = sh;
                    s_Urp = name.StartsWith("Universal");
                    break;
                }
            }

            string[] unlitCandidates =
            {
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Sprites/Default"
            };
            foreach (var name in unlitCandidates)
            {
                var sh = Shader.Find(name);
                if (sh != null) { s_Unlit = sh; break; }
            }

            if (s_Lit == null)
            {
                Debug.LogError("[Sandbox] Не найден ни один Lit-шейдер! Проверьте, что URP установлен.");
                s_Lit = Shader.Find("Sprites/Default");
            }

            s_Initialized = true;
        }

        /// <summary>Создаёт материал с albedo/normal и нужными параметрами поверхности.</summary>
        public static Material Create(string name, ProceduralTextures.Set set, Color tint,
            float metallic = 0f, float smoothness = 0.5f, Vector2? tiling = null, Vector2? offset = null,
            bool transparent = false, float alpha = 1f, Color? emission = null, float emissionStrength = 1f,
            float normalStrength = 1f, bool doubleSided = false)
        {
            EnsureShader();
            var mat = new Material(s_Lit) { name = name };
            Created.Add(mat);

            Vector2 t = tiling ?? Vector2.one;
            Vector2 o = offset ?? Vector2.zero;

            if (s_Urp)
            {
                if (set != null && set.Albedo != null)
                {
                    mat.SetTexture("_BaseMap", set.Albedo);
                    mat.SetTextureScale("_BaseMap", t);
                    mat.SetTextureOffset("_BaseMap", o);
                }
                Color baseCol = tint;
                baseCol.a *= alpha;
                mat.SetColor("_BaseColor", baseCol);

                if (set != null && set.Normal != null)
                {
                    mat.SetTexture("_BumpMap", set.Normal);
                    mat.SetTextureScale("_BumpMap", t);
                    mat.SetFloat("_BumpScale", normalStrength);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }
            else
            {
                if (set != null && set.Albedo != null)
                {
                    mat.SetTexture("_MainTex", set.Albedo);
                    mat.SetTextureScale("_MainTex", t);
                    mat.SetTextureOffset("_MainTex", o);
                }
                Color baseCol = tint;
                baseCol.a *= alpha;
                mat.SetColor("_Color", baseCol);
                if (set != null && set.Normal != null)
                {
                    mat.SetTexture("_BumpMap", set.Normal);
                    mat.SetTextureScale("_BumpMap", t);
                    mat.SetFloat("_BumpScale", normalStrength);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            SetFloatSafe(mat, "_Metallic", metallic);
            SetFloatSafe(mat, s_Urp ? "_Smoothness" : "_Glossiness", smoothness);

            // Всегда включаем ключевое слово _EMISSION с чёрным цветом: это ничего
            // не меняет визуально, но позволяет подсвечивать цель взаимодействия
            // через MaterialPropertyBlock (из MPB ключевые слова не включаются).
            if (emission == null)
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (doubleSided) SetFloatSafe(mat, "_Cull", 0f);

            if (emission.HasValue)
            {
                Color e = emission.Value * emissionStrength;
                e.a = 1f;
                mat.SetColor("_EmissionColor", e);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (transparent) MakeTransparent(mat, alpha);
            else MakeOpaque(mat);

            return mat;
        }

        public static Material CreateUnlit(string name, Color color, Texture2D tex = null)
        {
            EnsureShader();
            Shader sh = s_Unlit != null ? s_Unlit : s_Lit;
            var mat = new Material(sh) { name = name, color = color };
            Created.Add(mat);
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            if (color.a < 0.999f) MakeTransparent(mat, color.a);
            return mat;
        }

        static void SetFloatSafe(Material mat, string prop, float value)
        {
            if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
        }

        public static void MakeOpaque(Material mat)
        {
            if (s_Urp)
            {
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }
            else
            {
                mat.SetFloat("_Mode", 0f);
                mat.SetInt("_SrcBlend", (int)BlendMode.One);
                mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = -1;
            }
        }

        public static void MakeTransparent(Material mat, float alpha)
        {
            if (s_Urp)
            {
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                c.a = alpha;
                mat.SetColor("_BaseColor", c);
            }
            else if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                c.a = alpha;
                mat.SetColor("_Color", c);
            }
        }

        /// <summary>Создаёт все общие материалы мира. Вызывается один раз при старте.</summary>
        public static void Init()
        {
            if (ConcreteFloor != null) return;
            EnsureShader();

            var concrete = ProceduralTextures.Concrete(256, new Color(0.55f, 0.55f, 0.56f), 1f);
            var concreteDark = ProceduralTextures.Concrete(256, new Color(0.34f, 0.35f, 0.37f), 1.15f);
            var bricks = ProceduralTextures.Bricks(256);
            var wood = ProceduralTextures.Wood(256, new Color(0.66f, 0.47f, 0.28f));
            var darkWood = ProceduralTextures.Wood(256, new Color(0.36f, 0.25f, 0.16f));
            var metal = ProceduralTextures.Metal(256, new Color(0.62f, 0.64f, 0.68f), 0.18f);
            var rusty = ProceduralTextures.Metal(256, new Color(0.52f, 0.50f, 0.46f), 0.62f);
            var foam = ProceduralTextures.Foam(128, new Color(0.80f, 0.68f, 0.54f));
            var hazard = ProceduralTextures.Hazard(128);
            var grid = ProceduralTextures.Concrete(128, new Color(0.40f, 0.42f, 0.44f), 0.6f);

            ConcreteFloor = Create("ConcreteFloor", concrete, Color.white, 0f, 0.35f,
                new Vector2(SandboxConfig.ArenaWidth / 4f, SandboxConfig.ArenaDepth / 4f));
            ConcreteWall = Create("ConcreteWall", concreteDark, Color.white, 0f, 0.28f, new Vector2(3f, 1.2f));
            BrickWall = Create("BrickWall", bricks, Color.white, 0f, 0.30f, new Vector2(4f, 1.6f));
            BrickSolid = Create("BrickSolid", ProceduralTextures.Concrete(128, new Color(0.60f, 0.31f, 0.25f), 1.4f),
                Color.white, 0f, 0.28f, new Vector2(2f, 2f));
            Wood = Create("Wood", wood, Color.white, 0f, 0.45f, new Vector2(1f, 1f));
            DarkWood = Create("DarkWood", darkWood, Color.white, 0f, 0.42f, new Vector2(1f, 1f));
            Metal = Create("BarrelMetal", metal, Color.white, 0.85f, 0.62f, new Vector2(1f, 1f));
            RustyMetal = Create("RustyMetal", rusty, Color.white, 0.55f, 0.42f, new Vector2(1f, 1f));
            Hazard = Create("Hazard", hazard, Color.white, 0f, 0.4f, new Vector2(2f, 2f));
            Grid = Create("Grid", grid, Color.white, 0f, 0.3f, new Vector2(6f, 6f));
            Foam = Create("DummyFoam", foam, Color.white, 0f, 0.30f, new Vector2(2f, 2f));

            Target = Create("DummyTarget", null, Color.white, 0f, 0.35f);
            var targetTex = ProceduralTextures.TargetRings(256);
            if (s_Urp) Target.SetTexture("_BaseMap", targetTex);
            else Target.SetTexture("_MainTex", targetTex);

            Glass = Create("Glass", null, new Color(0.55f, 0.80f, 0.55f, 0.34f), 0f, 0.92f,
                transparent: true, alpha: 0.34f);

            Plastic = Create("Plastic", null, new Color(0.16f, 0.17f, 0.19f), 0.1f, 0.55f);
            Rubber = Create("Rubber", foam, new Color(0.22f, 0.22f, 0.24f), 0f, 0.18f, new Vector2(3f, 3f));
            Skin = Create("Hands", foam, new Color(0.85f, 0.68f, 0.58f), 0f, 0.42f, new Vector2(2f, 2f));
            PhoneBody = Create("PhoneBody", null, new Color(0.10f, 0.11f, 0.13f), 0.75f, 0.75f);
            LightPanel = Create("LightPanel", null, Color.white, 0f, 0.2f,
                emission: new Color(1f, 0.97f, 0.90f), emissionStrength: 3.2f);
        }

        public static void Cleanup()
        {
            foreach (var m in Created)
            {
                if (m != null) Object.Destroy(m);
            }
            Created.Clear();
            ConcreteFloor = null; ConcreteWall = null; BrickWall = null; BrickSolid = null; Wood = null; DarkWood = null;
            Metal = null; RustyMetal = null; Glass = null; Foam = null; Target = null; Hazard = null;
            Plastic = null; Rubber = null; Skin = null; PhoneBody = null; LightPanel = null; Grid = null;
        }
    }
}
