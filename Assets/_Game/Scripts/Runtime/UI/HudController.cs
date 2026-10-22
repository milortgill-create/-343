using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sandbox
{
    /// <summary>
    /// Весь интерфейс строится кодом: канвас, джойстик, кнопки, прицел, панели.
    /// Спрайты и шрифт — процедурные/встроенные, бинарных ассетов нет.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public InputService input;
        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }

        Text m_Prompt;
        Text m_Stats;
        Text m_Message;
        Text m_HealthValue;
        Image m_HealthFill;
        Image m_CrosshairDot;
        Image m_CrosshairRing;
        RectTransform m_Crosshair;
        Text m_ActionLabel;
        Image m_SprintImage;
        VirtualJoystick m_Joystick;
        Sprite m_Circle;
        Sprite m_Ring;
        Sprite m_Rounded;
        Font m_Font;

        float m_MessageTimer;
        bool m_SprintOn;

        public static HudController Create(Transform parent, InputService input)
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<HudController>();
            hud.input = input;
            hud.Build();
            return hud;
        }

        public static Font UiFont
        {
            get
            {
                Font f = null;
                try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (System.Exception) { f = null; }
                if (f == null)
                {
                    try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                    catch (System.Exception) { f = null; }
                }
                if (f == null) f = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Roboto", "sans-serif" }, 32);
                return f;
            }
        }

        void Build()
        {
            m_Font = UiFont;
            m_Circle = MakeSprite(ProceduralTextures.Circle(128, Color.white, 0.05f));
            m_Ring = MakeSprite(ProceduralTextures.Ring(128, Color.white, 0.10f));
            m_Rounded = MakeSprite(ProceduralTextures.RoundedRect(64, 64, 12, Color.white));

            // ---------- канвас ----------
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            Canvas.pixelPerfect = false;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)canvasGo.transform;

            // ---------- EventSystem ----------
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            BuildJoystick();
            BuildButtons();
            BuildCrosshair();
            BuildInfoPanels();
        }

        static Sprite MakeSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------
        void BuildJoystick()
        {
            var zone = NewUi("JoystickZone", Root, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(1f, 1f));
            var zoneRt = zone.rectTransform;
            zoneRt.offsetMin = new Vector2(0f, 0f);
            zoneRt.offsetMax = new Vector2(0f, 0f);
            zoneRt.anchorMin = new Vector2(0f, 0f);
            zoneRt.anchorMax = new Vector2(1f, 1f);
            zoneRt.sizeDelta = Vector2.zero;

            var bg = NewUi("JoystickBg", zoneRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(210f, 210f));
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.sprite = m_Circle;
            bgImage.color = new Color(1f, 1f, 1f, 0.16f);
            bgImage.raycastTarget = false;

            var handle = NewUi("JoystickHandle", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(92f, 92f));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = m_Circle;
            handleImage.color = new Color(1f, 1f, 1f, 0.42f);
            handleImage.raycastTarget = false;

            var hint = NewUi("JoystickHint", zoneRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(180f, 120f), new Vector2(240f, 60f));
            var hintText = hint.gameObject.AddComponent<Text>();
            hintText.font = m_Font;
            hintText.text = "ДВИЖЕНИЕ";
            hintText.fontSize = 22;
            hintText.alignment = TextAnchor.MiddleCenter;
            hintText.color = new Color(1f, 1f, 1f, 0.22f);
            hintText.raycastTarget = false;

            var joyGo = new GameObject("VirtualJoystick");
            joyGo.transform.SetParent(zoneRt, false);
            m_Joystick = joyGo.AddComponent<VirtualJoystick>();
            m_Joystick.root = zoneRt;
            m_Joystick.background = bg;
            m_Joystick.handle = handle;
            m_Joystick.radius = 96f;
            m_Joystick.Init();

            if (input != null) input.joystick = m_Joystick;
        }

        // ------------------------------------------------------------------
        void BuildButtons()
        {
            // правый нижний угол: «Действие» + «Прыжок» + «Бег»
            var action = CreateButton("Action", Root, new Vector2(1f, 0f), new Vector2(-130f, 120f),
                new Vector2(168f, 168f), new Color(0.95f, 0.62f, 0.20f, 0.85f), "ДЕЙСТВИЕ", 24,
                () => { if (input != null) input.QueueAction(); });
            m_ActionLabel = action.label;

            CreateButton("Jump", Root, new Vector2(1f, 0f), new Vector2(-268f, 92f),
                new Vector2(104f, 104f), new Color(0.35f, 0.70f, 0.95f, 0.75f), "ПРЫЖОК", 17,
                () => { if (input != null) input.QueueJump(); });

            var sprint = CreateButton("Sprint", Root, new Vector2(1f, 0f), new Vector2(-268f, 210f),
                new Vector2(104f, 104f), new Color(0.45f, 0.85f, 0.50f, 0.62f), "БЕГ", 18,
                () =>
                {
                    m_SprintOn = !m_SprintOn;
                    if (input != null) input.SetSprint(m_SprintOn);
                });
            m_SprintImage = sprint.image;

            // правый верхний угол: служебные кнопки
            CreateButton("Phone", Root, new Vector2(1f, 1f), new Vector2(-90f, -60f),
                new Vector2(96f, 76f), new Color(0.60f, 0.55f, 0.95f, 0.72f), "ТЕЛЕФОН", 16,
                () => { if (input != null) input.QueuePhone(); });

            CreateButton("Drop", Root, new Vector2(1f, 1f), new Vector2(-200f, -60f),
                new Vector2(96f, 76f), new Color(0.75f, 0.75f, 0.78f, 0.62f), "ВЫПУСТИТЬ", 14,
                () => { if (input != null) input.QueueDrop(); });

            CreateButton("Reset", Root, new Vector2(1f, 1f), new Vector2(-310f, -60f),
                new Vector2(96f, 76f), new Color(0.95f, 0.45f, 0.40f, 0.62f), "РЕСПАВН", 15,
                () => { if (input != null) input.QueueReset(); });

            CreateButton("Stress", Root, new Vector2(0f, 1f), new Vector2(330f, -60f),
                new Vector2(150f, 68f), new Color(0.95f, 0.85f, 0.30f, 0.55f), "СТРЕСС-ТЕСТ", 15,
                () => { if (input != null) input.QueueStressTest(); });

            // регистрируем кнопки как «поглотители» тачей, чтобы свайп по кнопке не крутил камеру
            if (input != null)
            {
                input.uiBlockers.Clear();
                foreach (var rt in Root.GetComponentsInChildren<RectTransform>())
                {
                    if (rt.GetComponent<Button>() != null) input.uiBlockers.Add(rt);
                }
            }
        }

        public struct ButtonRefs
        {
            public Image image;
            public Text label;
            public RectTransform rect;
        }

        ButtonRefs CreateButton(string name, RectTransform parent, Vector2 anchor, Vector2 anchoredPos,
            Vector2 size, Color color, string label, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var rt = NewUi(name, parent, anchor, anchor, anchoredPos, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = m_Rounded;
            image.color = color;
            image.raycastTarget = true;

            var button = rt.gameObject.AddComponent<Button>();
            var transition = button.colors;
            transition.normalColor = Color.white;
            transition.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            transition.pressedColor = new Color(0.75f, 0.85f, 1f, 1f);
            transition.selectedColor = Color.white;
            transition.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = transition;
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);

            var labelRt = NewUi("Label", rt, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var text = labelRt.gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.text = label;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.08f, 0.09f, 0.11f, 0.95f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return new ButtonRefs { image = image, label = text, rect = rt };
        }

        // ------------------------------------------------------------------
        void BuildCrosshair()
        {
            m_Crosshair = NewUi("Crosshair", Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(44f, 44f));

            var ringGo = NewUi("Ring", m_Crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(44f, 44f));
            m_CrosshairRing = ringGo.gameObject.AddComponent<Image>();
            m_CrosshairRing.sprite = m_Ring;
            m_CrosshairRing.color = new Color(1f, 1f, 1f, 0.72f);
            m_CrosshairRing.raycastTarget = false;

            var dotGo = NewUi("Dot", m_Crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(6f, 6f));
            m_CrosshairDot = dotGo.gameObject.AddComponent<Image>();
            m_CrosshairDot.sprite = m_Circle;
            m_CrosshairDot.color = new Color(1f, 1f, 1f, 0.9f);
            m_CrosshairDot.raycastTarget = false;
        }

        void BuildInfoPanels()
        {
            // верхняя левая панель со статистикой
            var panel = NewUi("StatsPanel", Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(470f, 190f));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = m_Rounded;
            panelImage.color = new Color(0.05f, 0.06f, 0.08f, 0.52f);
            panelImage.raycastTarget = false;

            var statsRt = NewUi("Stats", panel, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            statsRt.offsetMin = new Vector2(14f, 8f);
            statsRt.offsetMax = new Vector2(-14f, -8f);
            m_Stats = statsRt.gameObject.AddComponent<Text>();
            m_Stats.font = m_Font;
            m_Stats.fontSize = 19;
            m_Stats.lineSpacing = 1.12f;
            m_Stats.alignment = TextAnchor.UpperLeft;
            m_Stats.color = new Color(0.92f, 0.95f, 1f, 0.95f);
            m_Stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_Stats.verticalOverflow = VerticalWrapMode.Overflow;
            m_Stats.raycastTarget = false;
            m_Stats.text = "Инициализация…";

            // полоса здоровья
            var healthBar = NewUi("HealthBar", Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -222f), new Vector2(300f, 20f));
            var hbBg = healthBar.gameObject.AddComponent<Image>();
            hbBg.sprite = m_Rounded;
            hbBg.color = new Color(0.05f, 0.06f, 0.08f, 0.55f);
            hbBg.raycastTarget = false;

            var fillRt = NewUi("Fill", healthBar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, 0f), new Vector2(292f, 12f));
            m_HealthFill = fillRt.gameObject.AddComponent<Image>();
            m_HealthFill.sprite = m_Rounded;
            m_HealthFill.color = new Color(0.35f, 0.90f, 0.45f, 0.95f);
            m_HealthFill.type = Image.Type.Filled;
            m_HealthFill.fillMethod = Image.FillMethod.Horizontal;
            m_HealthFill.fillOrigin = 0;
            m_HealthFill.fillAmount = 1f;
            m_HealthFill.raycastTarget = false;

            var hvRt = NewUi("HealthValue", healthBar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(90f, 18f));
            m_HealthValue = hvRt.gameObject.AddComponent<Text>();
            m_HealthValue.font = m_Font;
            m_HealthValue.fontSize = 16;
            m_HealthValue.alignment = TextAnchor.MiddleRight;
            m_HealthValue.color = new Color(1f, 1f, 1f, 0.9f);
            m_HealthValue.raycastTarget = false;
            m_HealthValue.text = "100";

            // подсказка действия — по центру снизу
            var promptRt = NewUi("Prompt", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 132f), new Vector2(760f, 44f));
            m_Prompt = promptRt.gameObject.AddComponent<Text>();
            m_Prompt.font = m_Font;
            m_Prompt.fontSize = 30;
            m_Prompt.alignment = TextAnchor.MiddleCenter;
            m_Prompt.color = new Color(1f, 1f, 1f, 0.92f);
            m_Prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Prompt.raycastTarget = false;

            // всплывающие сообщения
            var msgRt = NewUi("Message", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 190f), new Vector2(900f, 40f));
            m_Message = msgRt.gameObject.AddComponent<Text>();
            m_Message.font = m_Font;
            m_Message.fontSize = 24;
            m_Message.alignment = TextAnchor.MiddleCenter;
            m_Message.color = new Color(1f, 0.92f, 0.55f, 0f);
            m_Message.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Message.raycastTarget = false;
        }

        static RectTransform NewUi(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
            return rt;
        }

        // ------------------------------------------------------------------
        // Обновление
        // ------------------------------------------------------------------
        public void SetPrompt(string text, InteractionKind kind)
        {
            if (m_Prompt == null) return;
            m_Prompt.text = text ?? string.Empty;
            m_Prompt.color = kind == InteractionKind.None
                ? new Color(1f, 1f, 1f, 0.35f)
                : kind == InteractionKind.Punch || kind == InteractionKind.StrikeWithItem
                    ? new Color(1f, 0.55f, 0.40f, 0.98f)
                    : new Color(0.62f, 0.95f, 1f, 0.98f);
        }

        public void SetCrosshair(bool active, InteractionKind kind)
        {
            if (m_CrosshairRing == null || m_Crosshair == null) return;
            Color c = active
                ? (kind == InteractionKind.Punch || kind == InteractionKind.StrikeWithItem
                    ? new Color(1f, 0.38f, 0.28f, 0.95f)
                    : new Color(0.35f, 0.95f, 1f, 0.95f))
                : new Color(1f, 1f, 1f, 0.55f);
            m_CrosshairRing.color = c;
            if (m_CrosshairDot != null) m_CrosshairDot.color = new Color(c.r, c.g, c.b, 1f);

            float scale = active ? 1.28f : 1f;
            m_Crosshair.localScale = Vector3.Lerp(m_Crosshair.localScale, Vector3.one * scale, Time.unscaledDeltaTime * 12f);
        }

        public void SetStats(string text)
        {
            if (m_Stats != null) m_Stats.text = text;
        }

        public void SetHealth(float health01, string text)
        {
            if (m_HealthFill != null)
            {
                m_HealthFill.fillAmount = Mathf.Clamp01(health01);
                m_HealthFill.color = Color.Lerp(new Color(0.95f, 0.30f, 0.25f), new Color(0.35f, 0.90f, 0.45f),
                    Mathf.Clamp01(health01));
            }
            if (m_HealthValue != null) m_HealthValue.text = text;
        }

        public void SetActionLabel(string text)
        {
            if (m_ActionLabel != null) m_ActionLabel.text = text;
        }

        public void SetSprintVisual(bool on)
        {
            if (m_SprintImage == null) return;
            m_SprintImage.color = on ? new Color(0.45f, 0.95f, 0.55f, 0.9f) : new Color(0.45f, 0.85f, 0.50f, 0.55f);
        }

        public void ShowMessage(string text, float seconds = 2.6f)
        {
            if (m_Message == null) return;
            m_Message.text = text;
            m_MessageTimer = seconds;
        }

        void Update()
        {
            if (m_Message == null) return;
            if (m_MessageTimer > 0f)
            {
                m_MessageTimer -= Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(m_MessageTimer / 0.5f);
                var c = m_Message.color;
                c.a = Mathf.Min(1f, m_MessageTimer > 0.5f ? 1f : a);
                m_Message.color = c;
            }
            else if (m_Message.color.a > 0f)
            {
                var c = m_Message.color;
                c.a = 0f;
                m_Message.color = c;
            }
        }

        public VirtualJoystick Joystick => m_Joystick;
    }
}
