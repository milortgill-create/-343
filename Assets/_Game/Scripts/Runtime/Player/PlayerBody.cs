using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Тело игрока: переключение между CharacterController (живой) и рэгдоллом
    /// (сильный удар / падение). В режиме рэгдолла камера жёстко сидит в кости
    /// головы и кувыркается вместе с телом.
    /// </summary>
    public class PlayerBody : MonoBehaviour
    {
        public PlayerMotor motor;
        public PlayerView view;
        public PlayerHands hands;
        public Transform head;                 // объект с PlayerView
        public Material skinMaterial;
        public Material clothMaterial;

        public RagdollController Ragdoll { get; private set; }
        public bool IsRagdolled { get; private set; }
        public Vector3 SpawnPosition { get; set; }
        public Quaternion SpawnRotation { get; set; }
        public float RagdollTime { get; private set; }

        Transform m_HeadOriginalParent;
        Vector3 m_HeadOriginalLocalPos;
        Quaternion m_HeadOriginalLocalRot;
        bool m_Built;

        public System.Action OnFallen;
        public System.Action OnGotUp;

        public void Build()
        {
            if (m_Built) return;
            m_Built = true;

            if (skinMaterial == null) skinMaterial = MaterialFactory.Skin;
            if (clothMaterial == null) clothMaterial = MaterialFactory.Rubber;

            m_HeadOriginalParent = head != null ? head.parent : null;
            if (head != null)
            {
                m_HeadOriginalLocalPos = head.localPosition;
                m_HeadOriginalLocalRot = head.localRotation;
            }

            Ragdoll = RagdollBuilder.Build(gameObject, RagdollProfile.Humanoid(SandboxConfig.PlayerHeight, SandboxConfig.PlayerMass),
                skinMaterial, clothMaterial, true, Layers.Player);
            Ragdoll.SetEnabled(false);

            SpawnPosition = transform.position;
            SpawnRotation = transform.rotation;
        }

        /// <summary>Урон, от которого игрок падает в рэгдолл.</summary>
        public void Fall(Vector3 impulse, Vector3 worldPoint)
        {
            if (IsRagdolled || Ragdoll == null) return;
            IsRagdolled = true;
            RagdollTime = 0f;

            // тело появляется ровно там, где стоял игрок, и смотрит туда же
            Vector3 pos = transform.position;
            Quaternion rot = Quaternion.Euler(0f, view != null ? view.Yaw : transform.eulerAngles.y, 0f);

            if (motor != null) motor.ControlEnabled = false;
            Ragdoll.SnapToBindPose(pos, rot);
            Ragdoll.SetEnabled(true);
            Ragdoll.GoLimp(24f, 6f);
            Ragdoll.AddImpulseAt(worldPoint.sqrMagnitude > 0.01f ? worldPoint : pos + Vector3.up, impulse);

            if (hands != null) hands.Hide();
            if (view != null) view.EnterRagdollMode(Ragdoll.Head);

            AudioPool.PlayClip(ProceduralAudio.RagdollHit, pos + Vector3.up, 1f, 0.92f);
            OnFallen?.Invoke();
        }

        /// <summary>Встать там, где упал (или на точке спавна).</summary>
        public void GetUp(bool atSpawn = false)
        {
            if (!IsRagdolled || Ragdoll == null) return;

            Vector3 pos;
            Quaternion rot;
            if (atSpawn)
            {
                pos = SpawnPosition;
                rot = SpawnRotation;
            }
            else
            {
                Transform hips = Ragdoll.Hips != null ? Ragdoll.Hips : transform;
                pos = new Vector3(hips.position.x, SpawnPosition.y, hips.position.z);
                // не даём встать внутри стены
                pos.x = Mathf.Clamp(pos.x, -SandboxConfig.ArenaWidth * 0.5f + 1f, SandboxConfig.ArenaWidth * 0.5f - 1f);
                pos.z = Mathf.Clamp(pos.z, -SandboxConfig.ArenaDepth * 0.5f + 1f, SandboxConfig.ArenaDepth * 0.5f - 1f);
                rot = Quaternion.Euler(0f, view != null ? view.Yaw : 0f, 0f);
            }

            Ragdoll.SetEnabled(false);
            transform.position = pos;
            transform.rotation = rot;

            if (view != null) view.ExitRagdollMode(m_HeadOriginalParent, m_HeadOriginalLocalPos);
            if (motor != null)
            {
                motor.ControlEnabled = true;
                motor.Teleport(pos, rot);
            }
            if (hands != null) hands.Show();

            IsRagdolled = false;
            RagdollTime = 0f;
            if (view != null) view.AddTrauma(0.25f);
            OnGotUp?.Invoke();
        }

        void Update()
        {
            if (!IsRagdolled) return;
            RagdollTime += Time.deltaTime;

            // страховка: если тело всё-таки ушло под пол — сразу поднимаем
            if (Ragdoll != null && Ragdoll.LowestPoint() < SandboxConfig.KillY)
            {
                Debug.LogWarning("[Sandbox] Игрок провалился под пол — восстановлен на точке спавна.");
                GetUp(true);
            }
        }

        public Vector3 HeadPosition()
        {
            if (IsRagdolled && Ragdoll != null && Ragdoll.Head != null) return Ragdoll.Head.position;
            return head != null ? head.position : transform.position + Vector3.up * SandboxConfig.EyeHeight;
        }
    }
}
