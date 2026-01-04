using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    public struct TowerTag : IComponentData {}
    public class TowerAuthoring : MonoBehaviour
    {
        public class TowerBaker : Baker<TowerAuthoring>
        {
            public override void Bake(TowerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<TowerTag>(entity);
                AddBuffer<DamageVisualBufferElement>(entity);

                // 2. 【核心修复】添加本地光标组件
                // 即使这个组件不需要同步（只有客户端逻辑用），在 Bake 时加上也是最稳妥的
                // 这样客户端生成的 Ghost 实体也会默认带有这个组件的“空壳”
                AddComponent(entity, new ClientDamageCursor 
                { 
                    LastTick = NetworkTick.Invalid 
                });
            }
        }
    }
}