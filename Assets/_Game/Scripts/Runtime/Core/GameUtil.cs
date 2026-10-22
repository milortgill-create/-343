using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>Имена слоёв проекта (см. ProjectSettings/TagManager.asset).</summary>
    public static class Layers
    {
        public const string PlayerName = "Player";
        public const string DummyName = "Dummy";
        public const string PropName = "Prop";
        public const string WorldName = "World";
        public const string HeldName = "Held";
        public const string HandsName = "Hands";
        public const string DebrisName = "Debris";

        static readonly Dictionary<string, int> Cache = new Dictionary<string, int>();

        public static int Get(string layerName)
        {
            if (Cache.TryGetValue(layerName, out int cached) && cached >= 0) return cached;
            int index = LayerMask.NameToLayer(layerName);
            if (index < 0)
            {
                // Слой не создан (например, проект открыли без TagManager.asset) —
                // работаем на Default, чтобы ничего не сломалось.
                Debug.LogWarning($"[Sandbox] Layer '{layerName}' not found in TagManager, falling back to Default.");
                index = 0;
            }
            Cache[layerName] = index;
            return index;
        }

        public static int Player => Get(PlayerName);
        public static int Dummy => Get(DummyName);
        public static int Prop => Get(PropName);
        public static int World => Get(WorldName);
        public static int Held => Get(HeldName);
        public static int Hands => Get(HandsName);
        public static int Debris => Get(DebrisName);

        /// <summary>Маска того, с чем может взаимодействовать игрок.</summary>
        public static int InteractableMask => (1 << Prop) | (1 << Dummy) | (1 << Debris);

        /// <summary>Маска для raycast камеры (без рук, без удерживаемого предмета).</summary>
        public static int ViewMask
        {
            get
            {
                int ignoreRaycast = Mathf.Max(0, LayerMask.NameToLayer("Ignore Raycast"));
                return ~((1 << Hands) | (1 << Held) | (1 << ignoreRaycast));
            }
        }

        /// <summary>Маска столкновений персонажа.</summary>
        public static int CharacterMask => (1 << World) | (1 << Prop) | (1 << Dummy) | (1 << Debris);

        public static void ApplyCollisionRules()
        {
            int held = Held, player = Player, hands = Hands, debris = Debris;
            // Предмет в руках не должен толкать самого игрока и не должен
            // сталкиваться с руками (иначе он "дрожит" у лица).
            Physics.IgnoreLayerCollision(held, player, true);
            Physics.IgnoreLayerCollision(held, hands, true);
            Physics.IgnoreLayerCollision(hands, player, true);
            // Осколки не толкают игрока (мелкий мусор от бутылок).
            Physics.IgnoreLayerCollision(debris, player, true);
            Physics.IgnoreLayerCollision(debris, hands, true);
        }
    }

    /// <summary>Мелкие вспомогательные функции для построения мира из кода.</summary>
    public static class GameUtil
    {
        /// <summary>Создаёт пустой GameObject с именем и родителем.</summary>
        public static GameObject New(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Создаёт примитив, снимает с него стандартный материал и ставит нужный слой.</summary>
        public static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPos,
            Vector3 localScale, Quaternion? localRot = null, int layer = -1)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            if (layer >= 0) SetLayerRecursive(go, layer);

            // Unity вешает на примитивы встроенный материал Standard, который в URP
            // становится розовым. Убираем его — материал назначит MaterialFactory.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = null;
            return go;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0 || layer > 31) return;
            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        /// <summary>Удаляет коллайдер примитива (когда коллайдер нужен на родительском объекте).</summary>
        public static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }

        public static Rigidbody AddRigidbody(GameObject go, float mass, bool isStatic = false)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = isStatic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxAngularVelocity = SandboxConfig.MaxAngularVelocity;
            rb.sleepThreshold = SandboxConfig.SleepThreshold;
            rb.maxDepenetrationVelocity = SandboxConfig.MaxDepenetrationVelocity;
            rb.solverIterations = Mathf.Max(1, (int)SandboxConfig.SolverIterations);
            rb.solverVelocityIterations = Mathf.Max(1, (int)SandboxConfig.SolverVelocityIterations);
            return rb;
        }

        /// <summary>
        /// Аккуратно «усаживает» предмет при спавне:
        ///  1) поднимает, если коллайдер оказался ниже уровня пола;
        ///  2) выталкивает по MTV, если он родился внутри статичной геометрии.
        /// Намеренные штабели предметов при этом не разрушаются.
        /// </summary>
        public static bool SettleSpawn(GameObject go, float floorY, int staticMask = -1, int maxIterations = 4)
        {
            if (staticMask < 0) staticMask = 1 << Layers.World;
            var cols = go.GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return false;

            bool moved = false;

            // 1) не ниже пола
            float lowest = float.MaxValue;
            foreach (var c in cols) lowest = Mathf.Min(lowest, c.bounds.min.y);
            if (lowest < floorY)
            {
                go.transform.position += Vector3.up * (floorY - lowest + 0.002f);
                Physics.SyncTransforms();
                moved = true;
            }

            // 2) не внутри стены/платформы
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool anyPenetration = false;
                foreach (var c in cols)
                {
                    if (c == null) continue;
                    var others = Physics.OverlapBox(c.bounds.center, c.bounds.extents * 0.98f,
                        Quaternion.identity, staticMask, QueryTriggerInteraction.Ignore);
                    foreach (var other in others)
                    {
                        if (other == null || other == c) continue;
                        if (other.attachedRigidbody != null && other.attachedRigidbody == c.attachedRigidbody) continue;
                        Vector3 dir;
                        float distance;
                        if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                                other, other.transform.position, other.transform.rotation,
                                out dir, out distance))
                        {
                            go.transform.position += dir * (distance + 0.002f);
                            Physics.SyncTransforms();
                            anyPenetration = true;
                            moved = true;
                            break;
                        }
                    }
                    if (anyPenetration) break;
                }
                if (!anyPenetration) break;
            }

            return moved;
        }

        public static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
