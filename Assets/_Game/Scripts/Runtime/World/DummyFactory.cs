using UnityEngine;

namespace Sandbox
{
    /// <summary>Создаёт манекен-болванку с полноценным рэгдоллом.</summary>
    public static class DummyFactory
    {
        public static DamageableDummy Create(Vector3 position, Quaternion rotation, string displayName,
            Transform parent, float height = 1.78f, float mass = 74f)
        {
            var go = GameUtil.New(displayName, parent);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.layer = Layers.Dummy;

            var dummy = go.AddComponent<DamageableDummy>();
            dummy.displayName = displayName;
            dummy.maxHealth = 100f;

            var ragdoll = RagdollBuilder.Build(go, RagdollProfile.Humanoid(height, mass),
                MaterialFactory.Foam, MaterialFactory.Target, true, Layers.Dummy);
            dummy.ragdoll = ragdoll;

            // подставка-блин, чтобы болванка выглядела закреплённой
            var basePlate = GameUtil.NewPrimitive(PrimitiveType.Cylinder, "Stand", go.transform,
                new Vector3(0f, 0.011f, 0f), new Vector3(0.95f, 0.011f, 0.95f));
            basePlate.GetComponent<MeshRenderer>().sharedMaterial = MaterialFactory.RustyMetal;
            var baseCol = basePlate.GetComponent<Collider>();
            if (baseCol != null) Object.Destroy(baseCol);
            var standCollider = basePlate.AddComponent<BoxCollider>();
            standCollider.size = new Vector3(0.95f, 0.022f, 0.95f);
            standCollider.center = Vector3.zero;
            basePlate.layer = Layers.World;
            basePlate.isStatic = true;

            // табличка с именем
            var label = GameObject.CreatePrimitive(PrimitiveType.Quad);
            label.name = "Label";
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0f, height + 0.42f, 0f);
            label.transform.localScale = new Vector3(1.05f, 0.26f, 1f);
            var lc = label.GetComponent<Collider>();
            if (lc != null) Object.Destroy(lc);
            var lmr = label.GetComponent<MeshRenderer>();
            if (lmr != null)
            {
                var tex = ProceduralTextures.Label(displayName.ToUpperInvariant(), 256, 64,
                    new Color(0.08f, 0.09f, 0.11f), new Color(0.55f, 0.95f, 1f));
                lmr.sharedMaterial = MaterialFactory.CreateUnlit("LabelMat", Color.white, tex);
                lmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lmr.receiveShadows = false;
            }
            label.layer = Layers.Dummy;
            var bb = label.AddComponent<Billboard>();
            bb.onlyY = true;

            // «живая» поза: таз закреплён, приводы держат тело вертикально
            ragdoll.SetEnabled(true);
            ragdoll.SnapToBindPose(position, rotation);
            ragdoll.SetUpright(true);
            dummy.RememberSpawn();

            if (OutOfBoundsRecovery.Instance != null) OutOfBoundsRecovery.Instance.Register(dummy);
            return dummy;
        }
    }
}
