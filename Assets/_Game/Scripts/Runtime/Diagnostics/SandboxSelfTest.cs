using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Самодиагностика полигона: проверяет, что карта закрытая, у всех предметов
    /// есть физика, ничего не проваливается сквозь пол, рэгдоллы собраны, а руки
    /// и точка удержания реально видны в кадре.
    /// Запускается автоматически при старте и из меню Tools → Sandbox.
    /// </summary>
    public static class SandboxSelfTest
    {
        public class Report
        {
            public readonly List<string> Passed = new List<string>();
            public readonly List<string> Failed = new List<string>();
            public readonly List<string> Notes = new List<string>();

            public void Pass(string check, string detail = "")
            {
                Passed.Add(check + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            }

            public void Fail(string check, string detail = "")
            {
                Failed.Add(check + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            }

            public void Note(string text) { Notes.Add(text); }

            public bool Ok => Failed.Count == 0;

            public string Text()
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== САМОДИАГНОСТИКА ПОЛИГОНА ===");
                sb.AppendLine($"Пройдено: {Passed.Count}, провалено: {Failed.Count}");
                foreach (var f in Failed) sb.AppendLine("  [FAIL] " + f);
                foreach (var p in Passed) sb.AppendLine("  [ OK ] " + p);
                foreach (var n in Notes) sb.AppendLine("  [info] " + n);
                sb.AppendLine(Ok ? "РЕЗУЛЬТАТ: PASS" : "РЕЗУЛЬТАТ: FAIL");
                return sb.ToString();
            }
        }

        public static Report Run(SandboxWorld world, GameObject player, Camera cam, PlayerHands hands,
            GrabController grab)
        {
            var r = new Report();

            // ---- 1. настройки физики ----
            Check("Гравитация", Mathf.Approximately(Physics.gravity.y, SandboxConfig.Gravity),
                $"y={Physics.gravity.y:F2}", r);
            Check("Шаг физики", Mathf.Approximately(Time.fixedDeltaTime, SandboxConfig.FixedDeltaTime),
                $"{Time.fixedDeltaTime:F3} c ({1f / Time.fixedDeltaTime:F0} Гц)", r);
            r.Note($"Solver iterations: {Physics.defaultSolverIterations}, sleep threshold: {Physics.sleepThreshold:F4}");

            // ---- 2. запас от протыкания пола ----
            float step = SandboxConfig.MaxTravelPerStep;
            float margin = SandboxConfig.FloorSafetyMargin;
            Check("Запас от туннелирования", margin > 2f,
                $"за шаг тело проходит {step:F3} м при толщине пола {SandboxConfig.FloorThickness:F2} м (запас x{margin:F1}); " +
                $"пробить плиту можно лишь на скорости > {SandboxConfig.TunnelingSpeed:F0} м/с", r);

            // ---- 3. пол ----
            Collider floorCol = null;
            if (world != null && world.Floor != null) floorCol = world.Floor.GetComponent<Collider>();
            if (floorCol == null)
            {
                r.Fail("Пол", "коллайдер пола не найден");
            }
            else
            {
                Check("Пол: верх на нужной высоте", Mathf.Abs(floorCol.bounds.max.y - SandboxConfig.FloorTopY) < 0.02f,
                    $"y={floorCol.bounds.max.y:F3}", r);
                Check("Пол: толщина", floorCol.bounds.size.y >= SandboxConfig.FloorThickness - 0.01f,
                    $"{floorCol.bounds.size.y:F2} м", r);
                Check("Пол: статичный", floorCol.attachedRigidbody == null, "без Rigidbody", r);
            }

            // ---- 4. закрытость полигона (6 лучей из центра должны попасть в World) ----
            if (cam != null)
            {
                Vector3 c = new Vector3(0f, 1.5f, 0f);
                Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
                int closed = 0;
                foreach (var d in dirs)
                {
                    if (Physics.Raycast(c, d, 60f, 1 << Layers.World, QueryTriggerInteraction.Ignore)) closed++;
                }
                Check("Полигон закрыт", closed == dirs.Length, $"{closed}/{dirs.Length} направлений перекрыты", r);
            }

            // ---- 5. предметы ----
            int props = 0, withRb = 0, withCollider = 0, insideFloor = 0, belowFloor = 0;
            float lowest = float.MaxValue;
            if (world != null)
            {
                foreach (var g in world.Props)
                {
                    if (g == null) continue;
                    props++;
                    if (g.Body != null) withRb++;
                    var cols = g.GetComponentsInChildren<Collider>();
                    if (cols.Length > 0) withCollider++;

                    float minY = float.MaxValue;
                    foreach (var col in cols)
                    {
                        minY = Mathf.Min(minY, col.bounds.min.y);
                        if (floorCol != null)
                        {
                            Vector3 dir; float dist;
                            if (Physics.ComputePenetration(col, col.transform.position, col.transform.rotation,
                                    floorCol, floorCol.transform.position, floorCol.transform.rotation, out dir, out dist))
                                insideFloor++;
                        }
                    }
                    if (minY < float.MaxValue)
                    {
                        lowest = Mathf.Min(lowest, minY);
                        if (minY < SandboxConfig.FloorTopY - 0.05f) belowFloor++;
                    }
                }
            }
            Check("У всех предметов есть Rigidbody", withRb == props, $"{withRb}/{props}", r);
            Check("У всех предметов есть коллайдер", withCollider == props, $"{withCollider}/{props}", r);
            Check("Ни один предмет не внутри пола", insideFloor == 0, $"пересечений: {insideFloor}", r);
            Check("Ни один предмет не ниже пола", belowFloor == 0,
                $"самая низкая точка: {(lowest == float.MaxValue ? 0f : lowest):F3} м", r);

            // ---- 6. манекены ----
            if (world != null)
            {
                Check("Манекены на карте", world.Dummies.Count >= 2, $"{world.Dummies.Count} шт.", r);
                foreach (var d in world.Dummies)
                {
                    if (d == null || d.ragdoll == null) { r.Fail("Рэгдолл манекена", d != null ? d.displayName : "null"); continue; }
                    var rag = d.ragdoll;
                    Check($"Рэгдолл «{d.displayName}»", rag.Bones.Length == 15 && rag.Bodies.Length == 15 && rag.Joints.Length == 14,
                        rag.Describe(), r);
                    Check($"«{d.displayName}» стоит на полу", rag.LowestPoint() > SandboxConfig.FloorTopY - 0.05f,
                        $"низ: {rag.LowestPoint():F3} м", r);
                }
            }

            // ---- 7. слои ----
            Check("Слой Prop", LayerMask.NameToLayer("Prop") == 10, "index 10", r);
            Check("Слой Dummy", LayerMask.NameToLayer("Dummy") == 9, "index 9", r);
            Check("Слой World", LayerMask.NameToLayer("World") == 11, "index 11", r);
            Check("Слой Held", LayerMask.NameToLayer("Held") == 12, "index 12", r);

            // ---- 8. рендер ----
            Check("Шейдер найден", MaterialFactory.Lit != null,
                MaterialFactory.IsUrp ? "Universal Render Pipeline/Lit" : "встроенный (запасной вариант)", r);
            if (cam != null)
            {
                Check("Камера", cam.isActiveAndEnabled,
                    $"FOV {cam.fieldOfView:F0}, near {cam.nearClipPlane:F2}, far {cam.farClipPlane:F0}", r);
                Check("AudioListener", cam.GetComponent<AudioListener>() != null, "звук привязан к камере", r);
            }

            // ---- 9. руки и точка удержания видны в кадре ----
            if (cam != null && hands != null && hands.holdAnchor != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(hands.holdAnchor.position);
                Check("Точка удержания в кадре", vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f,
                    $"viewport ({vp.x:F2}, {vp.y:F2}) z={vp.z:F2}", r);

                int handRenderers = 0;
                foreach (var mr in hands.GetComponentsInChildren<MeshRenderer>())
                {
                    Vector3 p = cam.WorldToViewportPoint(mr.transform.position);
                    if (p.z > 0f && p.x > -0.1f && p.x < 1.1f && p.y > -0.1f && p.y < 1.1f) handRenderers++;
                }
                Check("Руки видны внизу экрана", handRenderers >= 4, $"рендереров в кадре: {handRenderers}", r);
            }

            // ---- 10. игрок ----
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                Check("CharacterController", cc != null && cc.enabled,
                    cc != null ? $"h={cc.height:F2} r={cc.radius:F2}" : "", r);
                Check("Игрок стоит на полу", player.transform.position.y > SandboxConfig.FloorTopY - 0.02f,
                    $"y={player.transform.position.y:F3}", r);
            }

            // ---- 11. телефон ----
            if (world != null && world.Phone != null)
            {
                Check("Смартфон создан", world.Phone.Grabbable != null && world.Phone.Grabbable.Body != null,
                    $"масса {world.Phone.Grabbable.Body.mass:F2} кг", r);
            }
            if (grab != null)
            {
                r.Note(grab.Held != null ? "В руке: " + grab.Held.displayName : "Руки пусты");
            }

            r.Note($"Статичных коллайдеров: {(world != null ? world.StaticColliderCount : 0)}, " +
                   $"динамических тел: {(world != null ? world.RigidbodyCount : 0)}, " +
                   $"осколков: {Breakable.LiveShardCount}");

            Debug.Log(r.Text());
            if (!r.Ok) Debug.LogError("[Sandbox] Самодиагностика нашла проблемы — см. лог выше.");
            return r;
        }

        static void Check(string name, bool condition, string detail, Report r)
        {
            if (condition) r.Pass(name, detail);
            else r.Fail(name, detail);
        }
    }

    /// <summary>
    /// Стресс-тест физики прямо на устройстве: N секунд толкает все предметы
    /// случайными импульсами и следит, чтобы никто не провалился сквозь пол.
    /// Кнопка «СТРЕСС-ТЕСТ» в HUD.
    /// </summary>
    public class PhysicsStressTester : MonoBehaviour
    {
        public List<Grabbable> Props = new List<Grabbable>();
        public List<DamageableDummy> Dummies = new List<DamageableDummy>();

        public float duration = 14f;
        public float impulseInterval = 0.30f;
        [Tooltip("Разгон предмета, м/с. Импульс считается как m·v, поэтому "
                 + "результат не зависит от массы и никогда не приближается к порогу пробоя пола (50 м/с).")]
        public float minKickSpeed = 3f;
        public float maxKickSpeed = 22f;
        public int impulsesPerTick = 3;

        public bool IsRunning { get; private set; }
        public string StatusText { get; private set; } = "ожидание";
        public int ImpulsesApplied { get; private set; }
        public int Violations { get; private set; }
        public int RecoveriesAtStart { get; private set; }

        float m_Timer;
        float m_Elapsed;
        int m_StartRecoveries;

        public void Toggle()
        {
            if (IsRunning) Stop(true);
            else StartTest();
        }

        public void StartTest()
        {
            IsRunning = true;
            m_Elapsed = 0f;
            m_Timer = 0f;
            ImpulsesApplied = 0;
            Violations = 0;
            m_StartRecoveries = OutOfBoundsRecovery.Instance != null ? OutOfBoundsRecovery.Instance.Recoveries : 0;
            RecoveriesAtStart = 0;
            StatusText = "идёт…";
            Debug.Log("[Sandbox] Стресс-тест физики запущен.");
        }

        public void Stop(bool report)
        {
            IsRunning = false;
            if (!report) return;

            int recoveries = OutOfBoundsRecovery.Instance != null
                ? OutOfBoundsRecovery.Instance.Recoveries - m_StartRecoveries
                : 0;
            bool pass = Violations == 0 && recoveries == 0;
            StatusText = pass
                ? $"PASS: импульсов {ImpulsesApplied}, провалов 0, восстановлений 0"
                : $"FAIL: импульсов {ImpulsesApplied}, провалов {Violations}, восстановлений {recoveries}";
            Debug.Log("[Sandbox] Стресс-тест завершён: " + StatusText);
            if (!pass) Debug.LogError("[Sandbox] Стресс-тест провален: " + StatusText);
        }

        void Update()
        {
            if (!IsRunning) return;

            m_Elapsed += Time.deltaTime;
            m_Timer -= Time.deltaTime;

            if (m_Timer <= 0f)
            {
                m_Timer = impulseInterval;
                for (int i = 0; i < impulsesPerTick; i++) KickRandom();
            }

            CheckIntegrity();

            if (m_Elapsed >= duration) Stop(true);
            else StatusText = $"{m_Elapsed:F0}/{duration:F0} c, импульсов {ImpulsesApplied}, провалов {Violations}";
        }

        void KickRandom()
        {
            if (Props.Count == 0) return;
            var prop = Props[Random.Range(0, Props.Count)];
            if (prop == null || prop.Body == null || prop.IsHeld) return;

            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.6f + 0.15f;

            // задаём СКОРОСТЬ, а не «слепой» импульс: бутылку (0.42 кг) и бочку
            // (19 кг) пинает одинаково сильно по результату, и ни одно тело
            // не разгоняется выше порога, на котором возможен пробой пола.
            float speed = Random.Range(minKickSpeed, maxKickSpeed);
            speed = Mathf.Min(speed, SandboxConfig.MaxSafeSpeed * 0.9f);
            float mass = Mathf.Max(0.05f, prop.Body.mass);
            float magnitude = speed * mass;

            prop.Body.WakeUp();
            prop.Body.AddForce(dir.normalized * magnitude, ForceMode.Impulse);
            prop.Body.AddTorque(Random.insideUnitSphere * magnitude * 0.06f, ForceMode.Impulse);
            ImpulsesApplied++;
        }

        void CheckIntegrity()
        {
            foreach (var prop in Props)
            {
                if (prop == null || prop.IsHeld) continue;
                var cols = prop.GetComponentsInChildren<Collider>();
                foreach (var c in cols)
                {
                    if (c.bounds.min.y < SandboxConfig.FloorTopY - 0.30f)
                    {
                        Violations++;
                        Debug.LogError($"[Sandbox] Провал! «{prop.displayName}» ушёл ниже пола: y={c.bounds.min.y:F2}");
                        prop.ResetToSpawn();
                        break;
                    }
                }
            }

            foreach (var d in Dummies)
            {
                if (d == null) continue;
                if (d.LowestPoint() < SandboxConfig.FloorTopY - 0.30f)
                {
                    Violations++;
                    d.Recover();
                }
            }
        }

        /// <summary>Полный сброс полигона: предметы на места, манекены встают.</summary>
        public void ResetAll()
        {
            foreach (var p in Props)
            {
                if (p == null) continue;
                p.ResetToSpawn();
            }
            foreach (var d in Dummies)
            {
                if (d == null) continue;
                d.Recover();
            }
            Breakable.TrimShards();
            if (OutOfBoundsRecovery.Instance != null) OutOfBoundsRecovery.Instance.ClearCounter();
            StatusText = "полигон сброшен";
            Debug.Log("[Sandbox] Полигон сброшен в исходное состояние.");
        }
    }
}
