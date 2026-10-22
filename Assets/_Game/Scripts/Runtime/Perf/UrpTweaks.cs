using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sandbox
{
    /// <summary>
    /// Тонкая настройка URP-ассета из рантайма через отражение.
    /// Отражение используется намеренно: скрипты игры не зависят от сборок URP,
    /// поэтому проект компилируется даже если URP-пакет обновили/переименовали,
    /// а настройки просто молча не применятся (игра продолжит работать).
    /// </summary>
    public static class UrpTweaks
    {
        static object s_Asset;
        static Type s_Type;
        static bool s_Tried;
        static int s_Failures;

        public static bool Available
        {
            get
            {
                EnsureAsset();
                return s_Asset != null;
            }
        }

        public static string Describe()
        {
            EnsureAsset();
            if (s_Asset == null) return "URP asset not found (built-in fallback)";
            return s_Asset.ToString();
        }

        static void EnsureAsset()
        {
            if (s_Tried) return;
            s_Tried = true;
            try
            {
                RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
                if (asset == null) asset = QualitySettings.renderPipeline;
                if (asset == null) asset = GraphicsSettings.defaultRenderPipeline;
                s_Asset = asset;
                s_Type = asset != null ? asset.GetType() : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sandbox] UrpTweaks: не удалось получить RenderPipelineAsset: " + e.Message);
            }
        }

        /// <summary>Сбрасывает кэш (нужно после смены pipeline-ассета в редакторе).</summary>
        public static void Reset()
        {
            s_Tried = false;
            s_Asset = null;
            s_Type = null;
            s_Failures = 0;
        }

        public static void SetRenderScale(float scale)
        {
            scale = Mathf.Clamp(scale, 0.5f, 1.5f);
            // У URP renderScale меняется только в большую сторону без потери качества,
            // поэтому дополнительно подстраховываемся Screen.SetResolution не требуется.
            Set("renderScale", scale);
        }

        public static void SetShadowDistance(float meters)
        {
            Set("shadowDistance", Mathf.Max(1f, meters));
        }

        public static void SetMsaa(int samples)
        {
            Set("msaaSampleCount", Mathf.Clamp(samples, 1, 8));
        }

        public static void SetShadowCascades(int count)
        {
            Set("shadowCascadeCount", Mathf.Clamp(count, 1, 4));
        }

        public static void SetMainLightShadowResolution(int res)
        {
            Set("mainLightShadowmapResolution", res);
        }

        public static void SetAdditionalLightsPerObject(int count)
        {
            Set("additionalLightsPerObjectLimit", Mathf.Clamp(count, 0, 8));
        }

        public static void SetSoftShadows(bool enabled)
        {
            Set("supportsSoftShadows", enabled);
        }

        public static void SetHdr(bool enabled)
        {
            Set("supportsHDR", enabled);
        }

        public static void SetAdditionalLightShadows(bool enabled)
        {
            Set("supportsAdditionalLightShadows", enabled);
        }

        public static void SetDepthTexture(bool enabled)
        {
            Set("supportsCameraDepthTexture", enabled);
        }

        public static void SetOpaqueTexture(bool enabled)
        {
            Set("supportsCameraOpaqueTexture", enabled);
        }

        public static float GetRenderScale()
        {
            var v = Get("renderScale");
            return v is float f ? f : 1f;
        }

        static void Set(string property, object value)
        {
            EnsureAsset();
            if (s_Asset == null || s_Failures > 12) return;
            try
            {
                PropertyInfo pi = FindProperty(property);
                if (pi == null || !pi.CanWrite)
                {
                    s_Failures++;
                    return;
                }
                object converted = Convert.ChangeType(value, pi.PropertyType);
                pi.SetValue(s_Asset, converted, null);
            }
            catch (Exception)
            {
                s_Failures++;
            }
        }

        static object Get(string property)
        {
            EnsureAsset();
            if (s_Asset == null) return null;
            try
            {
                PropertyInfo pi = FindProperty(property);
                return pi != null && pi.CanRead ? pi.GetValue(s_Asset, null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static PropertyInfo FindProperty(string name)
        {
            if (s_Type == null) return null;
            Type t = s_Type;
            while (t != null && t != typeof(object))
            {
                PropertyInfo pi = t.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (pi != null) return pi;
                t = t.BaseType;
            }
            return null;
        }
    }
}
