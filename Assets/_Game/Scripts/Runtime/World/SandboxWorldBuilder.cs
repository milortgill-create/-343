using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sandbox
{
    /// <summary>Результат построения полигона.</summary>
    public class SandboxWorld
    {
        public Transform Root;
        public Transform Floor;
        public List<Grabbable> Props = new List<Grabbable>();
        public List<DamageableDummy> Dummies = new List<DamageableDummy>();
        public PhoneProp Phone;
        public Vector3 PlayerSpawn;
        public Quaternion PlayerSpawnRotation;
        public Light Sun;
        public ReflectionProbe Probe;
        public int StaticColliderCount;
        public int RigidbodyCount;
    }

    /// <summary>
    /// Строит тестовый полигон целиком из кода: бетонный пол, стены, потолок,
    /// свет, ящики, бочки, кирпичи, доски, бутылки, манекены, платформу и пандус.
    /// Вызывается из GameBootstrap (в Play-режиме) и из SandboxBaker (в редакторе).
    /// </summary>
    public static class SandboxWorldBuilder
    {
        public static SandboxWorld Build(Transform parent, bool includePhone = true)
        {
            MaterialFactory.Init();
            PropFactory.SetRoot(parent);

            var world = new SandboxWorld { Root = parent };

            var arena = GameUtil.New("Arena", parent);
            BuildShell(arena.transform, world);
            BuildLights(arena.transform, world);
            BuildFurniture(arena.transform, world);
            BuildProps(arena.transform, world);
            world.Dummies.Add(DummyFactory.Create(new Vector3(-1.6f, 0f, 2.2f), Quaternion.Euler(0, 168f, 0), "Манекен A", parent));
            world.Dummies.Add(DummyFactory.Create(new Vector3(2.6f, 0f, 3.0f), Quaternion.Euler(0, 192f, 0), "Манекен B", parent));
            BuildSigns(arena.transform);

            world.PlayerSpawn = new Vector3(0f, 0f, -6.2f);
            world.PlayerSpawnRotation = Quaternion.Euler(0f, 8f, 0f);

            if (includePhone)
            {
                world.Phone = PhoneFactory.Create(parent, world.PlayerSpawn + world.PlayerSpawnRotation * Vector3.forward * 1.2f + Vector3.up * 0.06f);
                if (world.Phone != null) world.Props.Add(world.Phone.Grabbable);
            }

            Physics.SyncTransforms();
            CountBodies(parent, world);
            return world;
        }

        // ------------------------------------------------------------------
        // Пол / стены / потолок
        // ------------------------------------------------------------------
        static void BuildShell(Transform parent, SandboxWorld world)
        {
            float w = SandboxConfig.ArenaWidth;
            float d = SandboxConfig.ArenaDepth;
            float h = SandboxConfig.ArenaHeight;
            float ft = SandboxConfig.FloorThickness;
            float wt = SandboxConfig.WallThickness;

            // ПОЛ: плита толщиной 1 м. Именно толщина (плюс ContinuousDynamic CCD
            // у брошенных тел) исключает проваливание предметов.
            var floor = PropFactory.StaticBox("Floor", new Vector3(0f, SandboxConfig.FloorTopY - ft * 0.5f, 0f),
                new Vector3(w + wt * 2f, ft, d + wt * 2f), MaterialFactory.ConcreteFloor, null, parent,
                PhysicMaterials.Concrete);
            world.Floor = floor.transform;
            floor.isStatic = true;
            var floorCol = floor.GetComponent<BoxCollider>();
            if (floorCol != null) floorCol.sharedMaterial = null;

            // СТЕНЫ
            PropFactory.StaticBox("Wall_North", new Vector3(0f, h * 0.5f, -(d * 0.5f + wt * 0.5f)),
                new Vector3(w + wt * 2f, h, wt), MaterialFactory.ConcreteWall, null, parent);
            PropFactory.StaticBox("Wall_South", new Vector3(0f, h * 0.5f, d * 0.5f + wt * 0.5f),
                new Vector3(w + wt * 2f, h, wt), MaterialFactory.BrickWall, null, parent);
            PropFactory.StaticBox("Wall_West", new Vector3(-(w * 0.5f + wt * 0.5f), h * 0.5f, 0f),
                new Vector3(wt, h, d), MaterialFactory.BrickWall, null, parent);
            PropFactory.StaticBox("Wall_East", new Vector3(w * 0.5f + wt * 0.5f, h * 0.5f, 0f),
                new Vector3(wt, h, d), MaterialFactory.ConcreteWall, null, parent);

            // ПОТОЛОК (не отбрасывает тени, чтобы «солнце» освещало полигон)
            var ceiling = PropFactory.StaticBox("Ceiling", new Vector3(0f, h + 0.3f, 0f),
                new Vector3(w + wt * 2f, 0.6f, d + wt * 2f), MaterialFactory.ConcreteWall, null, parent);
            var ceilRenderers = ceiling.GetComponentsInChildren<MeshRenderer>();
            foreach (var mr in ceilRenderers)
            {
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }

            // Плинтус по периметру — визуально закрывает стык пола и стен
            float skirtH = 0.22f;
            PropFactory.StaticBox("Skirt_N", new Vector3(0f, skirtH * 0.5f, -(d * 0.5f - 0.05f)),
                new Vector3(w, skirtH, 0.12f), MaterialFactory.Grid, null, parent);
            PropFactory.StaticBox("Skirt_S", new Vector3(0f, skirtH * 0.5f, d * 0.5f - 0.05f),
                new Vector3(w, skirtH, 0.12f), MaterialFactory.Grid, null, parent);
            PropFactory.StaticBox("Skirt_W", new Vector3(-(w * 0.5f - 0.05f), skirtH * 0.5f, 0f),
                new Vector3(0.12f, skirtH, d), MaterialFactory.Grid, null, parent);
            PropFactory.StaticBox("Skirt_E", new Vector3(w * 0.5f - 0.05f, skirtH * 0.5f, 0f),
                new Vector3(0.12f, skirtH, d), MaterialFactory.Grid, null, parent);

            // Разметка на полу: зона бросков и зона боя
            AddFloorDecal(parent, new Vector3(3.5f, 0f, 1.5f), new Vector2(6f, 6f), MaterialFactory.Hazard, 0.35f);
            AddFloorDecal(parent, new Vector3(-7.5f, 0f, 4.5f), new Vector2(4f, 4f), MaterialFactory.Grid, 0.5f);
        }

        static void AddFloorDecal(Transform parent, Vector3 position, Vector2 size, Material mat, float alpha)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "FloorDecal";
            quad.transform.SetParent(parent, false);
            quad.transform.position = position + Vector3.up * 0.012f;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            var col = quad.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var mr = quad.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var decalMat = MaterialFactory.Create("Decal", null, Color.white, 0f, 0.2f,
                    transparent: true, alpha: alpha);
                decalMat.SetTexture("_BaseMap", mat.mainTexture);
                mr.sharedMaterial = decalMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            quad.layer = Layers.World;
            quad.isStatic = true;
        }

        // ------------------------------------------------------------------
        // Освещение
        // ------------------------------------------------------------------
        static void BuildLights(Transform parent, SandboxWorld world)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.50f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.31f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.15f, 0.14f);
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.7f;
            RenderSettings.reflectionBounces = 1;

            // Основной направленный свет (тени только от него — дёшево для A53)
            var sunGo = GameUtil.New("Sun", parent);
            sunGo.transform.rotation = Quaternion.Euler(56f, -32f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.62f;
            sun.shadowBias = 0.12f;
            sun.shadowNormalBias = 0.035f;
            sun.renderMode = LightRenderMode.ForcePixel;
            sun.cullingMask = ~0; // светят на всё; руки не отбрасывают тень (shadowCastingMode=Off)
            world.Sun = sun;

            // Потолочные светильники: 4 «панели» + точечные источники без теней
            float w = SandboxConfig.ArenaWidth;
            float d = SandboxConfig.ArenaDepth;
            float y = SandboxConfig.ArenaHeight - 0.18f;
            Vector3[] spots =
            {
                new Vector3(-w * 0.25f, y, -d * 0.25f),
                new Vector3(w * 0.25f, y, -d * 0.25f),
                new Vector3(-w * 0.25f, y, d * 0.25f),
                new Vector3(w * 0.25f, y, d * 0.25f)
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var fixture = PropFactory.StaticBox("LightFixture_" + i, spots[i] + Vector3.up * 0.12f,
                    new Vector3(2.2f, 0.16f, 1.1f), MaterialFactory.LightPanel, null, parent);
                var mr = fixture.GetComponent<MeshRenderer>();
                if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;

                var lightGo = GameUtil.New("CeilingLight_" + i, parent);
                lightGo.transform.position = spots[i] + Vector3.down * 0.25f;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.95f, 0.86f);
                light.intensity = 2.1f;
                light.range = 17f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.cullingMask = ~0;
            }

            // Одна realtime-проба отражений — металл и стекло выглядят правильно.
            // Обновляется один раз при старте (ViaScripting), дальше бесплатно.
            if (PerformanceTuner.UseReflectionProbe)
            {
                var probeGo = GameUtil.New("ReflectionProbe", parent);
                probeGo.transform.position = new Vector3(0f, 2.2f, 0f);
                var probe = probeGo.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.OneFrame;
                probe.size = new Vector3(SandboxConfig.ArenaWidth, SandboxConfig.ArenaHeight, SandboxConfig.ArenaDepth);
                probe.center = new Vector3(0f, 1.4f, 0f);
                probe.intensity = 0.9f;
                probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
                probe.resolution = 64;
                probe.hdr = false;
                probe.cullingMask = ~0;
                world.Probe = probe;
            }
        }

        // ------------------------------------------------------------------
        // Мебель: платформа, пандус, стеллажи, тумбы
        // ------------------------------------------------------------------
        static void BuildFurniture(Transform parent, SandboxWorld world)
        {
            // Платформа для сбрасывания предметов (проверка падения и отскока)
            PropFactory.StaticBox("Platform", new Vector3(-8.4f, 0.65f, -5.2f), new Vector3(3.2f, 1.3f, 3.2f),
                MaterialFactory.ConcreteWall, null, parent);
            PropFactory.StaticBox("Platform_Edge", new Vector3(-8.4f, 1.32f, -5.2f), new Vector3(3.3f, 0.05f, 3.3f),
                MaterialFactory.Hazard, null, parent);

            // Пандус — катаем бочки
            PropFactory.Ramp(new Vector3(7.6f, 0.72f, -6.2f), 3.0f, 5.2f, 17f, MaterialFactory.ConcreteWall);

            // Стеллаж у южной стены (на нём бутылки)
            PropFactory.StaticBox("Shelf_A", new Vector3(4.2f, 0.95f, 9.2f), new Vector3(4.6f, 0.10f, 0.9f),
                MaterialFactory.DarkWood, null, parent);
            PropFactory.StaticBox("Shelf_A_Leg1", new Vector3(2.1f, 0.47f, 9.2f), new Vector3(0.14f, 0.94f, 0.8f),
                MaterialFactory.RustyMetal, null, parent);
            PropFactory.StaticBox("Shelf_A_Leg2", new Vector3(6.3f, 0.47f, 9.2f), new Vector3(0.14f, 0.94f, 0.8f),
                MaterialFactory.RustyMetal, null, parent);

            PropFactory.StaticBox("Shelf_B", new Vector3(-4.2f, 1.25f, 9.2f), new Vector3(4.2f, 0.10f, 0.8f),
                MaterialFactory.DarkWood, null, parent);
            PropFactory.StaticBox("Shelf_B_Leg1", new Vector3(-6.1f, 0.62f, 9.2f), new Vector3(0.14f, 1.24f, 0.7f),
                MaterialFactory.RustyMetal, null, parent);
            PropFactory.StaticBox("Shelf_B_Leg2", new Vector3(-2.3f, 0.62f, 9.2f), new Vector3(0.14f, 1.24f, 0.7f),
                MaterialFactory.RustyMetal, null, parent);

            // Тумба в центре — удобно ставить предметы и бить по ним сверху
            PropFactory.StaticBox("Pedestal", new Vector3(0.4f, 0.45f, -1.2f), new Vector3(1.5f, 0.9f, 1.5f),
                MaterialFactory.ConcreteWall, null, parent);
        }

        // ------------------------------------------------------------------
        // Предметы для тестов
        // ------------------------------------------------------------------
        static void BuildProps(Transform parent, SandboxWorld world)
        {
            void Add(GameObject go)
            {
                var g = go != null ? go.GetComponent<Grabbable>() : null;
                if (g != null) world.Props.Add(g);
            }

            // --- ящики (в том числе штабель) ---
            Add(PropFactory.Crate(new Vector3(-2.6f, 0.32f, -3.2f), 0.62f));
            Add(PropFactory.Crate(new Vector3(-2.6f, 0.96f, -3.2f), 0.62f));   // штабель
            Add(PropFactory.Crate(new Vector3(-1.9f, 0.32f, -3.6f), 0.62f, Quaternion.Euler(0f, 22f, 0f)));
            Add(PropFactory.Crate(new Vector3(5.4f, 0.30f, 2.8f), 0.58f, Quaternion.Euler(0f, -14f, 0f)));
            Add(PropFactory.Crate(new Vector3(-8.4f, 1.62f, -5.2f), 0.55f, Quaternion.Euler(0f, 35f, 0f))); // на платформе
            Add(PropFactory.Crate(new Vector3(9.6f, 0.40f, 5.4f), 0.78f));

            // --- бочки ---
            Add(PropFactory.Barrel(new Vector3(3.4f, 0.45f, -3.4f)));
            Add(PropFactory.Barrel(new Vector3(4.2f, 0.45f, -2.6f), true));
            Add(PropFactory.Barrel(new Vector3(-5.8f, 0.45f, 1.4f)));
            Add(PropFactory.Barrel(new Vector3(7.2f, 0.45f, 7.4f), true));
            Add(PropFactory.Barrel(new Vector3(-8.4f, 1.75f, -4.2f))); // на платформе — упадёт
            // бочка на боку у пандуса (проверка качения)
            Add(PropFactory.Barrel(new Vector3(7.6f, 0.30f, -2.6f), false, Quaternion.Euler(90f, 0f, 0f)));

            // --- кирпичи ---
            Vector3 brickBase = new Vector3(-0.6f, 0.94f, -1.2f);
            for (int i = 0; i < 3; i++)
            {
                Add(PropFactory.Brick(brickBase + new Vector3(i * 0.27f, 0f, 0f),
                    Quaternion.Euler(0f, i * 6f, 0f)));
            }
            Add(PropFactory.Brick(brickBase + new Vector3(0.13f, 0.08f, 0.02f), Quaternion.Euler(0f, 40f, 0f)));
            Add(PropFactory.Brick(new Vector3(6.2f, 0.04f, 4.6f), Quaternion.Euler(0f, 12f, 0f)));
            Add(PropFactory.Brick(new Vector3(6.45f, 0.04f, 4.9f), Quaternion.Euler(0f, -30f, 0f)));
            Add(PropFactory.Brick(new Vector3(-4.2f, 1.34f, 9.2f), Quaternion.Euler(0f, 5f, 0f)));  // на стеллаже
            Add(PropFactory.Brick(new Vector3(-9.6f, 0.04f, 3.1f), Quaternion.Euler(0f, 70f, 0f)));

            // --- доски ---
            Add(PropFactory.Plank(new Vector3(-11.8f, 1.05f, 0.6f), Quaternion.Euler(0f, 0f, 78f), 2.1f)); // прислонена к стене
            Add(PropFactory.Plank(new Vector3(-11.5f, 1.05f, 1.1f), Quaternion.Euler(0f, 4f, 76f), 2.1f));
            Add(PropFactory.Plank(new Vector3(1.6f, 0.03f, 6.2f), Quaternion.Euler(0f, 26f, 0f), 2.4f));
            Add(PropFactory.Plank(new Vector3(-6.6f, 0.03f, -7.4f), Quaternion.Euler(0f, -62f, 0f), 1.8f));

            // --- пустые бутылки ---
            for (int i = 0; i < 3; i++)
            {
                Add(PropFactory.Bottle(new Vector3(3.2f + i * 0.42f, 1.0f, 9.2f)));
            }
            Add(PropFactory.Bottle(new Vector3(-3.4f, 1.30f, 9.2f)));
            Add(PropFactory.Bottle(new Vector3(-4.9f, 1.30f, 9.2f)));
            Add(PropFactory.Bottle(new Vector3(0.1f, 0.90f, -1.5f))); // на тумбе
            Add(PropFactory.Bottle(new Vector3(-8.9f, 1.30f, -5.7f))); // на платформе

            // --- дополнительный «мусор» для стресс-теста физики ---
            Add(PropFactory.Crate(new Vector3(10.4f, 0.26f, -7.6f), 0.5f, Quaternion.Euler(0f, 48f, 0f)));
            Add(PropFactory.Brick(new Vector3(10.1f, 0.04f, -6.9f)));
            Add(PropFactory.Bottle(new Vector3(10.6f, 0.02f, -6.4f)));
        }

        // ------------------------------------------------------------------
        // Таблички
        // ------------------------------------------------------------------
        static void BuildSigns(Transform parent)
        {
            AddSign(parent, "ПОЛИГОН  A-343", new Vector3(0f, 4.2f, -9.9f), Quaternion.identity, new Vector2(6f, 1.2f));
            AddSign(parent, "ЗОНА БРОСКОВ", new Vector3(3.5f, 2.6f, 9.85f), Quaternion.Euler(0f, 180f, 0f), new Vector2(4.4f, 0.9f));
            AddSign(parent, "МАНЕКЕНЫ", new Vector3(-11.9f, 2.8f, 2.2f), Quaternion.Euler(0f, 90f, 0f), new Vector2(3.6f, 0.8f));
            AddSign(parent, "БЕЙ  ЛОМАЙ  БРОСАЙ", new Vector3(12.85f, 3.0f, -1.0f), Quaternion.Euler(0f, -90f, 0f), new Vector2(5.2f, 1.0f));
        }

        static void AddSign(Transform parent, string text, Vector3 position, Quaternion rotation, Vector2 size)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Sign_" + text;
            quad.transform.SetParent(parent, false);
            quad.transform.position = position;
            quad.transform.rotation = rotation;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            var col = quad.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var tex = ProceduralTextures.Label(text, 512, 96,
                new Color(0.09f, 0.10f, 0.12f), new Color(0.95f, 0.85f, 0.35f));
            var mat = MaterialFactory.CreateUnlit("Sign_" + text, Color.white, tex);
            var mr = quad.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            quad.layer = Layers.World;
            quad.isStatic = true;
        }

        static void CountBodies(Transform root, SandboxWorld world)
        {
            world.StaticColliderCount = 0;
            world.RigidbodyCount = 0;
            var cols = root.GetComponentsInChildren<Collider>();
            foreach (var c in cols)
            {
                if (c.attachedRigidbody == null) world.StaticColliderCount++;
            }
            world.RigidbodyCount = root.GetComponentsInChildren<Rigidbody>().Length;
        }
    }

    /// <summary>Простой биллборд для подписей над объектами.</summary>
    public class Billboard : MonoBehaviour
    {
        public bool onlyY = true;
        Transform m_Cam;

        void LateUpdate()
        {
            if (m_Cam == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                m_Cam = cam.transform;
            }
            Vector3 dir = transform.position - m_Cam.position;
            if (onlyY) dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
