using SO;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    
    public class MinionAuthoring : MonoBehaviour
    {
        public float MoveSpeed;
        
        public class MinionBaker : Baker<MinionAuthoring>
        {
            public override void Bake(MinionAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<MinionTag>(entity);
                AddComponent<NewMinionTag>(entity);
                AddComponent(entity, new CharacterMoveSpeed { Value = authoring.MoveSpeed });
                AddComponent(entity, new OriCharacterMoveSpeed { Value = authoring.MoveSpeed });
                AddComponent<MinionPathIndex>(entity);
                
                AddBuffer<MinionPathPosition>(entity);
                AddComponent<MobaTeam>(entity);
                AddComponent<URPMaterialPropertyBaseColor>(entity);
                
            
                // 添加等级组件
                AddComponent(entity, new MinionLevel { Value = 0 });
                
                AddBuffer<DamageVisualBufferElement>(entity);
                AddComponent(entity, new ClientDamageCursor 
                { 
                    LastTick = NetworkTick.Invalid 
                });
            }
        }
    }
}