using UnityEngine;
using UnityEngine.UI;

namespace Sandbox
{
    /// <summary>
    /// Плавающий виртуальный джойстик: появляется там, где игрок коснулся
    /// левой половины экрана. Координаты переводятся из экранных в локальные
    /// координаты канваса, поэтому работает при любой ориентации и DPI.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour
    {
        public RectTransform root;          // полноэкранный RectTransform канваса
        public RectTransform background;
        public RectTransform handle;
        public CanvasGroup canvasGroup;

        public float radius = 95f;
        public float returnSpeed = 14f;
        public float edgePadding = 40f;

        public Vector2 Value { get; private set; }
        public bool IsActive { get; private set; }

        Image m_BgImage;
        Image m_HandleImage;
        Color m_BgColor = new Color(1f, 1f, 1f, 0.16f);
        Color m_HandleColor = new Color(1f, 1f, 1f, 0.42f);

        void Awake()
        {
            CacheImages();
            SetVisualActive(false);
        }

        void CacheImages()
        {
            if (background != null) m_BgImage = background.GetComponent<Image>();
            if (handle != null) m_HandleImage = handle.GetComponent<Image>();
        }

        /// <summary>
        /// Вызывается после того, как HUD назначил background/handle/root
        /// (Awake отрабатывает раньше, чем ссылки проставлены из кода).
        /// </summary>
        public void Init()
        {
            CacheImages();
            Value = Vector2.zero;
            IsActive = false;
            SetVisualActive(false);
        }

        public void Begin(Vector2 screenPos)
        {
            if (root == null || background == null) return;
            IsActive = true;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, null, out local)) return;

            // не даём джойстику вылезти за край экрана
            Rect r = root.rect;
            float minX = r.xMin + radius + edgePadding;
            float maxX = r.xMin + r.width * 0.5f - radius - edgePadding;
            float minY = r.yMin + radius + edgePadding;
            float maxY = r.yMax - radius - edgePadding;
            local.x = Mathf.Clamp(local.x, minX, Mathf.Max(minX, maxX));
            local.y = Mathf.Clamp(local.y, minY, Mathf.Max(minY, maxY));

            background.anchoredPosition = local;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
            SetVisualActive(true);
        }

        public void Drag(Vector2 screenPos)
        {
            if (!IsActive || root == null || background == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, null, out local)) return;

            Vector2 delta = local - background.anchoredPosition;
            float len = delta.magnitude;
            if (len > radius) delta = delta * (radius / len);

            if (handle != null) handle.anchoredPosition = delta;
            Value = delta / Mathf.Max(1f, radius);
        }

        public void End()
        {
            IsActive = false;
        }

        void Update()
        {
            if (IsActive) return;
            if (Value.sqrMagnitude > 0.0001f)
            {
                Value = Vector2.MoveTowards(Value, Vector2.zero, returnSpeed * Time.unscaledDeltaTime);
                if (handle != null) handle.anchoredPosition = Value * radius;
            }
            else if (background != null && background.gameObject.activeSelf && m_BgImage != null && m_BgImage.color.a <= 0.01f)
            {
                SetVisualActive(false);
            }
        }

        void SetVisualActive(bool on)
        {
            if (background != null) background.gameObject.SetActive(on || Value.sqrMagnitude > 0.0001f);
            if (handle != null) handle.gameObject.SetActive(on || Value.sqrMagnitude > 0.0001f);
            if (m_BgImage != null) m_BgImage.color = on ? m_BgColor : new Color(m_BgColor.r, m_BgColor.g, m_BgColor.b, 0f);
            if (m_HandleImage != null) m_HandleImage.color = on ? m_HandleColor : new Color(m_HandleColor.r, m_HandleColor.g, m_HandleColor.b, 0f);
        }

        public void SetColors(Color bg, Color handle)
        {
            m_BgColor = bg;
            m_HandleColor = handle;
            SetVisualActive(IsActive);
        }
    }
}
