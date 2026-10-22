using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace Sandbox.EditorTools
{
    /// <summary>
    /// Создаёт и назначает URP-ассеты через отражение.
    ///
    /// Почему отражение: игровые сборки не зависят от сборок URP, поэтому проект
    /// компилируется даже если URP-пакет обновился. Ассеты создаются самим URP
    /// (UniversalRenderPipelineAsset.Create), а не вручную написанным YAML — это гарантирует,
    /// что внутренние ссылки (шейдеры, renderer features) будут корректными
    /// для установленной версии пакета.
    /// </summary>
    public static class UrpSetup
    {
        public const string Folder = "Assets/_Game/Rendering";
        public const string AssetPath = Folder + "/URP-Sandbox.asset";
        public const string RendererPath = Folder + "/URP-Sandbox-Renderer.asset";

        const string UrpAssembly = "Unity.RenderPipelines.Universal.Runtime";

        public static bool Ensure(bool verbose = true)
        {
            try
            {
                Type assetType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, {UrpAssembly}");
                Type rendererType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRendererData, {UrpAssembly}");
                Type rendererDataType = Type.GetType($"UnityEngine.Rendering.Universal.ScriptableRendererData, {UrpAssembly}");

                if (assetType == null || rendererType == null || rendererDataType == null)
                {
                    Debug.LogWarning("[Sandbox] URP не найден (нет сборки Unity.RenderPipelines.Universal.Runtime). " +
                                     "Проверьте Packages/manifest.json — проект продолжит работать на встроенном конвейере.");
                    return false;
                }

                if (!AssetDatabase.IsValidFolder(Folder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/_Game")) AssetDatabase.CreateFolder("Assets", "_Game");
                    AssetDatabase.CreateFolder("Assets/_Game", "Rendering");
                }

                // ---------------- renderer data ----------------
                var renderer = AssetDatabase.LoadAssetAtPath(RendererPath, rendererType) as ScriptableObject;
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance(rendererType) as ScriptableObject;
                    if (renderer == null)
                    {
                        Debug.LogError("[Sandbox] Не удалось создать UniversalRendererData.");
                        return false;
                    }
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }

                // ---------------- pipeline asset ----------------
                var urp = AssetDatabase.LoadAssetAtPath(AssetPath, assetType) as ScriptableObject;
                if (urp != null && !HasRenderer(urp))
                {
                    if (verbose) Debug.LogWarning("[Sandbox] URP-ассет повреждён (пустой список рендереров) — пересоздаю.");
                    AssetDatabase.DeleteAsset(AssetPath);
                    urp = null;
                }

                if (urp == null)
                {
                    MethodInfo create = assetType.GetMethod("Create",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { rendererDataType }, null);
                    if (create == null)
                    {
                        Debug.LogError("[Sandbox] UniversalRenderPipelineAsset.Create(ScriptableRendererData) не найден.");
                        return false;
                    }
                    urp = create.Invoke(null, new object[] { renderer }) as ScriptableObject;
                    if (urp == null)
                    {
                        Debug.LogError("[Sandbox] Не удалось создать UniversalRenderPipelineAsset.");
                        return false;
                    }
                    AssetDatabase.CreateAsset(urp, AssetPath);
                }

                Configure(urp);
                EditorUtility.SetDirty(urp);
                EditorUtility.SetDirty(renderer);

                var pipelineAsset = urp as RenderPipelineAsset;
                if (pipelineAsset != null)
                {
                    if (GraphicsSettings.defaultRenderPipeline != pipelineAsset)
                        GraphicsSettings.defaultRenderPipeline = pipelineAsset;

                    int levels = QualitySettings.names.Length;
                    for (int i = 0; i < levels; i++)
                    {
                        QualitySettings.SetQualityLevel(i, false);
                        if (QualitySettings.renderPipeline != pipelineAsset) QualitySettings.renderPipeline = pipelineAsset;
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (verbose) Debug.Log($"[Sandbox] URP готов: {AssetPath} назначен конвейером по умолчанию.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Sandbox] UrpSetup.Ensure() упал: " + e);
                return false;
            }
        }

        static bool HasRenderer(ScriptableObject urp)
        {
            try
            {
                var so = new SerializedObject(urp);
                var list = so.FindProperty("m_RendererDataList");
                if (list == null || list.arraySize == 0) return false;
                return list.GetArrayElementAtIndex(0).objectReferenceValue != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Настройки под Galaxy A53: без HDR, 1 каскад, 4 доп. света, SRP Batcher.</summary>
        static void Configure(ScriptableObject urp)
        {
            Set(urp, "m_SupportsHDR", false);
            Set(urp, "m_MSAA", 1);
            Set(urp, "m_RenderScale", 1.0f);
            Set(urp, "m_ShadowDistance", 26f);
            Set(urp, "m_ShadowCascadeCount", 1);
            Set(urp, "m_MainLightShadowmapResolution", 1024);
            Set(urp, "m_AdditionalLightsRenderingMode", 1);
            Set(urp, "m_AdditionalLightsPerObjectLimit", 4);
            Set(urp, "m_AdditionalLightShadowsSupported", false);
            Set(urp, "m_SoftShadowsSupported", true);
            Set(urp, "m_UseSRPBatcher", true);
            Set(urp, "m_SupportsDynamicBatching", true);
            Set(urp, "m_RequireDepthTexture", false);
            Set(urp, "m_RequireOpaqueTexture", false);
            Set(urp, "m_ReflectionProbeBlending", false);
            Set(urp, "m_ReflectionProbeBoxProjection", false);
            Set(urp, "m_MixedLightingSupported", true);
            Set(urp, "m_ShadowCascades", 0);
            Set(urp, "m_Cascade2Split", 0.25f);
            Set(urp, "m_ShadowDepthBias", 1.1f);
            Set(urp, "m_ShadowNormalBias", 0.6f);
        }

        static void Set(ScriptableObject target, string field, object value)
        {
            try
            {
                var so = new SerializedObject(target);
                var prop = so.FindProperty(field);
                if (prop == null) return;
                switch (value)
                {
                    case bool b:
                        if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = b;
                        else if (prop.propertyType == SerializedPropertyType.Integer) prop.intValue = b ? 1 : 0;
                        break;
                    case int i:
                        if (prop.propertyType == SerializedPropertyType.Integer) prop.intValue = i;
                        else if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = i != 0;
                        break;
                    case float f:
                        if (prop.propertyType == SerializedPropertyType.Float) prop.floatValue = f;
                        break;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (Exception)
            {
                // поле может отсутствовать в другой версии URP — пропускаем
            }
        }
    }
}
