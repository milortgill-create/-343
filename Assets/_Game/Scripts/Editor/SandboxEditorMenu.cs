using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sandbox.EditorTools
{
    /// <summary>
    /// Инструменты разработчика песочницы.
    ///
    /// «Превью полигона» строит всю карту прямо в редакторе (без входа в Play-режим),
    /// чтобы её можно было осмотреть и поправить свет/расстановку. Объекты создаются
    /// с HideFlags.DontSave, поэтому в сцену случайно не сохраняются и не конфликтуют
    /// с рантайм-построением.
    /// </summary>
    public static class SandboxEditorMenu
    {
        [MenuItem("Tools/Sandbox/2. Превью полигона в редакторе", false, 20)]
        public static void BuildPreview()
        {
            ClearPreview();

            var go = new GameObject(GameBootstrap.PreviewRootName);
            go.hideFlags = HideFlags.DontSave;

            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.hideFlags = HideFlags.DontSave;
            bootstrap.runSelfTest = false;
            bootstrap.autoStartStressTest = false;
            bootstrap.BuildAll();

            // помечаем всё дерево, чтобы Unity не пытался это сохранить
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.DontSave;
            }

            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("[Sandbox] Превью полигона построено в редакторе. Удалить: Tools → Sandbox → 3.");
        }

        [MenuItem("Tools/Sandbox/3. Удалить превью", false, 21)]
        public static void ClearPreview()
        {
            var existing = GameObject.Find(GameBootstrap.PreviewRootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
                Debug.Log("[Sandbox] Превью удалено.");
            }
        }

        [MenuItem("Tools/Sandbox/4. Самодиагностика и стресс-тест (Play Mode)", false, 22)]
        public static void RunTestsInPlayMode()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Sandbox",
                    "Самодиагностика и стресс-тест работают в Play-режиме.\nНажмите Play, затем этот пункт ещё раз.", "OK");
                return;
            }

            var bootstrap = Object.FindObjectOfType<GameBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[Sandbox] GameBootstrap не найден в сцене.");
                return;
            }

            var hands = bootstrap.Player != null ? bootstrap.Player.GetComponentInChildren<PlayerHands>() : null;
            var grab = bootstrap.Player != null ? bootstrap.Player.GetComponent<GrabController>() : null;
            var report = SandboxSelfTest.Run(bootstrap.World, bootstrap.Player, bootstrap.PlayerCamera, hands, grab);

            var tester = bootstrap.StressTester;
            if (tester != null && !tester.IsRunning) tester.StartTest();

            EditorUtility.DisplayDialog("Sandbox",
                (report.Ok ? "Самодиагностика: PASS" : "Самодиагностика: FAIL") +
                $"\n\nПроверок пройдено: {report.Passed.Count}\nПровалено: {report.Failed.Count}\n\n" +
                "Стресс-тест физики запущен — результат появится в Console через 14 секунд.",
                "OK");
        }

        [MenuItem("Tools/Sandbox/7. Сохранить сцену с превью как Sandbox_Baked", false, 23)]
        public static void SaveBakedScene()
        {
            var preview = GameObject.Find(GameBootstrap.PreviewRootName);
            if (preview == null)
            {
                BuildPreview();
                preview = GameObject.Find(GameBootstrap.PreviewRootName);
            }
            if (preview == null)
            {
                Debug.LogError("[Sandbox] Не удалось построить превью для запекания.");
                return;
            }

            // снимаем DontSave, иначе объекты не попадут в сцену
            foreach (var t in preview.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.None;
            }
            preview.hideFlags = HideFlags.None;

            var bootstrap = preview.GetComponent<GameBootstrap>();
            if (bootstrap != null)
            {
                // в запечённой сцене мир уже построен — отключаем повторное построение
                bootstrap.buildWorld = false;
                bootstrap.enabled = false;
            }

            string path = "Assets/_Game/Scenes/Sandbox_Baked.unity";
            var scene = SceneManager.GetActiveScene();
            bool saved = EditorSceneManager.SaveScene(scene, path, true);
            Debug.Log(saved
                ? $"[Sandbox] Запечённая сцена сохранена: {path}\n(основная сцена — Sandbox.unity, запечённая нужна только для ручных правок)"
                : "[Sandbox] Не удалось сохранить запечённую сцену.");
        }

        [MenuItem("Tools/Sandbox/Открыть основную сцену (Sandbox)", false, 40)]
        public static void OpenMainScene()
        {
            ClearPreview();
            var scene = EditorSceneManager.OpenScene(ProjectConfigurator.ScenePath, OpenSceneMode.Single);
            Debug.Log("[Sandbox] Открыта сцена: " + scene.path);
        }
    }
}
