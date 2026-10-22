using UnityEngine;

namespace Sandbox
{
    public enum ImpactSound { Wood, Metal, Glass, Soft, Concrete, None }

    /// <summary>
    /// Предмет, который можно взять в руки, бросить и которым можно ударить.
    /// Вешается на любой объект с Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        [Header("Описание")]
        public string displayName = "Предмет";
        public ImpactSound impactSound = ImpactSound.Wood;
        [Tooltip("Множитель урона при ударе этим предметом")]
        public float damageMultiplier = 1f;
        [Tooltip("Предмет слишком тяжёлый, чтобы его бросить далеко")]
        public float heaviness = 1f;

        [Header("Служебное")]
        public bool restoreOnReset = true;

        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Quaternion SpawnRotation { get; private set; }
        public int OriginalLayer { get; private set; }
        public CollisionDetectionMode OriginalCcd { get; private set; }
        public Bounds LocalBounds { get; private set; }

        ImpactHandler m_Impact;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            m_Impact = GetComponent<ImpactHandler>();
            OriginalLayer = gameObject.layer;
            OriginalCcd = Body != null ? Body.collisionDetectionMode : CollisionDetectionMode.Discrete;
            RememberSpawn();
        }

        public void RememberSpawn()
        {
            SpawnPosition = transform.position;
            SpawnRotation = transform.rotation;
        }

        public virtual void OnGrabbed()
        {
            IsHeld = true;
            if (Body == null) return;
            Body.WakeUp();
            Body.isKinematic = true;
            Body.velocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            gameObject.layer = Layers.Held;
            if (m_Impact != null) m_Impact.SuppressWhileHeld = true;
        }

        public virtual void OnReleased(bool thrown)
        {
            IsHeld = false;
            if (Body == null) return;
            Body.isKinematic = false;
            gameObject.layer = OriginalLayer == 0 ? Layers.Prop : OriginalLayer;
            Body.collisionDetectionMode = thrown ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            Body.WakeUp();
            if (m_Impact != null)
            {
                m_Impact.SuppressWhileHeld = false;
                if (thrown) m_Impact.MarkAsThrown(1.2f);
            }
        }

        /// <summary>Мгновенно вернуть предмет на место спавна (используется OutOfBoundsRecovery).</summary>
        public void ResetToSpawn()
        {
            if (Body != null)
            {
                Body.isKinematic = true;
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            transform.position = SpawnPosition;
            transform.rotation = SpawnRotation;
            Physics.SyncTransforms();
            IsHeld = false;
            if (Body != null)
            {
                Body.isKinematic = false;
                Body.collisionDetectionMode = OriginalCcd;
                Body.WakeUp();
            }
            gameObject.layer = OriginalLayer == 0 ? Layers.Prop : OriginalLayer;
        }

        public Bounds WorldBounds()
        {
            var cols = GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return new Bounds(transform.position, Vector3.one * 0.2f);
            var b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return b;
        }
    }
}
