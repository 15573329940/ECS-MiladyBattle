using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class RespawnChampSystem : SystemBase
    {
        public Action<int> OnUpdateRespawnCountdown;
        public Action OnRespawn;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<MobaPrefabs>();
            RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            RequireForUpdate<RespawnBufferElement>();
        }
        
        protected override void OnStartRunning()
        {
            if (SystemAPI.HasSingleton<RespawnEntityTag>()) return;
            var respawnPrefab = SystemAPI.GetSingleton<MobaPrefabs>().RespawnEntity;
            EntityManager.Instantiate(respawnPrefab);
        }

        protected override void OnUpdate()
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            if (!networkTime.IsFirstTimeFullyPredictingTick) return;
            var currentTick = networkTime.ServerTick;
            
            var isServer = World.IsServer();
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(World.Unmanaged);

            // 【修复点1】查询里去掉 MaxHitPoints！只查复活管理器有的东西
            foreach (var respawnBuffer in SystemAPI.Query<DynamicBuffer<RespawnBufferElement>>()
                         .WithAll<RespawnTickCount, Simulate>())
            {
                // 倒序遍历
                for (var i = respawnBuffer.Length - 1; i >= 0; i--)
                {
                    var curRespawn = respawnBuffer[i];

                    // 检查时间
                    if (currentTick.Equals(curRespawn.RespawnTick) || currentTick.IsNewerThan(curRespawn.RespawnTick))
                    {
                        if (isServer)
                        {
                            var playerEntity = curRespawn.PlayerEntity;

                            if (SystemAPI.Exists(playerEntity))
                            {
                                // 获取出生点
                                var spawnPos = float3.zero;
                                if (SystemAPI.HasComponent<PlayerSpawnInfo>(curRespawn.NetworkEntity))
                                {
                                    spawnPos = SystemAPI.GetComponent<PlayerSpawnInfo>(curRespawn.NetworkEntity).SpawnPosition;
                                }

                                // 瞬移
                                ecb.SetComponent(playerEntity, LocalTransform.FromPosition(spawnPos));
        
                                // 【修复点2】在这里单独获取英雄的最大血量
                                int maxHp = 100; // 默认值防止报错
                                if (SystemAPI.HasComponent<MaxHitPoints>(playerEntity))
                                {
                                    maxHp = SystemAPI.GetComponent<MaxHitPoints>(playerEntity).Value;
                                }
                                ecb.SetComponent(playerEntity, new CurrentHitPoints { Value = maxHp });

                                // 复活
                                ecb.SetComponentEnabled<DeadTag>(playerEntity, false);
                            }

                            respawnBuffer.RemoveAt(i);
                        }
                        else
                        {
                            OnRespawn?.Invoke();
                            // 客户端不能 RemoveAt
                        }
                    }
                    else if (!isServer)
                    {
                        // 倒计时逻辑
                        if (SystemAPI.TryGetSingleton<NetworkId>(out var networkId))
                        {
                            if (networkId.Value == curRespawn.NetworkId)
                            {
                                var ticksToRespawn = curRespawn.RespawnTick.TickIndexForValidTick - currentTick.TickIndexForValidTick;
                                var simulationTickRate = NetCodeConfig.Global.ClientServerTickRate.SimulationTickRate;
                                var secondsToStart = (int)math.ceil((float)ticksToRespawn / simulationTickRate);
                                OnUpdateRespawnCountdown?.Invoke(secondsToStart);
                            }
                        }
                    }
                }
            }
        }
    }
}