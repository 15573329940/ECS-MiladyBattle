using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Burst;
using Unity.Mathematics;
namespace TMG.NFE_Tutorial
{
    // 1. 定义金币组件
    // GhostComponent 表示这个组件需要在网络间同步
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct PlayerGold : IComponentData
    {
        // GhostField 表示这个字段需要同步
        // Quantization 决定了压缩精度，默认 1000 意味着保留3位小数左右的精度，足够金币用了
        [GhostField(Quantization = 1000)] 
        public float CurrentValue;

        // 下面这两个不需要同步，因为它们通常是配置数据，客户端可以查配置表，或者初始化时同步一次即可
        // 这里为了最简演示，我不加 GhostField，假设客户端只关心 CurrentValue
        public float MaxValue;
        public float GrowthRate; // 每秒增加多少
    }
    public class PlayerGoldAuthoring : MonoBehaviour
    {
        public float InitialGold = 5f;
        public float MaxGold = 1000f;
        public float GrowthRate = 1f; // 比如每秒涨 1 块钱

        class Baker : Baker<PlayerGoldAuthoring>
        {
            public override void Bake(PlayerGoldAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PlayerGold
                {
                    CurrentValue = authoring.InitialGold,
                    MaxValue = authoring.MaxGold,
                    GrowthRate = authoring.GrowthRate
                });
                
            }
        }
    }
    // 关键：只在服务端模拟组运行
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))] // 放在预测组是为了时间步长更稳定
    public partial struct GoldGrowthSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GamePlayingTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 获取网络固定的时间步长 (比如 1/60 秒)
            var deltaTime = SystemAPI.Time.DeltaTime;

            // 遍历所有拥有 PlayerGold 组件的实体
            foreach (var gold in SystemAPI.Query<RefRW<PlayerGold>>())
            {
                // 简单的数值计算
                if (gold.ValueRO.CurrentValue < gold.ValueRO.MaxValue)
                {
                    gold.ValueRW.CurrentValue += gold.ValueRO.GrowthRate * deltaTime;
                    
                    // 钳制最大值
                    if (gold.ValueRW.CurrentValue > gold.ValueRO.MaxValue)
                    {
                        gold.ValueRW.CurrentValue = gold.ValueRO.MaxValue;
                    }
                }
            }
        }
    }
}