using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Ограничения одного сустава. Оси сустава у всех костей одинаковые:
    /// X = вправо, Y = вверх, Z = вперёд (ось X — "асимметричная", у неё есть
    /// нижний и верхний предел, Y и Z — симметричные ±limit).
    /// </summary>
    [Serializable]
    public class JointLimits
    {
        [Header("Вокруг X (вправо)")] public float angularXLow = -25f;
        public float angularXHigh = 25f;
        [Header("Вокруг Y (вверх)")] public float angularY = 20f;
        [Header("Вокруг Z (вперёд)")] public float angularZ = 20f;
        [Header("Приводы (мышцы)")] public float spring = 320f;
        public float damper = 42f;
        public float maxForce = float.PositiveInfinity;

        public JointLimits Clone()
        {
            return new JointLimits
            {
                angularXLow = angularXLow,
                angularXHigh = angularXHigh,
                angularY = angularY,
                angularZ = angularZ,
                spring = spring,
                damper = damper,
                maxForce = maxForce
            };
        }

        public static JointLimits Create(float xLow, float xHigh, float y, float z, float spring, float damper)
        {
            return new JointLimits
            {
                angularXLow = xLow,
                angularXHigh = xHigh,
                angularY = y,
                angularZ = z,
                spring = spring,
                damper = damper
            };
        }
    }

    public enum BoneColliderKind { Box, Capsule, Sphere }

    [Serializable]
    public class BoneSpec
    {
        public string name;
        public string parent;
        /// <summary>Позиция кости в пространстве КОРНЯ рэгдолла (корень — в ступнях).</summary>
        public Vector3 localPos;
        public BoneColliderKind kind = BoneColliderKind.Capsule;
        /// <summary>0 = X, 1 = Y, 2 = Z — направление оси капсулы.</summary>
        public int direction = 1;
        public Vector3 colliderCenter;
        public Vector3 colliderSize = new Vector3(0.2f, 0.2f, 0.2f);
        public float radius = 0.09f;
        public float height = 0.4f;
        public float mass = 5f;
        public JointLimits limits;
        public bool isRoot;
        /// <summary>Множитель размера визуала (1 = точно по коллайдеру).</summary>
        public Vector3 visualMultiplier = Vector3.one;
        /// <summary>Красить "акцентным" материалом (мишень на груди манекена).</summary>
        public bool accent;
    }

    /// <summary>Описание скелета (15 костей) для роста 1.8 м, масштабируется.</summary>
    public class RagdollProfile
    {
        public string name = "Humanoid";
        public float height = 1.8f;
        public float totalMass = 78f;
        public List<BoneSpec> bones = new List<BoneSpec>();

        public static RagdollProfile Humanoid(float height = 1.8f, float totalMass = 78f)
        {
            var p = new RagdollProfile { height = height, totalMass = totalMass };
            float s = height / 1.8f;

            // ---------------- корпус ----------------
            var hips = new BoneSpec
            {
                name = "Hips", parent = null, isRoot = true,
                localPos = new Vector3(0f, 0.95f, 0f) * s,
                kind = BoneColliderKind.Box,
                colliderSize = new Vector3(0.34f, 0.26f, 0.22f) * s,
                colliderCenter = new Vector3(0f, 0.11f, 0f) * s,
                mass = 13f
            };
            p.bones.Add(hips);

            var spine = new BoneSpec
            {
                name = "Spine", parent = "Hips",
                localPos = new Vector3(0f, 1.14f, 0f) * s,
                kind = BoneColliderKind.Box,
                colliderSize = new Vector3(0.32f, 0.28f, 0.21f) * s,
                colliderCenter = new Vector3(0f, 0.12f, 0f) * s,
                mass = 11f,
                limits = JointLimits.Create(-22f, 22f, 16f, 18f, 520f, 60f)
            };
            p.bones.Add(spine);

            var chest = new BoneSpec
            {
                name = "Chest", parent = "Spine",
                localPos = new Vector3(0f, 1.38f, 0f) * s,
                kind = BoneColliderKind.Box,
                colliderSize = new Vector3(0.40f, 0.30f, 0.24f) * s,
                colliderCenter = new Vector3(0f, 0.13f, 0f) * s,
                mass = 14f,
                accent = true,
                limits = JointLimits.Create(-24f, 24f, 18f, 18f, 560f, 62f)
            };
            p.bones.Add(chest);

            var head = new BoneSpec
            {
                name = "Head", parent = "Chest",
                localPos = new Vector3(0f, 1.60f, 0f) * s,
                kind = BoneColliderKind.Sphere,
                radius = 0.115f * s,
                colliderCenter = new Vector3(0f, 0.125f * s, 0.012f * s),
                mass = 4.6f,
                limits = JointLimits.Create(-32f, 32f, 45f, 25f, 300f, 34f)
            };
            p.bones.Add(head);

            // ---------------- руки ----------------
            for (int side = -1; side <= 1; side += 2)
            {
                string suf = side > 0 ? "Right" : "Left";
                float x = side * 0.235f * s;

                p.bones.Add(new BoneSpec
                {
                    name = "UpperArm" + suf, parent = "Chest",
                    localPos = new Vector3(x, 1.50f * s, 0f),
                    kind = BoneColliderKind.Capsule, direction = 0,
                    radius = 0.055f * s, height = 0.27f * s,
                    colliderCenter = new Vector3(side * 0.135f * s, 0f, 0f),
                    mass = 3.0f,
                    limits = JointLimits.Create(-55f, 55f, 85f, 80f, 240f, 30f)
                });

                p.bones.Add(new BoneSpec
                {
                    name = "LowerArm" + suf, parent = "UpperArm" + suf,
                    localPos = new Vector3(side * 0.50f * s, 1.50f * s, 0f),
                    kind = BoneColliderKind.Capsule, direction = 0,
                    radius = 0.048f * s, height = 0.25f * s,
                    colliderCenter = new Vector3(side * 0.125f * s, 0f, 0f),
                    mass = 2.0f,
                    limits = JointLimits.Create(-70f, 70f, 25f, 105f, 180f, 24f)
                });

                p.bones.Add(new BoneSpec
                {
                    name = "Hand" + suf, parent = "LowerArm" + suf,
                    localPos = new Vector3(side * 0.75f * s, 1.50f * s, 0f),
                    kind = BoneColliderKind.Box,
                    colliderSize = new Vector3(0.11f, 0.06f, 0.10f) * s,
                    colliderCenter = new Vector3(side * 0.055f * s, 0f, 0f),
                    mass = 0.7f,
                    limits = JointLimits.Create(-30f, 30f, 30f, 35f, 90f, 14f)
                });
            }

            // ---------------- ноги ----------------
            for (int side = -1; side <= 1; side += 2)
            {
                string suf = side > 0 ? "Right" : "Left";
                float x = side * 0.105f * s;

                p.bones.Add(new BoneSpec
                {
                    name = "UpLeg" + suf, parent = "Hips",
                    localPos = new Vector3(x, 0.92f * s, 0f),
                    kind = BoneColliderKind.Capsule, direction = 1,
                    radius = 0.078f * s, height = 0.45f * s,
                    colliderCenter = new Vector3(0f, -0.215f * s, 0f),
                    mass = 9.5f,
                    limits = JointLimits.Create(-85f, 40f, 32f, 34f, 420f, 50f)
                });

                p.bones.Add(new BoneSpec
                {
                    name = "LowLeg" + suf, parent = "UpLeg" + suf,
                    localPos = new Vector3(x, 0.50f * s, 0f),
                    kind = BoneColliderKind.Capsule, direction = 1,
                    radius = 0.062f * s, height = 0.42f * s,
                    colliderCenter = new Vector3(0f, -0.20f * s, 0f),
                    mass = 4.6f,
                    limits = JointLimits.Create(-100f, 8f, 8f, 8f, 320f, 40f)
                });

                p.bones.Add(new BoneSpec
                {
                    name = "Foot" + suf, parent = "LowLeg" + suf,
                    localPos = new Vector3(x, 0.10f * s, 0f),
                    kind = BoneColliderKind.Box,
                    colliderSize = new Vector3(0.11f, 0.09f, 0.27f) * s,
                    colliderCenter = new Vector3(0f, -0.03f * s, 0.055f * s),
                    mass = 1.3f,
                    limits = JointLimits.Create(-32f, 32f, 20f, 18f, 200f, 26f)
                });
            }

            return p;
        }
    }

    /// <summary>
    /// Строит рэгдолл целиком из кода: кости, коллайдеры, Rigidbody и
    /// ConfigurableJoint с приводами (за счёт приводов манекен стоит, а при
    /// "сильном ударе" приводы ослабляются и тело улетает и кувыркается).
    /// ConfigurableJoint выбран вместо CharacterJoint: у него явно задаются все
    /// три оси и есть приводы — на слабых устройствах это стабильнее.
    /// </summary>
    public static class RagdollBuilder
    {
        public static RagdollController Build(GameObject owner, RagdollProfile profile,
            Material bodyMaterial, Material accentMaterial, bool addVisuals = true, int layer = -1)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (profile == null) profile = RagdollProfile.Humanoid();

            var root = new GameObject("Ragdoll_" + profile.name);
            root.transform.SetParent(owner.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            if (layer >= 0) root.layer = layer;

            var byName = new Dictionary<string, Transform>();
            var rbByName = new Dictionary<string, Rigidbody>();
            var bones = new List<Transform>();
            var rbs = new List<Rigidbody>();
            var joints = new List<ConfigurableJoint>();
            var colliders = new List<Collider>();
            var visuals = new List<Transform>();

            float rawMass = 0f;
            foreach (var b in profile.bones) rawMass += Mathf.Max(0.01f, b.mass);
            float massScale = rawMass > 0f ? profile.totalMass / rawMass : 1f;

            foreach (var spec in profile.bones)
            {
                Transform parentTf = (spec.parent != null && byName.TryGetValue(spec.parent, out var pt))
                    ? pt
                    : root.transform;

                var go = new GameObject(spec.name);
                go.transform.SetParent(parentTf, false);
                if (layer >= 0) go.layer = layer;

                // localPos задан в пространстве корня рэгдолла → пересчитываем в мировые
                go.transform.position = root.transform.TransformPoint(spec.localPos);
                go.transform.rotation = root.transform.rotation;

                var col = AddCollider(go, spec);
                if (col != null) col.sharedMaterial = PhysicMaterials.Flesh;
                colliders.Add(col);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.05f, spec.mass * massScale);
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.maxAngularVelocity = SandboxConfig.MaxAngularVelocity;
                rb.sleepThreshold = SandboxConfig.SleepThreshold;
                rb.maxDepenetrationVelocity = SandboxConfig.MaxDepenetrationVelocity;
                rb.solverIterations = 6;
                rb.solverVelocityIterations = 1;
                rb.freezeRotation = false;
                rbs.Add(rb);

                if (spec.parent != null && rbByName.TryGetValue(spec.parent, out var parentRb) && spec.limits != null)
                {
                    joints.Add(AddJoint(go, parentRb, spec.limits));
                }

                if (addVisuals)
                {
                    var v = AddVisual(go, spec, spec.accent && accentMaterial != null ? accentMaterial : bodyMaterial);
                    if (v != null)
                    {
                        if (layer >= 0) v.layer = layer;
                        visuals.Add(v.transform);
                    }
                }

                byName[spec.name] = go.transform;
                rbByName[spec.name] = rb;
                bones.Add(go.transform);
            }

            // границы коллайдеров нужны актуальными до расчёта самопересечений
            Physics.SyncTransforms();
            int ignored = IgnoreOverlappingColliders(colliders);

            var controller = owner.GetComponent<RagdollController>();
            if (controller == null) controller = owner.AddComponent<RagdollController>();
            controller.Setup(root.transform, bones.ToArray(), rbs.ToArray(), joints.ToArray(),
                colliders.ToArray(), byName, profile.totalMass, ignored);
            return controller;
        }

        static ConfigurableJoint AddJoint(GameObject go, Rigidbody parentRb, JointLimits lim)
        {
            var joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            // ВАЖНО: сначала нижний предел, потом верхний (иначе Unity его зажмёт).
            joint.lowAngularXLimit = NewLimit(Mathf.Min(lim.angularXLow, 0f));
            joint.highAngularXLimit = NewLimit(Mathf.Max(lim.angularXHigh, 0f));
            joint.angularYLimit = NewLimit(Mathf.Max(0.5f, lim.angularY));
            joint.angularZLimit = NewLimit(Mathf.Max(0.5f, lim.angularZ));

            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            var drive = new JointDrive
            {
                positionSpring = lim.spring,
                positionDamper = lim.damper,
                maximumForce = float.IsInfinity(lim.maxForce) || float.IsNaN(lim.maxForce)
                    ? float.PositiveInfinity
                    : Mathf.Max(0.01f, lim.maxForce)
            };
            joint.angularXDrive = drive;
            joint.angularYZDrive = drive;
            joint.targetRotation = Quaternion.identity;
            joint.targetAngularVelocity = Vector3.zero;

            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.06f;
            joint.projectionAngle = 12f;
            joint.enablePreprocessing = false;
            joint.enableCollision = false;
            joint.breakForce = float.PositiveInfinity;
            joint.breakTorque = float.PositiveInfinity;
            joint.massScale = 1f;
            joint.connectedMassScale = 1f;
            return joint;
        }

        static SoftJointLimit NewLimit(float limit)
        {
            float abs = Mathf.Abs(limit);
            return new SoftJointLimit
            {
                limit = limit,
                bounciness = 0f,
                contactDistance = Mathf.Min(0.2f, abs * 0.35f + 0.02f)
            };
        }

        static Collider AddCollider(GameObject go, BoneSpec spec)
        {
            switch (spec.kind)
            {
                case BoneColliderKind.Box:
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = spec.colliderCenter;
                    bc.size = Vector3.Max(spec.colliderSize, Vector3.one * 0.02f);
                    return bc;
                }
                case BoneColliderKind.Sphere:
                {
                    var sc = go.AddComponent<SphereCollider>();
                    sc.center = spec.colliderCenter;
                    sc.radius = Mathf.Max(0.02f, spec.radius);
                    return sc;
                }
                default:
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.center = spec.colliderCenter;
                    cc.direction = Mathf.Clamp(spec.direction, 0, 2);
                    cc.radius = Mathf.Max(0.02f, spec.radius);
                    cc.height = Mathf.Max(cc.radius * 2f + 0.01f, spec.height);
                    return cc;
                }
            }
        }

        static GameObject AddVisual(GameObject go, BoneSpec spec, Material mat)
        {
            PrimitiveType type;
            Vector3 scale;
            Quaternion rot = Quaternion.identity;

            switch (spec.kind)
            {
                case BoneColliderKind.Box:
                    type = PrimitiveType.Cube;
                    scale = spec.colliderSize;
                    break;
                case BoneColliderKind.Sphere:
                    type = PrimitiveType.Sphere;
                    scale = Vector3.one * (spec.radius * 2f);
                    break;
                default:
                    type = PrimitiveType.Capsule;
                    // примитив Capsule: диаметр 1, высота 2, ось — локальный Y
                    scale = new Vector3(spec.radius * 2f, spec.height * 0.5f, spec.radius * 2f);
                    if (spec.direction == 0) rot = Quaternion.Euler(0f, 0f, 90f);
                    else if (spec.direction == 2) rot = Quaternion.Euler(90f, 0f, 0f);
                    break;
            }

            scale = Vector3.Scale(scale, spec.visualMultiplier);
            scale = Vector3.Max(scale, Vector3.one * 0.01f);

            var visual = GameObject.CreatePrimitive(type);
            visual.name = spec.name + "_Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = spec.colliderCenter;
            visual.transform.localRotation = rot;
            visual.transform.localScale = scale;

            var col = visual.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            var mr = visual.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
            return visual;
        }

        /// <summary>
        /// Отключает столкновения костей, которые пересекаются в стартовой позе
        /// (соседние сегменты всегда перекрываются в районе сустава — иначе рэгдолл
        /// "взрывается" в первые же кадры симуляции).
        /// </summary>
        static int IgnoreOverlappingColliders(List<Collider> colliders)
        {
            int count = 0;
            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    var a = colliders[i];
                    var b = colliders[j];
                    if (a == null || b == null) continue;
                    if (a.attachedRigidbody != null && a.attachedRigidbody == b.attachedRigidbody) continue;

                    Bounds ba = a.bounds;
                    Bounds bb = b.bounds;
                    ba.Expand(-0.02f);
                    bb.Expand(-0.02f);
                    if (ba.Intersects(bb))
                    {
                        Physics.IgnoreCollision(a, b, true);
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
