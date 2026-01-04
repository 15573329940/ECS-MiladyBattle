using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using SO;
namespace TMG.NFE_Tutorial
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct MinionUpgradeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinionUpgradeData>(); // 需要读取 Blob 数据来获知价格
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);
            
            var upgradeDataBlob = SystemAPI.GetSingleton<MinionUpgradeData>().BlobRef;
            if (!upgradeDataBlob.IsCreated) return;

            // 【注意】这里遍历的是临时 RPC 实体 (requestEntity)
            foreach (var (rpc, receiveRpcCommand, requestEntity) in SystemAPI.Query<RefRO<UpgradeMinionRpc>, ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                // 1. 从 RPC 数据中读取出目标玩家
                Entity playerEntity = rpc.ValueRO.TargetPlayer;

                // 2. 检查玩家是否存在且数据健全
                if (SystemAPI.Exists(playerEntity) && 
                    SystemAPI.HasComponent<PlayerGold>(playerEntity) && 
                    SystemAPI.HasComponent<PlayerMinionTech>(playerEntity))
                {
                    var gold = SystemAPI.GetComponent<PlayerGold>(playerEntity);
                    var tech = SystemAPI.GetComponent<PlayerMinionTech>(playerEntity);
                    int typeIndex = rpc.ValueRO.MinionTypeIndex;
                    int currentLevel = tech.GetLevel(typeIndex);
                    ref var unitType = ref upgradeDataBlob.Value.UnitTypes[typeIndex];
                    // --- 升级逻辑 ---
                    if (currentLevel < unitType.Levels.Length - 1) 
                    {
                        ref var levelData = ref unitType.Levels[currentLevel];
                        // 确保使用正确的 Cost 字段
                        int cost = levelData.UpgradeCost; 
                        
                        if (gold.CurrentValue >= cost)
                        {
                            // 扣钱
                            gold.CurrentValue -= cost;
                            SystemAPI.SetComponent(playerEntity, gold);

                            // 升级
                            tech.SetLevel(typeIndex, currentLevel + 1);
                            SystemAPI.SetComponent(playerEntity, tech);
                        }
                    }
                }
                
                // 3. 【放心销毁】因为 requestEntity 是刚才创建的临时信件
                // 这一步必须做，否则内存泄漏。且不会误删玩家。
                ecb.DestroyEntity(requestEntity); 
            }
        }
    }
}