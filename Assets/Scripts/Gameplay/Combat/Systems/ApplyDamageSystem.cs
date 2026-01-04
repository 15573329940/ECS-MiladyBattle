using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    // 服务端写，客户端读的视觉缓冲
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct DamageVisualBufferElement : IBufferElementData
    {
        [GhostField] public int Value;       // 数值
        [GhostField] public bool IsGold;     // true=金币, false=伤害
        [GhostField] public NetworkTick Tick; // 时间戳，用于去重
    }
    // 客户端本地组件，记录“我读到哪了”，防止重复飘字
    public struct ClientDamageCursor : IComponentData
    {
        public NetworkTick LastTick;
    }
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ApplyDamageSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinionUpgradeData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var upgradeDataBlob = SystemAPI.GetSingleton<MinionUpgradeData>().BlobRef;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);
            
            bool isServer = state.WorldUnmanaged.IsServer();
            var currentTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick; // 获取当前 Tick

            // 获取 BufferLookup，允许动态写入
            var visualBufferLookup = SystemAPI.GetBufferLookup<DamageVisualBufferElement>(false);

            foreach (var (damageBuffer, currentHitPoints, entity) in SystemAPI
                         .Query<DynamicBuffer<DamageBufferElement>, RefRW<CurrentHitPoints>>()
                         .WithAll<Simulate>().WithNone<DestroyEntityTag, DestroyOnTimer>()
                         .WithEntityAccess())
            {
                if (damageBuffer.IsEmpty) continue;

                int totalDamage = 0;
                
                // 1. 确保目标身上有视觉 Buffer
                DynamicBuffer<DamageVisualBufferElement> visualBuffer = default;
                bool hasVisualBuffer = visualBufferLookup.TryGetBuffer(entity, out visualBuffer);

                // --- 处理每一段伤害 ---
                foreach (var damageElement in damageBuffer)
                {
                    int dmg = damageElement.Value;
                    totalDamage += dmg;

                    // 写入伤害飘字 (红色)
                    if (dmg > 0 && hasVisualBuffer)
                    {
                        // 简单限制长度防止无限增长
                        if (visualBuffer.Length > 10) visualBuffer.RemoveAt(0);
                        
                        visualBuffer.Add(new DamageVisualBufferElement
                        {
                            Value = dmg,
                            IsGold = false,
                            Tick = currentTick
                        });
                    }
                }

                if (totalDamage > 0)
                {
                    currentHitPoints.ValueRW.Value -= totalDamage;

                    // --- 死亡逻辑 ---
                    if (currentHitPoints.ValueRO.Value <= 0)
                    {
                        if (isServer)
                        {
                            // 1. 如果是玩家英雄
                            if (SystemAPI.HasComponent<ChampTag>(entity))
                            {
                                // A. 锁血为0
                                currentHitPoints.ValueRW.Value = 0;

                                // B. 【核心】启用 DeadTag (标记死亡)
                                ecb.SetComponentEnabled<DeadTag>(entity, true);

                                // C. 移到“地狱” (极远处，防止由于物理碰撞导致预测错误)
                                ecb.SetComponent(entity, LocalTransform.FromPosition(new float3(0, -9999, 0)));
                
                                // D. 发送复活倒计时请求
                                var networkEntity = SystemAPI.GetComponent<NetworkEntityReference>(entity).Value;
                                var respawnEntity = SystemAPI.GetSingletonEntity<RespawnEntityTag>();
                                var respawnTickCount = SystemAPI.GetComponent<RespawnTickCount>(respawnEntity).Value;
                                var respawnTick = currentTick;
                                respawnTick.Add(respawnTickCount);

                                ecb.AppendToBuffer(respawnEntity, new RespawnBufferElement
                                {
                                    NetworkEntity = networkEntity,
                                    RespawnTick = respawnTick,
                                    NetworkId = SystemAPI.GetComponent<NetworkId>(networkEntity).Value,
                    
                                    // 【新增】把当前实体存进去！
                                    PlayerEntity = entity 
                                });
                
                                // 【绝对禁止】不要加 DestroyEntityTag！
                                // ecb.AddComponent<DestroyEntityTag>(entity); 
                            }
                            else
                            {
                                //ecb.AddComponent<DestroyEntityTag>(entity);
                                ecb.AddComponent(entity, new DestroyOnTimer { Value = 0.2f });
                                ecb.AddComponent(entity, new DestroyAtTick());
                                // 击杀金币逻辑
                                if (SystemAPI.HasComponent<MobaTeam>(entity))
                                {
                                    var victimTeam = SystemAPI.GetComponent<MobaTeam>(entity).Value;
                                    float goldReward = 0f;

                                    // [修改] 从配置表读取金币
                                    if (upgradeDataBlob.IsCreated && 
                                        SystemAPI.HasComponent<MinionTypeIndex>(entity) && 
                                        SystemAPI.HasComponent<MinionLevel>(entity))
                                    {
                                        int typeIdx = SystemAPI.GetComponent<MinionTypeIndex>(entity).Value;
                                        int lvl = SystemAPI.GetComponent<MinionLevel>(entity).Value;
                                    
                                        // 安全检查
                                        if (typeIdx < upgradeDataBlob.Value.UnitTypes.Length)
                                        {
                                            ref var levels = ref upgradeDataBlob.Value.UnitTypes[typeIdx].Levels;
                                            if (lvl < levels.Length)
                                            {
                                                goldReward = levels[lvl].KillGold; // <--- 读取配置
                                            }
                                        }
                                    }

                                    // 给玩家加钱
                                    foreach (var (playerGold, playerTeam) in
                                             SystemAPI.Query<RefRW<PlayerGold>, RefRO<MobaTeam>>())
                                    {
                                        if (playerTeam.ValueRO.Value != victimTeam)
                                        {
                                            playerGold.ValueRW.CurrentValue += goldReward;
                                            if (playerGold.ValueRW.CurrentValue > playerGold.ValueRO.MaxValue)
                                                playerGold.ValueRW.CurrentValue = playerGold.ValueRO.MaxValue;
                                        }
                                    }

                                    // 写入金币飘字 (黄色) -> 写入死者的 Buffer
                                    
                                    if (hasVisualBuffer)
                                    {
                                        visualBuffer.Add(new DamageVisualBufferElement
                                        {
                                            Value = (int)goldReward,
                                            IsGold = true,
                                            Tick = currentTick
                                        });
                                    }
                                }
                            }
                        }
                        else 
                        {
                            currentHitPoints.ValueRW.Value = 0;
                        }
                    }
                }
                damageBuffer.Clear();
            }
        }
    }
}