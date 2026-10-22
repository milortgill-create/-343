using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Все числовые настройки песочницы в одном месте.
    /// Эти же константы читаются тестовым стендом Tools/physics_check.py
    /// (он парсит файл и проверяет, что предметы физически не могут
    /// протуннелировать сквозь пол при максимальной скорости броска).
    /// </summary>
    public static class SandboxConfig
    {
        // ---------------------------------------------------------------
        // Арена (закрытый полигон)
        // ---------------------------------------------------------------
        public const float ArenaWidth = 26f;      // X
        public const float ArenaDepth = 20f;      // Z
        public const float ArenaHeight = 7.0f;    // Y (высота потолка)
        public const float FloorThickness = 1.0f; // ТОЛЩИНА коллайдера пола (м)
        public const float WallThickness = 0.6f;  // толщина стен
        public const float FloorTopY = 0f;        // верх пола = 0
        public const float KillY = -2.0f;         // ниже этой отметки предмет считается "провалившимся"

        // ---------------------------------------------------------------
        // Физика
        // ---------------------------------------------------------------
        public const float FixedDeltaTime = 0.02f;   // 50 Гц — стабильно и дёшево для A53
        public const float Gravity = -9.81f;
        public const float MaxThrowSpeed = 18f;      // жёсткий потолок скорости броска, м/с
        public const float BaseThrowSpeed = 11f;     // базовая скорость броска, м/с
        public const float ThrowSpeedPerHandVel = 0.45f; // добавка от скорости руки
        /// <summary>
        /// Жёсткий потолок скорости любого динамического тела (м/с).
        /// Порог пробоя плиты пола = FloorThickness / FixedDeltaTime = 50 м/с:
        /// быстрее этого тело за один такт проходит сквозь метр бетона.
        /// 40 м/с — запас 20 %, который не достижим штатной игрой
        /// (потолок броска 18 м/с), но отсечёт любой баг или читерский импульс.
        /// Контролируется OutOfBoundsRecovery.Sweep() каждые 0.5 с.
        /// </summary>
        public const float MaxSafeSpeed = 40f;

        public const float MaxAngularVelocity = 12f;
        public const float SolverIterations = 6f;
        public const float SolverVelocityIterations = 1f;
        public const float SleepThreshold = 0.005f;
        public const float ContactOffset = 0.015f;
        public const float BounceThreshold = 1.5f;
        public const float MaxDepenetrationVelocity = 8f;

        /// <summary>Смещение по лучу за 1 шаг физики при максимальной скорости броска.</summary>
        public static float MaxTravelPerStep => MaxThrowSpeed * FixedDeltaTime;

        /// <summary>Запас прочности пола: во сколько раз плита толще шага движения.</summary>
        public static float FloorSafetyMargin => FloorThickness / MaxTravelPerStep;

        /// <summary>Скорость, при которой тело теоретически могло бы протуннелировать сквозь пол.</summary>
        public static float TunnelingSpeed => FloorThickness / FixedDeltaTime;

        // ---------------------------------------------------------------
        // Игрок
        // ---------------------------------------------------------------
        public const float EyeHeight = 1.68f;
        public const float PlayerRadius = 0.34f;
        public const float PlayerHeight = 1.8f;
        public const float WalkSpeed = 2.7f;
        public const float RunSpeed = 4.8f;
        public const float MoveAcceleration = 14f;
        public const float AirControl = 0.35f;
        public const float JumpSpeed = 4.6f;
        public const float PlayerMass = 78f;
        public const float PlayerMaxHealth = 100f;
        public const float RagdollThreshold = 55f;   // урон, после которого игрок падает в рэгдолл

        // ---------------------------------------------------------------
        // Камера
        // ---------------------------------------------------------------
        public const float CameraFov = 66f;
        public const float NearClip = 0.05f;
        public const float FarClip = 120f;
        public const float LookSensitivity = 0.16f;  // градусы на пиксель свайпа
        public const float MaxPitch = 86f;
        public const float HeadBobAmount = 0.055f;   // метры
        public const float HeadBobFrequency = 8.5f;  // рад/с при полной скорости
        public const float LandDip = 0.22f;
        public const float ShakeDecay = 1.6f;

        // ---------------------------------------------------------------
        // Взаимодействие
        // ---------------------------------------------------------------
        public const float InteractReach = 2.6f;
        public const float InteractRadius = 0.14f;     // радиус SphereCast — проще попасть пальцем
        public const float GrabLerp = 22f;             // скорость "прилипания" предмета к руке
        public const float PunchImpulse = 150f;        // Н·с — удар пустой рукой
        public const float HeldItemPunchBonus = 1.9f;  // множитель, если бьём предметом
        public const float ThrowImpactDamageScale = 6f;
        public const float RagdollImpulseThreshold = 260f; // Н·с — после этого манекен улетает

        // ---------------------------------------------------------------
        // Оптимизация (Samsung Galaxy A53: Exynos 1200 / Mali-G77, 6-8 ГБ)
        // ---------------------------------------------------------------
        public enum PerfPreset { Low, Medium, High }

        public static PerfPreset DefaultPresetForDevice()
        {
            string model = (SystemInfo.deviceModel ?? string.Empty).ToUpperInvariant();
            // Samsung Galaxy A53 (SM-A536*) и сопоставимые среднебюджетники.
            bool isA53 = model.Contains("A536") || model.Contains("A53 ") || model.Contains("SM-A53");
            if (isA53) return PerfPreset.Medium;

            int mem = SystemInfo.systemMemorySize;      // МБ
            int cores = SystemInfo.processorCount;
            if (mem <= 3072 || cores <= 4) return PerfPreset.Low;
            if (mem <= 6144) return PerfPreset.Medium;
            return PerfPreset.High;
        }

        public static void ApplyPreset(PerfPreset preset)
        {
            switch (preset)
            {
                case PerfPreset.Low:
                    UrpTweaks.SetRenderScale(0.70f);
                    UrpTweaks.SetShadowDistance(14f);
                    UrpTweaks.SetMsaa(1);
                    Application.targetFrameRate = 30;
                    PerformanceTuner.InterpolateBodies = false;
                    PerformanceTuner.UseReflectionProbe = false;
                    PerformanceTuner.ShardCount = 4;
                    break;
                case PerfPreset.Medium: // Galaxy A53
                    UrpTweaks.SetRenderScale(0.85f);
                    UrpTweaks.SetShadowDistance(22f);
                    UrpTweaks.SetMsaa(1);
                    Application.targetFrameRate = 60;
                    PerformanceTuner.InterpolateBodies = true;
                    PerformanceTuner.UseReflectionProbe = true;
                    PerformanceTuner.ShardCount = 6;
                    break;
                default:
                    UrpTweaks.SetRenderScale(1.0f);
                    UrpTweaks.SetShadowDistance(35f);
                    UrpTweaks.SetMsaa(2);
                    Application.targetFrameRate = 60;
                    PerformanceTuner.InterpolateBodies = true;
                    PerformanceTuner.UseReflectionProbe = true;
                    PerformanceTuner.ShardCount = 10;
                    break;
            }
        }
    }
}
