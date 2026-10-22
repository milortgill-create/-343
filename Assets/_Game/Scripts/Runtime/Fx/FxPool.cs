using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Пыль/искры от ударов. Всё на пуле из десятка объектов с процедурным
    /// прозрачным материалом — никаких систем частиц и внешних шейдеров.
    /// </summary>
    public class FxPool : MonoBehaviour
    {
        public static FxPool Instance { get; private set; }

        public int poolSize = 14;
        public Material puffMaterial;

        readonly Queue<PuffAnim> m_Free = new Queue<PuffAnim>();
        readonly List<PuffAnim> m_All = new List<PuffAnim>();
        MaterialPropertyBlock m_Mpb;

        void Awake()
        {
            Instance = this;
            m_Mpb = new MaterialPropertyBlock();
            if (puffMaterial == null)
            {
                puffMaterial = MaterialFactory.Create("DustPuff", null, new Color(0.75f, 0.72f, 0.66f, 0.35f),
                    0f, 0.1f, transparent: true, alpha: 0.35f);
            }
            for (int i = 0; i < poolSize; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "DustPuff_" + i;
                go.transform.SetParent(transform, false);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = puffMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
                go.layer = Layers.Hands; // не участвует в raycast'ах
                var puff = go.AddComponent<PuffAnim>();
                puff.Owner = this;
                go.SetActive(false);
                m_Free.Enqueue(puff);
                m_All.Add(puff);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void SpawnDust(Vector3 position, Vector3 normal, float intensity)
        {
            if (Instance == null) return;
            int puffs = Mathf.Clamp(Mathf.RoundToInt(1 + intensity * 3), 1, 4);
            for (int i = 0; i < puffs; i++) Instance.Spawn(position, normal, intensity);
        }

        void Spawn(Vector3 position, Vector3 normal, float intensity)
        {
            PuffAnim puff = m_Free.Count > 0 ? m_Free.Dequeue() : null;
            if (puff == null)
            {
                // пул исчерпан — переиспользуем самый старый
                puff = m_All.Count > 0 ? m_All[0] : null;
                if (puff == null) return;
            }

            puff.transform.position = position + normal * 0.03f + Random.insideUnitSphere * 0.05f;
            puff.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
            puff.transform.localScale = Vector3.one * 0.05f;
            puff.Launch(0.10f + intensity * 0.22f, 0.28f + intensity * 0.30f, normal);
            puff.gameObject.SetActive(true);
        }

        public void Release(PuffAnim puff)
        {
            puff.gameObject.SetActive(false);
            m_Free.Enqueue(puff);
        }

        public MaterialPropertyBlock Mpb => m_Mpb;
    }

    public class PuffAnim : MonoBehaviour
    {
        public FxPool Owner;
        public float maxAlpha = 0.45f;

        float m_TargetScale;
        float m_Life;
        float m_Age;
        Vector3 m_Dir;
        Renderer m_Renderer;
        MaterialPropertyBlock m_Mpb;
        Color m_Color = new Color(0.78f, 0.75f, 0.69f);

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Mpb = new MaterialPropertyBlock();
        }

        public void Launch(float targetScale, float life, Vector3 dir)
        {
            m_TargetScale = targetScale;
            m_Life = Mathf.Max(0.05f, life);
            m_Age = 0f;
            m_Dir = dir;
            m_Color = new Color(0.78f, 0.75f, 0.69f, maxAlpha);
        }

        void Update()
        {
            m_Age += Time.deltaTime;
            float t = Mathf.Clamp01(m_Age / m_Life);

            transform.localScale = Vector3.one * Mathf.Lerp(0.04f, m_TargetScale, Mathf.Sqrt(t));
            transform.position += m_Dir * (0.35f * (1f - t)) * Time.deltaTime + Vector3.up * (0.22f * (1f - t)) * Time.deltaTime;

            if (m_Renderer != null)
            {
                m_Color.a = maxAlpha * (1f - t) * (1f - t);
                m_Mpb.SetColor("_BaseColor", m_Color);
                m_Mpb.SetColor("_Color", m_Color);
                m_Renderer.SetPropertyBlock(m_Mpb);
            }

            if (t >= 1f && Owner != null) Owner.Release(this);
        }
    }
}
