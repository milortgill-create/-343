using System.Collections.Generic;
using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Подсветка цели взаимодействия: мягкое свечение через MaterialPropertyBlock
    /// (не плодит материалы и не ломает переиспользование шейдеров).
    /// </summary>
    public class TargetHighlighter
    {
        readonly List<Renderer> m_Current = new List<Renderer>();
        readonly MaterialPropertyBlock m_Mpb = new MaterialPropertyBlock();
        Renderer[] m_Buffer = new Renderer[8];

        Color m_Color = new Color(0.15f, 0.85f, 1.0f);
        float m_Pulse;

        public void SetColor(Color c) { m_Color = c; }

        public void SetTarget(GameObject target)
        {
            Clear();
            if (target == null) return;
            int count = target.GetComponentsInChildren(m_Current);
            if (count == 0) return;
            // оставляем только видимые рендереры
            for (int i = m_Current.Count - 1; i >= 0; i--)
            {
                if (m_Current[i] == null) m_Current.RemoveAt(i);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < m_Current.Count; i++)
            {
                var r = m_Current[i];
                if (r == null) continue;
                m_Mpb.Clear();
                r.SetPropertyBlock(m_Mpb);
            }
            m_Current.Clear();
        }

        public void Tick(float deltaTime)
        {
            if (m_Current.Count == 0) return;
            m_Pulse += deltaTime * 5.5f;
            float k = 0.55f + 0.45f * Mathf.Sin(m_Pulse);
            Color emission = m_Color * (0.55f * k);
            emission.a = 1f;
            for (int i = 0; i < m_Current.Count; i++)
            {
                var r = m_Current[i];
                if (r == null) continue;
                m_Mpb.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(m_Mpb);
            }
        }

        public bool HasTarget => m_Current.Count > 0;
    }
}
