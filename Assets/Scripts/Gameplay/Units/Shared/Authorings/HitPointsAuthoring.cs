using Unity.Entities;
using UnityEngine;
using Unity.Rendering; // 必须引用这个命名空间！
using Unity.Mathematics;
namespace TMG.NFE_Tutorial
{
    public class HitPointsAuthoring : MonoBehaviour
    {
        public int MaxHitPoints;
        public Vector3 HealthBarOffset;
        public GameObject HealthBarObject;
        public class HitPointsBaker : Baker<HitPointsAuthoring>
        {
            public override void Bake(HitPointsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                var healthBarEntity = GetEntity(authoring.HealthBarObject, TransformUsageFlags.Renderable);
                AddComponent(entity, new HealthBarRef
                {
                    BarEntity = healthBarEntity//shader版血条
                });
                AddComponent(entity, new CurrentHitPoints{Value = authoring.MaxHitPoints});
                AddComponent(entity, new MaxHitPoints{Value = authoring.MaxHitPoints});
                AddBuffer<DamageBufferElement>(entity);
                AddBuffer<DamageThisTick>(entity);
                AddComponent(entity, new HealthBarOffset { Value = authoring.HealthBarOffset });
            }
        }
    }
}