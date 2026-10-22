using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Разрушаемый предмет (стеклянная бутылка). При ударе выше порога разлетается
    /// на осколки — у каждого своя физика, слой Debris и время жизни, чтобы число
    /// активных тел на карте не росло бесконечно.
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        public float breakSpeed = 5.0f;
        public float shardMass = 0.05f;
        public float shardLifetime = 14f;
        public Material shardMaterial;
        public ImpactSound sound = ImpactSound.Glass;

        static readonly List<GameObject> LiveShards = new List<GameObject>();
        public const int MaxLiveShards = 42;

        bool m_Broken;

        public void TryBreak(float impactSpeed, Vector3 point, Vector3 impulse)
        {
            if (m_Broken) return;
            if (impactSpeed < breakSpeed) return;
            Break(point, impulse);
        }

        public void Break(Vector3 point, Vector3 impulse)
        {
            if (m_Broken) return;
            m_Broken = true;

            int count = Mathf.Clamp(PerformanceTuner.ShardCount, 3, 12);
            Vector3 baseVel = impulse.sqrMagnitude > 0.01f ? impulse / Mathf.Max(0.05f, TotalMass()) : Vector3.zero;
            baseVel = Vector3.ClampMagnitude(baseVel, 6f);

            AudioPool.PlayImpact(sound, point, 1f);

            for (int i = 0; i < count; i++)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "Shard";
                shard.transform.position = point + Random.insideUnitSphere * 0.07f;
                shard.transform.rotation = Random.rotation;
                float sx = Random.Range(0.02f, 0.055f);
                float sy = Random.Range(0.015f, 0.04f);
                float sz = Random.Range(0.02f, 0.07f);
                shard.transform.localScale = new Vector3(sx, sy, sz);
                shard.layer = Layers.Debris;

                var mr = shard.GetComponent<MeshRenderer>();
                if (mr != null && shardMaterial != null) mr.sharedMaterial = shardMaterial;

                var rb = shard.AddComponent<Rigidbody>();
                rb.mass = shardMass;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                PhysicMaterials.Apply(shard, PhysicMaterials.Glass);
                rb.maxAngularVelocity = 18f;
                rb.velocity = baseVel + Random.insideUnitSphere * 2.6f + Vector3.up * Random.Range(0.6f, 1.8f);
                rb.angularVelocity = Random.insideUnitSphere * 12f;

                var reaper = shard.AddComponent<ShardReaper>();
                reaper.lifeTime = shardLifetime;

                LiveShards.Add(shard);
            }

            // осколки-«стёкла» на полу: пара плоских кусочков без физики для вида
            TrimShards();

            Destroy(gameObject);
        }

        public static void TrimShards()
        {
            for (int i = LiveShards.Count - 1; i >= 0; i--)
            {
                if (LiveShards[i] == null) LiveShards.RemoveAt(i);
            }
            while (LiveShards.Count > MaxLiveShards)
            {
                var oldest = LiveShards[0];
                LiveShards.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }
        }

        public static int LiveShardCount
        {
            get
            {
                TrimShards();
                return LiveShards.Count;
            }
        }

        float TotalMass()
        {
            var rb = GetComponent<Rigidbody>();
            return rb != null ? rb.mass : 1f;
        }
    }

    /// <summary>Удаляет осколок по истечении времени жизни.</summary>
    public class ShardReaper : MonoBehaviour
    {
        public float lifeTime = 12f;
        float m_T;

        void Update()
        {
            m_T += Time.deltaTime;
            if (m_T >= lifeTime) Destroy(gameObject);
        }
    }
}
