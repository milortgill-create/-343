using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    public enum RagdollState
    {
        /// <summary>Приводы держат позу, таз закреплён — болванка стоит.</summary>
        Upright,
        /// <summary>Кратковременная реакция на слабый удар (приводы ослаблены).</summary>
        Flinch,
        /// <summary>Полностью свободное тело: отлетает, падает, кувыркается.</summary>
        Limp,
        /// <summary>Рэгдолл выключен.</summary>
        Disabled
    }

    /// <summary>
    /// Управление готовым рэгдоллом: держать позу, принять удар, обмякнуть,
    /// телепортироваться обратно и "встать".
    /// </summary>
    public class RagdollController : MonoBehaviour
    {
        Transform m_Root;
        Transform[] m_Bones = new Transform[0];
        Rigidbody[] m_Bodies = new Rigidbody[0];
        ConfigurableJoint[] m_Joints = new ConfigurableJoint[0];
        Collider[] m_Colliders = new Collider[0];
        Dictionary<string, Transform> m_Map = new Dictionary<string, Transform>();

        Pose[] m_BindPoses = new Pose[0];
        float m_FlinchTimer;
        float m_StandSpring;
        float m_StandDamper;
        bool m_RootWasKinematic;

        public RagdollState State { get; private set; } = RagdollState.Disabled;
        public float TotalMass { get; private set; }
        public int IgnoredSelfCollisions { get; private set; }
        public Transform Head => Find("Head");
        public Transform Chest => Find("Chest");
        public Transform Hips => Find("Hips");
        public Transform[] Bones => m_Bones;
        public Rigidbody[] Bodies => m_Bodies;
        public ConfigurableJoint[] Joints => m_Joints;
        public Collider[] Colliders => m_Colliders;
        public bool IsBuilt => m_Bones != null && m_Bones.Length > 0;

        public struct Pose
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        public void Setup(Transform root, Transform[] bones, Rigidbody[] bodies, ConfigurableJoint[] joints,
            Collider[] colliders, Dictionary<string, Transform> map, float totalMass, int ignoredSelfCollisions)
        {
            m_Root = root;
            m_Bones = bones ?? new Transform[0];
            m_Bodies = bodies ?? new Rigidbody[0];
            m_Joints = joints ?? new ConfigurableJoint[0];
            m_Colliders = colliders ?? new Collider[0];
            m_Map = map ?? new Dictionary<string, Transform>();
            TotalMass = totalMass;
            IgnoredSelfCollisions = ignoredSelfCollisions;

            m_BindPoses = new Pose[m_Bones.Length];
            for (int i = 0; i < m_Bones.Length; i++)
            {
                m_BindPoses[i] = new Pose
                {
                    LocalPosition = m_Bones[i].localPosition,
                    LocalRotation = m_Bones[i].localRotation
                };
            }

            // запоминаем "стоячие" параметры приводов из профиля
            m_StandSpring = 0f;
            m_StandDamper = 0f;
            foreach (var j in m_Joints)
            {
                if (j == null) continue;
                m_StandSpring = Mathf.Max(m_StandSpring, j.angularXDrive.positionSpring);
                m_StandDamper = Mathf.Max(m_StandDamper, j.angularXDrive.positionDamper);
            }
            if (m_StandSpring <= 0f) m_StandSpring = 400f;
            if (m_StandDamper <= 0f) m_StandDamper = 45f;

            m_RootWasKinematic = m_Bodies.Length > 0 && m_Bodies[0].isKinematic;
            State = RagdollState.Disabled;
        }

        public Transform Find(string boneName)
        {
            return m_Map != null && m_Map.TryGetValue(boneName, out var t) ? t : null;
        }

        public Rigidbody FindBody(string boneName)
        {
            var t = Find(boneName);
            return t != null ? t.GetComponent<Rigidbody>() : null;
        }

        /// <summary>Включить рэгдолл в режиме "стоит" (корень закреплён, приводы жёсткие).</summary>
        public void SetUpright(bool rootKinematic = true)
        {
            if (!IsBuilt) return;
            SetEnabled(true);
            SetRootKinematic(rootKinematic);
            SetDriveStrength(m_StandSpring, m_StandDamper);
            State = RagdollState.Upright;
        }

        /// <summary>Обмякнуть: корень свободен, приводы почти не работают.</summary>
        public void GoLimp(float driveSpring = 22f, float driveDamper = 6f)
        {
            if (!IsBuilt) return;
            SetEnabled(true);
            SetRootKinematic(false);
            SetDriveStrength(driveSpring, driveDamper);
            State = RagdollState.Limp;
        }

        /// <summary>Короткая реакция на удар: приводы ослабляются на долю секунды.</summary>
        public void Flinch(float duration = 0.28f, float springScale = 0.25f)
        {
            if (!IsBuilt) return;
            if (State == RagdollState.Limp) return;
            SetDriveStrength(m_StandSpring * springScale, m_StandDamper * 0.6f);
            State = RagdollState.Flinch;
            m_FlinchTimer = duration;
        }

        void Update()
        {
            if (State != RagdollState.Flinch) return;
            m_FlinchTimer -= Time.deltaTime;
            if (m_FlinchTimer <= 0f)
            {
                SetDriveStrength(m_StandSpring, m_StandDamper);
                State = m_RootWasKinematic ? RagdollState.Upright : RagdollState.Limp;
            }
        }

        public void SetEnabled(bool enabled)
        {
            if (m_Root == null) return;
            if (m_Root.gameObject.activeSelf == enabled) return;
            m_Root.gameObject.SetActive(enabled);
            State = enabled ? State : RagdollState.Disabled;
        }

        public bool IsEnabled => m_Root != null && m_Root.gameObject.activeSelf;

        public void SetRootKinematic(bool kinematic)
        {
            m_RootWasKinematic = kinematic;
            for (int i = 0; i < m_Bodies.Length; i++)
            {
                if (m_Bodies[i] == null) continue;
                // закрепляем только таз — остальное держат приводы
                m_Bodies[i].isKinematic = kinematic && i == 0;
            }
        }

        public void SetDriveStrength(float spring, float damper)
        {
            spring = Mathf.Max(0f, spring);
            damper = Mathf.Max(0f, damper);
            foreach (var j in m_Joints)
            {
                if (j == null) continue;
                var d = new JointDrive
                {
                    positionSpring = spring,
                    positionDamper = damper,
                    maximumForce = float.PositiveInfinity
                };
                j.angularXDrive = d;
                j.angularYZDrive = d;
            }
        }

        /// <summary>Приложить импульс к конкретной точке тела (удар).</summary>
        public void AddImpulseAt(Vector3 worldPoint, Vector3 impulse)
        {
            if (!IsBuilt) return;
            Rigidbody best = null;
            float bestSqr = float.MaxValue;
            foreach (var rb in m_Bodies)
            {
                if (rb == null || rb.isKinematic) continue;
                float sqr = (rb.worldCenterOfMass - worldPoint).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = rb; }
            }
            if (best == null)
            {
                foreach (var rb in m_Bodies)
                {
                    if (rb == null) continue;
                    float sqr = (rb.worldCenterOfMass - worldPoint).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = rb; }
                }
            }
            if (best == null) return;
            best.WakeUp();
            best.AddForceAtPosition(impulse, worldPoint, ForceMode.Impulse);
        }

        /// <summary>Импульс всем костям (взрыв/сильный толчок).</summary>
        public void AddImpulseAll(Vector3 impulse)
        {
            foreach (var rb in m_Bodies)
            {
                if (rb == null || rb.isKinematic) continue;
                rb.WakeUp();
                rb.AddForce(impulse, ForceMode.Impulse);
            }
        }

        /// <summary>Суммарная скорость тела — для проверки "летит или уже лежит".</summary>
        public float MaxBodySpeed()
        {
            float max = 0f;
            foreach (var rb in m_Bodies)
            {
                if (rb == null) continue;
                max = Mathf.Max(max, rb.velocity.magnitude);
            }
            return max;
        }

        public bool IsMostlyAsleep()
        {
            foreach (var rb in m_Bodies)
            {
                if (rb != null && !rb.isKinematic && !rb.IsSleeping()) return false;
            }
            return true;
        }

        /// <summary>Мгновенно вернуть тело в стартовую позу (используется при "восстании").</summary>
        public void SnapToBindPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (m_Root == null) return;
            m_Root.position = worldPosition;
            m_Root.rotation = worldRotation;
            for (int i = 0; i < m_Bones.Length && i < m_BindPoses.Length; i++)
            {
                if (m_Bones[i] == null) continue;
                m_Bones[i].localPosition = m_BindPoses[i].LocalPosition;
                m_Bones[i].localRotation = m_BindPoses[i].LocalRotation;
                if (i < m_Bodies.Length && m_Bodies[i] != null)
                {
                    m_Bodies[i].velocity = Vector3.zero;
                    m_Bodies[i].angularVelocity = Vector3.zero;
                }
            }
            Physics.SyncTransforms();
        }

        /// <summary>Плавно поднять тело: сначала телепорт в позу, затем включаем приводы.</summary>
        public void StandUp(Vector3 worldPosition, Quaternion worldRotation, bool rootKinematic = true)
        {
            SetEnabled(true);
            // на время подъёма делаем все кости кинематическими, чтобы их не сбили
            foreach (var rb in m_Bodies)
            {
                if (rb != null) { rb.isKinematic = true; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            SnapToBindPose(worldPosition, worldRotation);
            SetDriveStrength(m_StandSpring, m_StandDamper);
            foreach (var rb in m_Bodies)
            {
                if (rb != null) rb.isKinematic = false;
            }
            SetRootKinematic(rootKinematic);
            State = RagdollState.Upright;
        }

        /// <summary>Минимальная Y всех костей — для контроля "не провалилось ли тело".</summary>
        public float LowestPoint()
        {
            float min = float.MaxValue;
            foreach (var c in m_Colliders)
            {
                if (c == null) continue;
                min = Mathf.Min(min, c.bounds.min.y);
            }
            return min == float.MaxValue ? 0f : min;
        }

        public void SetLayer(int layer)
        {
            if (m_Root == null) return;
            GameUtil.SetLayerRecursive(m_Root.gameObject, layer);
        }

        public string Describe()
        {
            return $"bones={m_Bones.Length} rigidbodies={m_Bodies.Length} joints={m_Joints.Length} " +
                   $"mass={TotalMass:F1}kg ignoredSelfCollisions={IgnoredSelfCollisions} state={State}";
        }
    }
}
