using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Sandbox.EditorTools
{
    /// <summary>
    /// Точка входа для сборки Android. Используется и локально (меню
    /// Tools → Sandbox → Собрать APK), и в CI:
    ///   unity -batchmode -quit -executeMethod Sandbox.EditorTools.BuildRunner.BuildAndroid
    ///
    /// Каталог вывода берётся из переменной окружения BUILD_PATH или аргумента
    /// командной строки -buildPath, иначе Build/Android.
    /// </summary>
    public static class BuildRunner
    {
        public const string ApkFileName = "Sandbox.apk";
        public const string AabFileName = "Sandbox.aab";
        public const string DefaultOutputDir = "Build/Android";

        [MenuItem("Tools/Sandbox/5. Собрать APK (Android)", false, 50)]
        public static void BuildApkMenu()
        {
            BuildInternal(false);
            EditorUtility.DisplayDialog("Sandbox", "APK собран:\n" + LastOutputPath, "OK");
        }

        [MenuItem("Tools/Sandbox/6. Собрать AAB (Google Play)", false, 51)]
        public static void BuildAabMenu()
        {
            BuildInternal(true);
            EditorUtility.DisplayDialog("Sandbox", "AAB собран:\n" + LastOutputPath, "OK");
        }

        public static string LastOutputPath { get; private set; } = string.Empty;

        /// <summary>Вызывается из CI (-executeMethod). Завершает процесс с кодом 0/1.</summary>
        public static void BuildAndroid()
        {
            bool ok = BuildInternal(false, exitOnFinish: false);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void BuildAndroidAab()
        {
            bool ok = BuildInternal(true, exitOnFinish: false);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool BuildInternal(bool appBundle, bool exitOnFinish = false)
        {
            try
            {
                Debug.Log("[Sandbox] === Начало сборки Android ===");

                // 1. приводим проект в рабочее состояние (слои, URP, шейдеры, Android-настройки)
                ProjectConfigurator.Ensure(verbose: true);

                // 2. целевая платформа
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    Debug.Log("[Sandbox] Переключаемся на Android…");
                    bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                    if (!switched)
                    {
                        Debug.LogError("[Sandbox] Не удалось переключиться на Android. Установлен ли модуль Android Build Support?");
                        return false;
                    }
                }

                EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
                EditorUserBuildSettings.buildAppBundle = appBundle;
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.allowDebugging = false;

                string dir = ResolveOutputDir();
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, appBundle ? AabFileName : ApkFileName).Replace('\\', '/');

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ProjectConfigurator.ScenePath },
                    locationPathName = path,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.CompressWithLz4HC
                };

                Debug.Log($"[Sandbox] Сборка: {path} (IL2CPP/ARM64, Landscape, minSdk 24)");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                LastOutputPath = path;
                Debug.Log($"[Sandbox] Результат: {summary.result}, размер {summary.totalSize / (1024f * 1024f):F1} МБ, " +
                          $"время {summary.totalTime.TotalSeconds:F1} c, ошибок {summary.totalErrors}, предупреждений {summary.totalWarnings}");

                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("[Sandbox] Сборка не удалась — см. лог выше.");
                    if (exitOnFinish && Application.isBatchMode) EditorApplication.Exit(1);
                    return false;
                }

                Debug.Log("[Sandbox] APK готов: " + Path.GetFullPath(path));
                if (exitOnFinish && Application.isBatchMode) EditorApplication.Exit(0);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Sandbox] Исключение при сборке: " + e);
                if (exitOnFinish && Application.isBatchMode) EditorApplication.Exit(1);
                return false;
            }
        }

        public static string ResolveOutputDir()
        {
            string dir = Environment.GetEnvironmentVariable("BUILD_PATH");
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetEnvironmentVariable("UNITY_BUILD_PATH");

            if (string.IsNullOrEmpty(dir))
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-buildPath") { dir = args[i + 1]; break; }
                }
            }

            if (string.IsNullOrEmpty(dir)) dir = DefaultOutputDir;

            // относительный путь считаем от корня проекта
            if (!Path.IsPathRooted(dir))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                dir = Path.Combine(projectRoot, dir);
            }
            return dir.Replace('\\', '/');
        }
    }
}
