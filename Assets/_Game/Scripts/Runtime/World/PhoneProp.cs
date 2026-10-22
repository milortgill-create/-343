using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// 3D-смартфон в руках игрока. Полноценный физический предмет: его можно
    /// взять, бросить (упадёт на пол и покатится), разбить экран при сильном
    /// ударе и подобрать снова. Экран живой: часы, заряд и счётчик FPS.
    /// </summary>
    public class PhoneProp : MonoBehaviour
    {
        public Grabbable Grabbable { get; private set; }
        public Renderer ScreenRenderer { get; private set; }
        public bool ScreenCracked { get; private set; }
        public float Battery { get; private set; } = 0.87f;
        public int ScreenWidth = 256;
        public int ScreenHeight = 512;

        Texture2D m_ScreenTex;
        Material m_ScreenMat;
        float m_RedrawTimer;
        float m_CrackSpeed;

        public static PhoneProp Create(Transform parent, Vector3 position)
        {
            var root = GameUtil.New("Phone", parent);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, 25f, 0f);

            // корпус
            var body = GameUtil.NewPrimitive(PrimitiveType.Cube, "Body", root.transform,
                Vector3.zero, new Vector3(0.075f, 0.0095f, 0.155f));
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.PhoneBody;
            var bodyCol = body.GetComponent<BoxCollider>();
            if (bodyCol != null) Object.Destroy(bodyCol);

            // рамка-бампер
            var bumper = GameUtil.NewPrimitive(PrimitiveType.Cube, "Bumper", root.transform,
                new Vector3(0f, 0.0005f, 0f), new Vector3(0.079f, 0.0075f, 0.159f));
            bumper.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.RustyMetal;
            GameUtil.StripCollider(bumper);

            // экран (лицевая сторона — вверх по Y)
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(root.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0.0051f, 0f);
            screen.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // quad смотрит вверх (+Y)
            screen.transform.localScale = new Vector3(0.068f, 0.146f, 1f);
            GameUtil.StripCollider(screen);
            var smr = screen.GetComponent<MeshRenderer>();

            // блок камер
            var camBump = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "CameraBump", root.transform,
                new Vector3(-0.021f, -0.0058f, 0.055f), new Vector3(0.017f, 0.0016f, 0.017f),
                Quaternion.Euler(90f, 0f, 0f));
            camBump.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Plastic;
            GameUtil.StripCollider(camBump);

            var lens = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Lens", root.transform,
                new Vector3(-0.021f, -0.0074f, 0.055f), new Vector3(0.010f, 0.0012f, 0.010f),
                Quaternion.Euler(90f, 0f, 0f));
            lens.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.Glass;
            GameUtil.StripCollider(lens);

            // кнопки
            var power = GameUtil.NewPrimitive(PrimitiveType.Cube, "PowerButton", root.transform,
                new Vector3(0.0385f, 0.0005f, 0.028f), new Vector3(0.0022f, 0.004f, 0.026f));
            power.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.RustyMetal;
            GameUtil.StripCollider(power);

            var vol = GameUtil.NewPrimitive(PrimitiveType.Cube, "VolumeButton", root.transform,
                new Vector3(-0.0385f, 0.0005f, 0.030f), new Vector3(0.0022f, 0.004f, 0.042f));
            vol.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.RustyMetal;
            GameUtil.StripCollider(vol);

            // единый коллайдер на корпус
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.079f, 0.0105f, 0.159f);
            col.center = Vector3.zero;

            // физика и взаимодействие — как у обычного предмета
            GameUtil.SetLayerRecursive(root, Layers.Prop);
            var rb = GameUtil.AddRigidbody(root, 0.19f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var grab = root.AddComponent<Grabbable>();
            grab.displayName = "Смартфон";
            grab.impactSound = ImpactSound.Metal;
            grab.damageMultiplier = 0.6f;
            grab.heaviness = 0.15f;
            grab.RememberSpawn();

            var impact = root.AddComponent<ImpactHandler>();
            impact.minImpactSpeed = 1.0f;
            impact.dustSpeed = 6.5f;
            impact.damageScale = 2.5f;

            var phone = root.AddComponent<PhoneProp>();
            phone.AttachScreen(smr);

            GameUtil.SettleSpawn(root, SandboxConfig.FloorTopY);
            if (OutOfBoundsRecovery.Instance != null) OutOfBoundsRecovery.Instance.Register(grab);
            return phone;
        }

        public void AttachScreen(Renderer screenRenderer)
        {
            ScreenRenderer = screenRenderer;
            m_ScreenTex = ProceduralTextures.PhoneScreen(ScreenWidth, ScreenHeight, "12:00", "SANDBOX", Battery, false);
            m_ScreenMat = MaterialFactory.Create("PhoneScreen", null, Color.white, 0f, 0.85f,
                emission: new Color(0.85f, 0.92f, 1.0f), emissionStrength: 1.35f);
            m_ScreenMat.SetTexture("_BaseMap", m_ScreenTex);
            if (m_ScreenMat.HasProperty("_EmissionMap")) m_ScreenMat.SetTexture("_EmissionMap", m_ScreenTex);
            screenRenderer.sharedMaterial = m_ScreenMat;
        }

        void Update()
        {
            if (m_ScreenTex == null) return;

            m_RedrawTimer -= Time.unscaledDeltaTime;
            Battery = Mathf.Max(0.05f, Battery - Time.unscaledDeltaTime * 0.0006f);
            if (m_RedrawTimer > 0f) return;
            m_RedrawTimer = 1f;

            var now = System.DateTime.Now;
            string clock = now.ToString("HH:mm");
            float fps = FpsMeter.Instance != null ? FpsMeter.Instance.Fps : 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            string sub = ScreenCracked ? "ЭКРАН РАЗБИТ" : string.Format("{0:F0} FPS", fps);

            // перерисовываем существующую текстуру — без аллокаций
            ProceduralTextures.PhoneScreen(ScreenWidth, ScreenHeight, clock, sub, Battery, ScreenCracked, m_ScreenTex);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed <= m_CrackSpeed) return;
            m_CrackSpeed = speed;

            if (!ScreenCracked && speed > 6.5f && collision.contactCount > 0)
            {
                ScreenCracked = true;
                AudioPool.PlayClip(ProceduralAudio.Break, collision.GetContact(0).point, 0.8f, 1.15f);
                Debug.Log($"[Sandbox] Смартфон упал на скорости {speed:F1} м/с — экран треснул.");
            }
        }

        /// <summary>Починить экран (кнопка отладки).</summary>
        public void Repair()
        {
            ScreenCracked = false;
            m_CrackSpeed = 0f;
            Battery = 0.87f;
            if (m_ScreenTex != null)
            {
                ProceduralTextures.PhoneScreen(ScreenWidth, ScreenHeight, "00:00", "OK", Battery, false, m_ScreenTex);
            }
        }
    }

    public static class PhoneFactory
    {
        public static PhoneProp Create(Transform parent, Vector3 position)
        {
            return PhoneProp.Create(parent, position);
        }
    }
}
