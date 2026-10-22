using System.Collections;
using System.Text;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Единственный объект в стартовой сцене: настраивает физику, строит полигон,
    /// игрока, руки, камеру, HUD и менеджеры. Всё содержимое создаётся кодом,
    /// поэтому в проекте нет бинарных ассетов и сцена не может «поехать».
    ///
    /// Порядок инициализации важен:
    ///   физика → менеджеры (включая OutOfBoundsRecovery) → мир → игрок → HUD → самодиагностика.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Состав сборки")]
        public bool buildWorld = true;
        public bool startWithPhoneInHand = true;
        public bool runSelfTest = true;
        public bool autoStartStressTest = false;
        public float selfTestDelay = 0.8f;

        [Header("Оптимизация")]
        public bool autoDetectPerfPreset = true;
        public SandboxConfig.PerfPreset forcedPreset = SandboxConfig.PerfPreset.Medium;

        public SandboxWorld World { get; private set; }
        public GameObject Player { get; private set; }
        public Camera PlayerCamera { get; private set; }
        public PlayerController Controller { get; private set; }
        public HudController Hud { get; private set; }
        public PhysicsStressTester StressTester { get; private set; }
        public SandboxSelfTest.Report LastReport { get; private set; }
        public bool Ready { get; private set; }

        /// <summary>Имя объекта с редакторским превью (см. Tools → Sandbox).</summary>
        public const string PreviewRootName = "__SandboxPreview";

        void Awake()
        {
            BuildAll();
        }

        /// <summary>
        /// Полное построение песочницы. Вызывается из Awake в Play-режиме
        /// и вручную из редакторского меню «Превью полигона».
        /// </summary>
        public void BuildAll()
        {
            // на всякий случай убираем редакторское превью, если оно пережило вход в Play-режим
            var stale = GameObject.Find(PreviewRootName);
            if (stale != null && stale != gameObject) Destroy(stale);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            ApplyRuntimeSettings();
            ApplyPhysics();

            // редакторная камера-заглушка из сцены больше не нужна:
            // сначала гасим (чтобы не конфликтовала с Camera.main), потом удаляем
            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (c == null || c.gameObject == null) continue;
                c.enabled = false;
                c.gameObject.SetActive(false);
                Destroy(c.gameObject);
            }

            var managers = GameUtil.New("Managers", transform);

            var perf = managers.AddComponent<PerformanceTuner>();
            perf.autoDetect = autoDetectPerfPreset;
            perf.forcedPreset = forcedPreset;
            perf.ApplyAuto();

            managers.AddComponent<FpsMeter>();
            managers.AddComponent<AudioPool>();
            managers.AddComponent<FxPool>();
            managers.AddComponent<OutOfBoundsRecovery>();
            StressTester = managers.AddComponent<PhysicsStressTester>();

            var input = managers.AddComponent<InputService>();
            input.lookSensitivity = SandboxConfig.LookSensitivity;

            MaterialFactory.Init();

            if (buildWorld)
            {
                var worldRoot = GameUtil.New("World", transform);
                World = SandboxWorldBuilder.Build(worldRoot.transform, true);
            }

            Player = BuildPlayer(input);

            Hud = HudController.Create(transform, input);
            if (Controller != null) Controller.hud = Hud;

            if (World != null)
            {
                StressTester.Props = World.Props;
                StressTester.Dummies = World.Dummies;
            }

            if (World != null && World.Phone != null && startWithPhoneInHand)
            {
                var interactor = Player.GetComponent<Interactor>();
                if (interactor != null)
                {
                    interactor.phone = World.Phone;
                    interactor.PlacePhoneInHand();
                }
            }

            Physics.SyncTransforms();
            sw.Stop();
            Ready = true;

            LogStartupBanner(sw.Elapsed.TotalMilliseconds);

            if (runSelfTest) StartCoroutine(RunSelfTestRoutine());
            if (autoStartStressTest && StressTester != null) StressTester.StartTest();
        }

        // ------------------------------------------------------------------
        void ApplyRuntimeSettings()
        {
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // горизонтальная ориентация дополнительно фиксируется в рантайме
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            if (Screen.orientation != ScreenOrientation.LandscapeLeft &&
                Screen.orientation != ScreenOrientation.LandscapeRight)
            {
                Screen.orientation = ScreenOrientation.AutoRotation;
            }

            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.All;
        }

        void ApplyPhysics()
        {
            Time.fixedDeltaTime = SandboxConfig.FixedDeltaTime;
            Time.maximumDeltaTime = 0.1f;

            Physics.gravity = new Vector3(0f, SandboxConfig.Gravity, 0f);
            Physics.defaultSolverIterations = Mathf.Max(1, (int)SandboxConfig.SolverIterations);
            Physics.defaultSolverVelocityIterations = Mathf.Max(1, (int)SandboxConfig.SolverVelocityIterations);
            Physics.sleepThreshold = SandboxConfig.SleepThreshold;
            Physics.defaultContactOffset = SandboxConfig.ContactOffset;
            Physics.bounceThreshold = SandboxConfig.BounceThreshold;
            Physics.defaultMaxDepenetrationVelocity = SandboxConfig.MaxDepenetrationVelocity;
            Physics.defaultMaxAngularSpeed = SandboxConfig.MaxAngularVelocity;
            Physics.autoSyncTransforms = false;
            Physics.reuseCollisionCallbacks = true;
            Physics.queriesHitTriggers = false;
            Physics.queriesStartInColliders = true;
            Physics.autoSimulation = true;

            Layers.ApplyCollisionRules();
            // осколки стекла не должны толкаться друг с другом (экономим физику)
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Debris, true);
        }

        // ------------------------------------------------------------------
        GameObject BuildPlayer(InputService input)
        {
            Vector3 spawn = World != null ? World.PlayerSpawn : new Vector3(0f, 0f, -6f);
            Quaternion spawnRot = World != null ? World.PlayerSpawnRotation : Quaternion.identity;

            var player = GameUtil.New("Player", transform);
            player.transform.position = spawn;
            player.transform.rotation = spawnRot;
            player.layer = Layers.Player;
            Player = player;

            var motor = player.AddComponent<PlayerMotor>();
            var health = player.AddComponent<PlayerHealth>();
            var body = player.AddComponent<PlayerBody>();
            var grab = player.AddComponent<GrabController>();

            // --- камера ---
            var head = GameUtil.New("Head", player.transform);
            head.transform.localPosition = new Vector3(0f, SandboxConfig.EyeHeight, 0f);
            head.transform.localRotation = Quaternion.identity;
            head.layer = Layers.Player;

            var offset = GameUtil.New("ViewOffset", head.transform);
            offset.transform.localPosition = Vector3.zero;
            offset.transform.localRotation = Quaternion.identity;
            offset.layer = Layers.Player;

            var camGo = GameUtil.New("PlayerCamera", offset.transform);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;
            camGo.layer = Layers.Player;
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = SandboxConfig.CameraFov;
            cam.nearClipPlane = SandboxConfig.NearClip;
            cam.farClipPlane = SandboxConfig.FarClip;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depth = 0;
            camGo.AddComponent<AudioListener>();
            try { camGo.tag = "MainCamera"; }
            catch (System.Exception) { /* тег может отсутствовать — не критично */ }
            PlayerCamera = cam;

            // --- руки ---
            var handsGo = GameUtil.New("Hands", camGo.transform);
            var hands = handsGo.AddComponent<PlayerHands>();
            hands.skinMaterial = MaterialFactory.Skin;
            hands.sleeveMaterial = MaterialFactory.Rubber;
            hands.Build(cam);

            // --- настройки компонентов ---
            var view = head.AddComponent<PlayerView>();
            view.yawRoot = player.transform;
            view.offsetRoot = offset.transform;
            view.cam = cam;
            view.sensitivity = SandboxConfig.LookSensitivity;
            // синхронизируем углы камеры с направлением спавна
            view.SetAngles(player.transform.eulerAngles.y, 0f);

            grab.holdAnchor = hands.holdAnchor;
            grab.playerCollider = motor.Controller;

            body.motor = motor;
            body.view = view;
            body.hands = hands;
            body.head = head.transform;
            body.skinMaterial = MaterialFactory.Skin;
            body.clothMaterial = MaterialFactory.Rubber;
            body.Build();
            body.SpawnPosition = spawn;
            body.SpawnRotation = spawnRot;

            health.body = body;
            health.view = view;

            // --- хитбокс (урон от прилетающих предметов) ---
            var hurt = GameUtil.New("HurtBox", player.transform);
            hurt.transform.localPosition = new Vector3(0f, SandboxConfig.PlayerHeight * 0.52f, 0f);
            var sphere = hurt.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.56f;
            sphere.center = Vector3.zero;
            hurt.layer = Layers.Player;
            var hurtBox = hurt.AddComponent<PlayerHurtBox>();
            hurtBox.health = health;

            // --- взаимодействие ---
            var interactor = player.AddComponent<Interactor>();
            interactor.cam = cam;
            interactor.grab = grab;
            interactor.hands = hands;
            interactor.view = view;
            interactor.body = body;
            interactor.mask = Layers.InteractableMask;
            if (World != null) interactor.phone = World.Phone;

            // --- ввод ---
            input.lookSensitivity = SandboxConfig.LookSensitivity;

            // --- оркестратор ---
            var controller = player.AddComponent<PlayerController>();
            controller.input = input;
            controller.motor = motor;
            controller.view = view;
            controller.hands = hands;
            controller.body = body;
            controller.health = health;
            controller.interactor = interactor;
            controller.grab = grab;
            controller.hud = Hud;
            controller.stressTester = StressTester;
            Controller = controller;

            // --- события ---
            motor.OnLanded += view.OnLand;
            motor.OnFootstep += speed =>
            {
                AudioPool.PlayClip(ProceduralAudio.Footstep,
                    player.transform.position + Vector3.up * 0.1f,
                    0.20f + speed * 0.30f,
                    0.92f + speed * 0.24f);
            };
            grab.OnThrown += (item, speed) =>
            {
                view.AddTrauma(0.06f + Mathf.Clamp01(speed / 22f) * 0.14f);
                if (Hud != null) Hud.ShowMessage($"{item.displayName}: бросок {speed * 3.6f:F0} км/ч", 1.6f);
            };
            health.OnDamaged += (amount, info) =>
            {
                if (Hud != null) Hud.ShowMessage($"Урон {amount:F0} от {DescribeSource(info.source)}", 1.8f);
            };
            body.OnFallen += () =>
            {
                if (Hud != null) Hud.ShowMessage("Ты упал — нажми «Действие», чтобы встать", 3.5f);
            };

            return player;
        }

        static string DescribeSource(GameObject src)
        {
            if (src == null) return "падения";
            var g = src.GetComponentInParent<Grabbable>();
            return g != null ? g.displayName : src.name;
        }

        // ------------------------------------------------------------------
        IEnumerator RunSelfTestRoutine()
        {
            yield return new WaitForSecondsRealtime(selfTestDelay);

            if (Controller != null) Controller.hud = Hud;

            var hands = Player != null ? Player.GetComponentInChildren<PlayerHands>() : null;
            var grab = Player != null ? Player.GetComponent<GrabController>() : null;
            LastReport = SandboxSelfTest.Run(World, Player, PlayerCamera, hands, grab);

            if (Hud != null)
            {
                Hud.ShowMessage(LastReport.Ok
                    ? $"Самодиагностика: PASS ({LastReport.Passed.Count} проверок)"
                    : $"Самодиагностика: FAIL ({LastReport.Failed.Count}) — см. лог", 5f);
                Hud.SetPrompt(string.Empty, InteractionKind.None);
            }
        }

        void LogStartupBanner(double buildMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=====================================================");
            sb.AppendLine(" SANDBOX FPS — тестовый полигон физики (URP, Android)");
            sb.AppendLine("=====================================================");
            sb.AppendLine($" Устройство : {SystemInfo.deviceModel} / {SystemInfo.graphicsDeviceName}");
            sb.AppendLine($" Память     : {SystemInfo.systemMemorySize} МБ, ядер: {SystemInfo.processorCount}");
            sb.AppendLine($" Пресет     : {(PerformanceTuner.Instance != null ? PerformanceTuner.Instance.Preset.ToString() : "?")} " +
                          $"(renderScale {(PerformanceTuner.Instance != null ? PerformanceTuner.Instance.CurrentScale : 1f):F2})");
            sb.AppendLine($" Конвейер   : {(MaterialFactory.IsUrp ? "URP" : "built-in (запасной)")} / {MaterialFactory.Lit}");
            sb.AppendLine($" Физика     : fixedDt {Time.fixedDeltaTime:F3} c, gravity {Physics.gravity.y:F2}, " +
                          $"solver {Physics.defaultSolverIterations}");
            sb.AppendLine($" Бросок     : max {SandboxConfig.MaxThrowSpeed:F0} м/с → {SandboxConfig.MaxTravelPerStep:F3} м/шаг " +
                          $"при толщине пола {SandboxConfig.FloorThickness:F2} м (запас x{SandboxConfig.FloorSafetyMargin:F1})");
            if (World != null)
            {
                sb.AppendLine($" Полигон    : {World.ArenaDescription()}");
            }
            sb.AppendLine($" Построение : {buildMs:F0} мс");
            sb.AppendLine(" Управление : джойстик слева, свайп справа, кнопка «Действие».");
            sb.AppendLine("              ПК: WASD + мышь, ЛКМ/E — действие, Space — прыжок,");
            sb.AppendLine("              Q — выпустить, F — телефон, R — респаун, T — стресс-тест.");
            sb.AppendLine("=====================================================");
            Debug.Log(sb.ToString());
        }
    }

    public static class SandboxWorldExtensions
    {
        public static string ArenaDescription(this SandboxWorld w)
        {
            if (w == null) return "нет";
            return $"{w.Props.Count} предметов, {w.Dummies.Count} манекена(ов), " +
                   $"{w.RigidbodyCount} динамических тел, {w.StaticColliderCount} статичных коллайдеров" +
                   (w.Phone != null ? ", смартфон" : "");
        }
    }
}
