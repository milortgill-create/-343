using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Руки персонажа, которые видно внизу экрана. Строятся из примитивов,
    /// покачиваются при ходьбе, отстают при резких поворотах камеры,
    /// выбрасываются вперёд при ударе и сходятся к предмету, когда он в руках.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        public Transform holdAnchor;
        public Material skinMaterial;
        public Material sleeveMaterial;

        [Header("Параметры анимации")]
        public float swayAmount = 0.9f;
        public float swaySpeed = 9f;
        public float walkSwayAmount = 0.030f;
        public float punchDistance = 0.19f;
        public float punchOutTime = 0.09f;
        public float punchInTime = 0.19f;

        public Transform LeftArm { get; private set; }
        public Transform RightArm { get; private set; }
        public Transform LeftHandBone { get; private set; }
        public Transform RightHandBone { get; private set; }
        public float PunchTimer => m_PunchTime;
        public bool IsPunching => m_PunchTime > 0f;
        public bool PunchWithRight => m_PunchRight;

        Vector3 m_BasePos;
        Quaternion m_BaseRot;
        Vector3 m_LeftBase;
        Vector3 m_RightBase;
        Quaternion m_LeftBaseRot;
        Quaternion m_RightBaseRot;
        Vector3 m_Sway;
        Vector3 m_SwayVel;
        float m_PunchTime;
        float m_PunchTotal;
        bool m_PunchRight = true;
        bool m_Built;
        float m_HoldBlend;

        public void Build(Camera cam)
        {
            if (m_Built) return;
            m_Built = true;

            transform.SetParent(cam.transform, false);
            transform.localPosition = new Vector3(0f, -0.012f, 0.06f);
            transform.localRotation = Quaternion.identity;
            m_BasePos = transform.localPosition;
            m_BaseRot = transform.localRotation;
            gameObject.layer = Layers.Hands;

            if (skinMaterial == null) skinMaterial = MaterialFactory.Skin;
            if (sleeveMaterial == null) sleeveMaterial = MaterialFactory.Rubber;

            LeftArm = BuildArm("ArmLeft", -1, out var leftHand);
            RightArm = BuildArm("ArmRight", 1, out var rightHand);
            LeftHandBone = leftHand;
            RightHandBone = rightHand;

            m_LeftBase = LeftArm.localPosition;
            m_RightBase = RightArm.localPosition;
            m_LeftBaseRot = LeftArm.localRotation;
            m_RightBaseRot = RightArm.localRotation;

            if (holdAnchor == null)
            {
                var anchor = new GameObject("HoldAnchor");
                anchor.transform.SetParent(cam.transform, false);
                anchor.transform.localPosition = new Vector3(0.17f, -0.21f, 0.58f);
                anchor.transform.localRotation = Quaternion.Euler(-8f, 6f, 0f);
                anchor.layer = Layers.Hands;
                holdAnchor = anchor.transform;
            }
        }

        Transform BuildArm(string name, int side, out Transform hand)
        {
            var arm = new GameObject(name);
            arm.transform.SetParent(transform, false);
            arm.transform.localPosition = new Vector3(side * 0.185f, -0.235f, 0.02f);
            arm.transform.localRotation = Quaternion.Euler(30f, side * 13f, -side * 9f);
            arm.layer = Layers.Hands;

            // плечо → локоть
            CreateLimb(arm.transform, "Upper", new Vector3(0f, 0f, -0.10f), new Vector3(0f, -0.035f, 0.10f),
                0.052f, sleeveMaterial, true);
            // локоть → запястье
            CreateLimb(arm.transform, "Fore", new Vector3(0f, -0.035f, 0.10f), new Vector3(0f, -0.005f, 0.30f),
                0.042f, skinMaterial, true);
            // кисть
            var handGo = GameUtil.NewPrimitive(PrimitiveType.Cube, "Hand", arm.transform,
                new Vector3(0f, 0.002f, 0.355f), new Vector3(0.078f, 0.050f, 0.115f),
                Quaternion.Euler(-6f, 0f, 0f), Layers.Hands);
            handGo.GetComponent<MeshRenderer>().sharedMaterial = skinMaterial;
            GameUtil.StripCollider(handGo);
            DisableShadows(handGo);

            // большой палец — мелкая деталь, чтобы кисть не выглядела «кирпичом»
            var thumb = GameUtil.NewPrimitive(PrimitiveType.Capsule, "Thumb", handGo.transform,
                new Vector3(side * 0.036f, 0.006f, 0.028f), new Vector3(0.026f, 0.020f, 0.026f),
                Quaternion.Euler(70f, 0f, side * 25f), Layers.Hands);
            thumb.GetComponent<MeshRenderer>().sharedMaterial = skinMaterial;
            GameUtil.StripCollider(thumb);
            DisableShadows(thumb);

            hand = handGo.transform;
            return arm.transform;
        }

        static void DisableShadows(GameObject go)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        static void CreateLimb(Transform parent, string name, Vector3 from, Vector3 to, float radius,
            Material mat, bool noShadows)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);

            Vector3 dir = to - from;
            float len = dir.magnitude;
            go.transform.localPosition = (from + to) * 0.5f;
            go.transform.localRotation = len > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, dir.normalized)
                : Quaternion.identity;
            go.transform.localScale = new Vector3(radius * 2f, Mathf.Max(0.01f, len * 0.5f), radius * 2f);

            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
            go.layer = Layers.Hands;
            if (noShadows) DisableShadows(go);
        }

        /// <summary>Начать удар (руки чередуются).</summary>
        public void Punch(bool right)
        {
            m_PunchRight = right;
            m_PunchTime = punchOutTime + punchInTime;
            m_PunchTotal = m_PunchTime;
        }

        public void Tick(float speed01, Vector2 lookDelta, float dt, bool holdingItem, float bobPhase)
        {
            if (!m_Built) return;

            // --- отстающие руки при повороте камеры ---
            Vector3 target = new Vector3(
                Mathf.Clamp(-lookDelta.x * 0.0016f, -0.045f, 0.045f) * swayAmount,
                Mathf.Clamp(-lookDelta.y * 0.0016f, -0.045f, 0.045f) * swayAmount,
                0f);
            m_Sway = Vector3.SmoothDamp(m_Sway, target, ref m_SwayVel, 1f / swaySpeed, 10f, dt);

            // --- покачивание при ходьбе ---
            float wx = Mathf.Cos(bobPhase) * walkSwayAmount * speed01;
            float wy = Mathf.Abs(Mathf.Sin(bobPhase)) * walkSwayAmount * 0.75f * speed01;

            transform.localPosition = m_BasePos + m_Sway + new Vector3(wx, -wy, 0f);
            transform.localRotation = m_BaseRot * Quaternion.Euler(
                -m_Sway.y * 190f + wy * 60f, m_Sway.x * 190f + wx * 60f, -wx * 130f);

            // --- предмет в руках: кисти сходятся к якорю ---
            m_HoldBlend = Mathf.MoveTowards(m_HoldBlend, holdingItem ? 1f : 0f, dt * 7f);
            Vector3 leftTarget = m_LeftBase;
            Vector3 rightTarget = m_RightBase;
            Quaternion leftRot = m_LeftBaseRot;
            Quaternion rightRot = m_RightBaseRot;

            if (m_HoldBlend > 0.001f && holdAnchor != null)
            {
                Vector3 anchorLocal = transform.InverseTransformPoint(holdAnchor.position);
                Vector3 leftHold = anchorLocal + new Vector3(-0.075f, -0.045f, -0.05f);
                Vector3 rightHold = anchorLocal + new Vector3(0.055f, -0.055f, -0.02f);
                leftTarget = Vector3.Lerp(leftTarget, leftHold, m_HoldBlend);
                rightTarget = Vector3.Lerp(rightTarget, rightHold, m_HoldBlend);
                leftRot = Quaternion.Slerp(leftRot, Quaternion.Euler(48f, 22f, -14f), m_HoldBlend);
                rightRot = Quaternion.Slerp(rightRot, Quaternion.Euler(48f, -22f, 14f), m_HoldBlend);
            }

            // --- удар ---
            float punchK = 0f;
            if (m_PunchTime > 0f)
            {
                m_PunchTime = Mathf.Max(0f, m_PunchTime - dt);
                float elapsed = m_PunchTotal - m_PunchTime;
                punchK = elapsed < punchOutTime
                    ? Mathf.SmoothStep(0f, 1f, elapsed / punchOutTime)
                    : 1f - Mathf.SmoothStep(0f, 1f, (elapsed - punchOutTime) / punchInTime);
            }

            Vector3 punchOffset = new Vector3(0f, 0.012f, punchDistance) * punchK;
            Quaternion punchRot = Quaternion.Euler(-16f * punchK, 0f, 0f);

            if (LeftArm != null)
            {
                LeftArm.localPosition = leftTarget + (m_PunchRight ? Vector3.zero : punchOffset);
                LeftArm.localRotation = leftRot * (m_PunchRight ? Quaternion.identity : punchRot);
            }
            if (RightArm != null)
            {
                RightArm.localPosition = rightTarget + (m_PunchRight ? punchOffset : Vector3.zero);
                RightArm.localRotation = rightRot * (m_PunchRight ? punchRot : Quaternion.identity);
            }
        }

        public void Hide()
        {
            if (LeftArm != null) LeftArm.gameObject.SetActive(false);
            if (RightArm != null) RightArm.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (LeftArm != null) LeftArm.gameObject.SetActive(true);
            if (RightArm != null) RightArm.gameObject.SetActive(true);
        }
    }
}
