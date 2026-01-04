using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    public class ChampAuthoring : MonoBehaviour
    {
        public float MoveSpeed;
        public class ChampBaker : Baker<ChampAuthoring>
        {
            public override void Bake(ChampAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<ChampTag>(entity);
                AddComponent<NewChampTag>(entity);
                AddComponent<MobaTeam>(entity);
                AddComponent<URPMaterialPropertyBaseColor>(entity);
                AddComponent<ChampMoveTargetPosition>(entity);
                AddComponent(entity,new CharacterMoveSpeed{Value = authoring.MoveSpeed});
                AddComponent<AbilityInput>(entity);
                AddComponent<AimInput>(entity);
                AddComponent<NetworkEntityReference>(entity);
                AddComponent<SpawnMinionInput>(entity);
                AddBuffer<DamageVisualBufferElement>(entity);
                // 1. 添加组件
                AddComponent<AimSkillShotTag>(entity);
                // 2. 默认禁用！
                SetComponentEnabled<AimSkillShotTag>(entity, false);
                // 1. 添加组件
                AddComponent<DeadTag>(entity);
                // 2. 默认禁用！
                SetComponentEnabled<DeadTag>(entity, false);
            }
        }
    }
}