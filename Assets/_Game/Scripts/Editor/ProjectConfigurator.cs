using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sandbox.EditorTools
{
    /// <summary>
    /// Приводит проект к рабочему состоянию при каждом открытии редактора и
    /// перед сборкой: слои, URP, always-included шейдеры, настройки Android-плеера,
    /// список сцен в сборке. Всё идемпотентно — ничего не переписывается, если
    /// значения уже правильные.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectConfigurator
    {
        public const string BundleId = "com.milortgill.sandboxfps";
        public const string CompanyName = "MilortGill";
        public const string ProductName = "Sandbox FPS — Test Polygon";
        public const string ScenePath = "Assets/_Game/Scenes/Sandbox.unity";

        public static readonly Dictionary<int, string> RequiredLayers = new Dictionary<int, string>
        {
            { 8, "Player" },
            { 9, "Dummy" },
            { 10, "Prop" },
            { 11, "World" },
            { 12, "Held" },
            { 13, "Hands" },
            { 14, "Debris" }
        };

        public static readonly string[] RequiredTags = { "Interactable", "Dummy", "SpawnPoint" };

        public static readonly string[] RequiredShaders =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Terrain/Lit",
            "Legacy Shaders/Diffuse",
            "Sprites/Default"
        };

        static ProjectConfigurator()
        {
            EditorApplication.delayCall += () =>
            {
                try { Ensure(false); }
                catch (Exception e) { Debug.LogWarning("[Sandbox] Авто-настройка проекта не удалась: " + e.Message); }
            };
        }

        [MenuItem("Tools/Sandbox/1. Настроить проект (слои, URP, Android)", false, 10)]
        public static void EnsureMenu()
        {
            Ensure(true);
            EditorUtility.DisplayDialog("Sandbox", "Проект настроен: слои, URP, шейдеры, Android-настройки и сцена в сборке.\nПодробности — в Console.", "OK");
        }

        public static void Ensure(bool verbose = true)
        {
            EnsureLayers(verbose);
            EnsureTags(verbose);
            UrpSetup.Ensure(verbose);
            EnsureAlwaysIncludedShaders(verbose);
            ApplyPlayerSettings(verbose);
            EnsureBuildScenes(verbose);
            if (verbose) Debug.Log("[Sandbox] Настройка проекта завершена.");
        }

        // ------------------------------------------------------------------
        static UnityEngine.Object LoadProjectSettingsAsset(string path)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            return all != null && all.Length > 0 ? all[0] : null;
        }

        public static void EnsureLayers(bool verbose)
        {
            var asset = LoadProjectSettingsAsset("ProjectSettings/TagManager.asset");
            if (asset == null)
            {
                Debug.LogWarning("[Sandbox] TagManager.asset не найден — слои будут созданы при следующем открытии проекта.");
                return;
            }

            var so = new SerializedObject(asset);
            var layers = so.FindProperty("layers");
            if (layers == null) return;

            bool changed = false;
            foreach (var kv in RequiredLayers)
            {
                if (kv.Key >= layers.arraySize) continue;
                var el = layers.GetArrayElementAtIndex(kv.Key);
                if (el.stringValue != kv.Value)
                {
                    el.stringValue = kv.Value;
                    changed = true;
                }
            }
            if (changed)
            {
                so.ApplyModifiedProperties();
                if (verbose) Debug.Log("[Sandbox] Слои проекта обновлены (Player/Dummy/Prop/World/Held/Hands/Debris).");
            }
        }

        public static void EnsureTags(bool verbose)
        {
            var asset = LoadProjectSettingsAsset("ProjectSettings/TagManager.asset");
            if (asset == null) return;

            var so = new SerializedObject(asset);
            var tags = so.FindProperty("tags");
            if (tags == null) return;

            var existing = new HashSet<string>();
            for (int i = 0; i < tags.arraySize; i++) existing.Add(tags.GetArrayElementAtIndex(i).stringValue);

            bool changed = false;
            foreach (var t in RequiredTags)
            {
                if (existing.Contains(t)) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = t;
                changed = true;
            }
            if (changed)
            {
                so.ApplyModifiedProperties();
                if (verbose) Debug.Log("[Sandbox] Теги проекта обновлены.");
            }
        }

        public static void EnsureAlwaysIncludedShaders(bool verbose)
        {
            var asset = LoadProjectSettingsAsset("ProjectSettings/GraphicsSettings.asset");
            if (asset == null) return;

            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            var existing = new HashSet<UnityEngine.Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var v = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (v != null) existing.Add(v);
            }

            bool changed = false;
            foreach (var shaderName in RequiredShaders)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) continue;
                if (existing.Contains(shader)) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
                if (verbose) Debug.Log($"[Sandbox] Шейдер добавлен в Always Included: {shaderName}");
            }

            // убираем пустые/битые записи
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    list.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// Настройки Android: горизонт, IL2CPP + ARM64, minSdk 24 (Android 7.0),
        /// GLES3 + Vulkan, офлайн (без разрешения INTERNET), линейное цветовое пространство.
        /// </summary>
        public static void ApplyPlayerSettings(bool verbose)
        {
            const BuildTargetGroup group = BuildTargetGroup.Android;

            bool changed = false;

            if (PlayerSettings.companyName != CompanyName) { PlayerSettings.companyName = CompanyName; changed = true; }
            if (PlayerSettings.productName != ProductName) { PlayerSettings.productName = ProductName; changed = true; }

            string appId = PlayerSettings.GetApplicationIdentifier(group);
            if (appId != BundleId)
            {
                PlayerSettings.SetApplicationIdentifier(group, BundleId);
                changed = true;
            }

            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.LandscapeLeft)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                changed = true;
            }
            if (!PlayerSettings.allowedAutorotateToLandscapeLeft) { PlayerSettings.allowedAutorotateToLandscapeLeft = true; changed = true; }
            if (!PlayerSettings.allowedAutorotateToLandscapeRight) { PlayerSettings.allowedAutorotateToLandscapeRight = true; changed = true; }
            if (PlayerSettings.allowedAutorotateToPortrait) { PlayerSettings.allowedAutorotateToPortrait = false; changed = true; }
            if (PlayerSettings.allowedAutorotateToPortraitUpsideDown) { PlayerSettings.allowedAutorotateToPortraitUpsideDown = false; changed = true; }

            if (PlayerSettings.Android.minSdkVersion != AndroidSdkVersions.AndroidApiLevel24)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
                changed = true;
            }
            if (PlayerSettings.Android.targetSdkVersion != (AndroidSdkVersions)0)
            {
                PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)0; // Automatic (highest installed)
                changed = true;
            }

            var backend = PlayerSettings.GetScriptingBackend(group);
            if (backend != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
                changed = true;
            }

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                changed = true;
            }

            var apis = PlayerSettings.GetGraphicsAPIs(group);
            var wanted = new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan };
            if (apis == null || apis.Length != wanted.Length || !apis.SequenceEqual(wanted))
            {
                PlayerSettings.SetGraphicsAPIs(group, wanted);
                changed = true;
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                changed = true;
            }

            if (PlayerSettings.stripEngineCode != true) { PlayerSettings.stripEngineCode = true; changed = true; }

            var stripping = PlayerSettings.GetManagedStrippingLevel(group);
            if (stripping != ManagedStrippingLevel.Minimal)
            {
                PlayerSettings.SetManagedStrippingLevel(group, ManagedStrippingLevel.Minimal);
                changed = true;
            }

            if (PlayerSettings.Android.forceInternetPermission != false)
            {
                PlayerSettings.Android.forceInternetPermission = false;
                changed = true;
            }
            if (PlayerSettings.Android.forceSDCardPermission != false)
            {
                PlayerSettings.Android.forceSDCardPermission = false;
                changed = true;
            }
            if (PlayerSettings.Android.useLowAccuracyLocation != true)
            {
                PlayerSettings.Android.useLowAccuracyLocation = true;
                changed = true;
            }
            if (PlayerSettings.runInBackground != true) { PlayerSettings.runInBackground = true; changed = true; }
            if (PlayerSettings.MTRendering != true) { PlayerSettings.MTRendering = true; changed = true; }
            if (PlayerSettings.Android.bundleVersionCode < 1) { PlayerSettings.Android.bundleVersionCode = 1; changed = true; }
            if (PlayerSettings.accelerometerFrequency != AccelerometerFrequency.Disabled)
            {
                PlayerSettings.accelerometerFrequency = AccelerometerFrequency.Disabled;
                changed = true;
            }

            // Игра полностью офлайн: разрешение INTERNET не запрашиваем
            // (forceInternetPermission = false, никаких UnityWebRequest-вызовов в рантайме).

            if (changed)
            {
                AssetDatabase.SaveAssets();
                if (verbose)
                    Debug.Log($"[Sandbox] Android-настройки применены: {BundleId}, Landscape, IL2CPP/ARM64, minSdk 24, GLES3+Vulkan, Linear.");
            }
        }

        public static void EnsureBuildScenes(bool verbose)
        {
            if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScenePath)))
            {
                Debug.LogError($"[Sandbox] Сцена не найдена: {ScenePath}");
                return;
            }

            var scenes = EditorBuildSettings.scenes;
            bool ok = scenes != null && scenes.Length == 1 && scenes[0].path == ScenePath && scenes[0].enabled;
            if (ok) return;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            if (verbose) Debug.Log("[Sandbox] Сцена добавлена в список сцен сборки: " + ScenePath);
        }
    }
}
