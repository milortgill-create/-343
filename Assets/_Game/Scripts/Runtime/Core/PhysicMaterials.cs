using UnityEngine;

namespace Sandbox
{
    /// <summary>
    /// Физические материалы (трение / упругость).
    /// Создаются в рантайме и переиспользуются — это дешевле, чем
    /// десятки отдельных PhysicMaterial-ассетов, и даёт одинаковое
    /// поведение предметов на полу, стенах и между собой.
    /// </summary>
    public static class PhysicMaterials
    {
        static PhysicMaterial s_Concrete;
        static PhysicMaterial s_Wood;
        static PhysicMaterial s_Metal;
        static PhysicMaterial s_Glass;
        static PhysicMaterial s_Flesh;
        static PhysicMaterial s_World;

        /// <summary>Бетонный пол и стены: цепкий, почти не пружинит.</summary>
        public static PhysicMaterial Concrete => s_Concrete ??= Create("PM_Concrete", 0.85f, 0.05f);

        /// <summary>Дерево (ящики, доски).</summary>
        public static PhysicMaterial Wood => s_Wood ??= Create("PM_Wood", 0.72f, 0.14f);

        /// <summary>Металл (бочки): скользкий, звонкий отскок.</summary>
        public static PhysicMaterial Metal => s_Metal ??= Create("PM_Metal", 0.42f, 0.26f);

        /// <summary>Стекло (бутылки, осколки).</summary>
        public static PhysicMaterial Glass => s_Glass ??= Create("PM_Glass", 0.55f, 0.32f);

        /// <summary>Манекены: мягкое тело, гасит отскок.</summary>
        public static PhysicMaterial Flesh => s_Flesh ??= Create("PM_Flesh", 0.9f, 0.02f);

        /// <summary>Статичная геометрия арены (платформы, стеллажи, тумба).</summary>
        public static PhysicMaterial World => s_World ??= Create("PM_World", 0.9f, 0.06f);

        static PhysicMaterial Create(string name, float friction, float bounciness)
        {
            var mat = new PhysicMaterial(name)
            {
                dynamicFriction = friction,
                staticFriction = friction * 1.1f,
                bounciness = bounciness,
                frictionCombine = FrictionCombine.Average,
                bounceCombine = FrictionCombine.Maximum
            };
            return mat;
        }

        /// <summary>Подбирает материал по типу звука удара — удобно в PropFactory.</summary>
        public static PhysicMaterial ForImpactSound(ImpactSound sound)
        {
            switch (sound)
            {
                case ImpactSound.Wood: return Wood;
                case ImpactSound.Metal: return Metal;
                case ImpactSound.Glass: return Glass;
                case ImpactSound.Soft: return Flesh;
                case ImpactSound.Concrete: return Concrete;
                default: return World;   // ImpactSound.None
            }
        }

        /// <summary>Назначает материал всем коллайдерам объекта (включая дочерние).</summary>
        public static void Apply(GameObject go, PhysicMaterial material)
        {
            if (go == null || material == null) return;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].sharedMaterial = material;
            }
        }
    }
}
