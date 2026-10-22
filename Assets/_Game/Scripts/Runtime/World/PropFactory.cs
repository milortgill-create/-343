using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Фабрика тестовых предметов: ящики, бочки, кирпичи, доски, бутылки.
    /// У каждого — Rigidbody, коллайдер, Grabbable и ImpactHandler, то есть
    /// полная физика: гравитация, столкновения, подбор, бросок, урон.
    /// </summary>
    public static class PropFactory
    {
        static Transform s_Root;

        public static void SetRoot(Transform root)
        {
            s_Root = root;
        }

        static Transform Root => s_Root;

        static GameObject Finish(GameObject go, string displayName, float mass, ImpactSound sound,
            float damageMultiplier = 1f, float heaviness = 1f)
        {
            go.layer = Layers.Prop;
            GameUtil.SetLayerRecursive(go, Layers.Prop);

            // трение/упругость по типу поверхности: ящик не скользит как бочка,
            // бутылка звонко подпрыгивает, кирпич шоркает по бетону
            PhysicMaterials.Apply(go, PhysicMaterials.ForImpactSound(sound));

            var rb = GameUtil.AddRigidbody(go, mass);
            rb.interpolation = PerformanceTuner.InterpolateBodies
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;

            var grab = go.AddComponent<Grabbable>();
            grab.displayName = displayName;
            grab.impactSound = sound;
            grab.damageMultiplier = damageMultiplier;
            grab.heaviness = heaviness;
            grab.RememberSpawn();

            var impact = go.AddComponent<ImpactHandler>();
            impact.minImpactSpeed = 1.3f;
            impact.dustSpeed = 3.0f;

            // гарантируем, что предмет не родился внутри пола/стены
            GameUtil.SettleSpawn(go, SandboxConfig.FloorTopY);

            if (OutOfBoundsRecovery.Instance != null) OutOfBoundsRecovery.Instance.Register(grab);
            return go;
        }

        // ------------------------------------------------------------------
        // Деревянный ящик
        // ------------------------------------------------------------------
        public static GameObject Crate(Vector3 position, float size = 0.62f, Quaternion? rotation = null)
        {
            var root = GameUtil.New("Crate", Root);
            root.transform.position = position;
            root.transform.rotation = rotation ?? Quaternion.identity;

            var body = GameUtil.NewPrimitive(PrimitiveType.Cube, "Body", root.transform,
                Vector3.zero, Vector3.one * size);
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Wood;

            // рамка из реек — визуально отличает ящик от куба и даёт интересную физику
            float t = size * 0.075f;
            Vector3[] edges =
            {
                new Vector3(0, size * 0.5f - t * 0.5f, 0),
                new Vector3(0, -size * 0.5f + t * 0.5f, 0)
            };
            foreach (var e in edges)
            {
                var band = GameUtil.NewPrimitive(PrimitiveType.Cube, "Band", root.transform,
                    e, new Vector3(size * 1.02f, t, size * 1.02f));
                band.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.DarkWood;
                GameUtil.StripCollider(band);
            }

            var col = root.AddComponent<BoxCollider>();
            col.size = Vector3.one * size;
            col.center = Vector3.zero;

            return Finish(root, "Ящик", 12f * (size / 0.62f), ImpactSound.Wood, 1.25f, 1.0f);
        }

        // ------------------------------------------------------------------
        // Металлическая бочка
        // ------------------------------------------------------------------
        public static GameObject Barrel(Vector3 position, bool rusty = false, Quaternion? rotation = null)
        {
            var root = GameUtil.New(rusty ? "Barrel_Rusty" : "Barrel", Root);
            root.transform.position = position;
            root.transform.rotation = rotation ?? Quaternion.identity;

            var body = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Body", root.transform,
                Vector3.zero, new Vector3(0.58f, 0.44f, 0.58f));
            body.GetComponent<MeshRenderer>().sharedMaterial = rusty ? MaterialFactory.RustyMetal : MaterialFactory.Metal;

            // рёбра жёсткости
            for (int i = -1; i <= 1; i += 2)
            {
                var rib = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Rib", root.transform,
                    new Vector3(0, i * 0.16f, 0), new Vector3(0.615f, 0.02f, 0.615f));
                rib.GetComponent<MeshRenderer>().sharedMaterial = rusty ? MaterialFactory.RustyMetal : MaterialFactory.Metal;
                GameUtil.StripCollider(rib);
            }
            var lid = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Lid", root.transform,
                new Vector3(0, 0.441f, 0), new Vector3(0.50f, 0.012f, 0.50f));
            lid.GetComponent<MeshRenderer>().sharedMaterial = rusty ? MaterialFactory.RustyMetal : MaterialFactory.Metal;
            GameUtil.StripCollider(lid);

            // у цилиндра-примитива Unity создаёт CapsuleCollider — подгоняем под бочку
            var cap = body.GetComponent<CapsuleCollider>();
            if (cap != null) UnityEngine.Object.Destroy(cap);
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius = 0.29f;
            col.height = 0.88f;
            col.center = Vector3.zero;

            return Finish(root, rusty ? "Бочка (ржавая)" : "Бочка", rusty ? 22f : 19f, ImpactSound.Metal, 1.6f, 1.5f);
        }

        // ------------------------------------------------------------------
        // Кирпич
        // ------------------------------------------------------------------
        public static GameObject Brick(Vector3 position, Quaternion? rotation = null)
        {
            var root = GameUtil.New("Brick", Root);
            root.transform.position = position;
            root.transform.rotation = rotation ?? Quaternion.identity;

            var body = GameUtil.NewPrimitive(PrimitiveType.Cube, "Body", root.transform,
                Vector3.zero, new Vector3(0.25f, 0.075f, 0.12f));
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.BrickSolid;

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.25f, 0.075f, 0.12f);

            return Finish(root, "Кирпич", 2.6f, ImpactSound.Concrete, 1.35f, 0.45f);
        }

        // ------------------------------------------------------------------
        // Доска
        // ------------------------------------------------------------------
        public static GameObject Plank(Vector3 position, Quaternion? rotation = null, float length = 2.0f)
        {
            var root = GameUtil.New("Plank", Root);
            root.transform.position = position;
            root.transform.rotation = rotation ?? Quaternion.identity;

            var body = GameUtil.NewPrimitive(PrimitiveType.Cube, "Body", root.transform,
                Vector3.zero, new Vector3(0.17f, 0.035f, length));
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.DarkWood;

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.17f, 0.035f, length);

            return Finish(root, "Доска", 5.5f * (length / 2f), ImpactSound.Wood, 1.15f, 1.2f);
        }

        // ------------------------------------------------------------------
        // Пустая стеклянная бутылка (бьётся)
        // ------------------------------------------------------------------
        public static GameObject Bottle(Vector3 position, Quaternion? rotation = null)
        {
            var root = GameUtil.New("Bottle", Root);
            root.transform.position = position;
            root.transform.rotation = rotation ?? Quaternion.identity;

            var body = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Body", root.transform,
                new Vector3(0, 0.10f, 0), new Vector3(0.072f, 0.10f, 0.072f));
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Glass;
            var bodyCol = body.GetComponent<CapsuleCollider>();
            if (bodyCol != null)
            {
                bodyCol.direction = 1;
                bodyCol.radius = 0.036f;
                bodyCol.height = 0.20f;
                bodyCol.center = new Vector3(0, -0.10f, 0);
            }

            var shoulder = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Shoulder", root.transform,
                new Vector3(0, 0.205f, 0), new Vector3(0.055f, 0.015f, 0.055f));
            shoulder.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Glass;
            GameUtil.StripCollider(shoulder);

            var neck = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Neck", root.transform,
                new Vector3(0, 0.255f, 0), new Vector3(0.028f, 0.04f, 0.028f));
            neck.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Glass;
            GameUtil.StripCollider(neck);

            var cap = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Cap", root.transform,
                new Vector3(0, 0.292f, 0), new Vector3(0.030f, 0.006f, 0.030f));
            cap.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Metal;
            GameUtil.StripCollider(cap);

            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius = 0.037f;
            col.height = 0.30f;
            col.center = new Vector3(0, 0.15f, 0);
            // убираем дублирующий коллайдер на "теле"
            if (bodyCol != null) UnityEngine.Object.Destroy(bodyCol);

            var go = Finish(root, "Бутылка", 0.42f, ImpactSound.Glass, 0.9f, 0.25f);
            var brk = go.AddComponent<Breakable>();
            brk.breakSpeed = 4.6f;
            brk.shardMaterial = MaterialFactory.Glass;
            brk.sound = ImpactSound.Glass;
            return go;
        }

        // ------------------------------------------------------------------
        // Статичные элементы полигона (без Rigidbody — не подбираются)
        // ------------------------------------------------------------------
        public static GameObject StaticBox(string name, Vector3 position, Vector3 size, Material mat,
            Quaternion? rotation = null, Transform parent = null, PhysicMaterial physicsMaterial = null)
        {
            var go = GameUtil.NewPrimitive(PrimitiveType.Cube, name, parent != null ? parent : Root,
                Vector3.zero, size, rotation);
            // position задаётся в мировых координатах — не зависит от смещения родителя
            go.transform.position = position;
            if (rotation.HasValue) go.transform.rotation = rotation.Value;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var col = go.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.center = Vector3.zero;
                col.size = Vector3.one; // BoxCollider примитива всегда 1x1x1 в локальных единицах
                col.sharedMaterial = physicsMaterial != null ? physicsMaterial : PhysicMaterials.World;
            }
            go.layer = Layers.World;
            go.isStatic = true;
            return go;
        }

        /// <summary>Наклонная плоскость — проверяем качение бочек и скольжение.</summary>
        public static GameObject Ramp(Vector3 position, float width, float length, float angleDegrees, Material mat)
        {
            var root = GameUtil.New("Ramp", Root);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(-angleDegrees, 0f, 0f);

            var slab = GameUtil.NewPrimitive(PrimitiveType.Cube, "Slab", root.transform,
                new Vector3(0, -0.15f, 0), new Vector3(width, 0.30f, length));
            slab.GetComponent<MeshRenderer>().sharedMaterial = mat;
            root.layer = Layers.World;
            GameUtil.SetLayerRecursive(root, Layers.World);
            root.isStatic = true;
            return root;
        }
    }
}
