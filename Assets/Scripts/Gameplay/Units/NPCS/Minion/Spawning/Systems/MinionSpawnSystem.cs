using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    public struct MinionSpawnIndex : IComponentData
    {
        public int Value;
    }
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct UnifiedMinionSpawnSystem : ISystem
    {
        

        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, new MinionSpawnIndex { Value = 0 });
            state.RequireForUpdate<GamePlayingTag>();
            state.RequireForUpdate<MinionPathContainers>();
            state.RequireForUpdate<MobaPrefabs>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<MinionUpgradeData>(); 
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var deltaTime = SystemAPI.Time.DeltaTime;

            var prefabContainer = SystemAPI.GetSingletonEntity<MobaPrefabs>();
            var minionPrefabsBuffer = SystemAPI.GetBuffer<MinionPrefabElement>(prefabContainer);
            
            // 1. 获取路径容器组件
            var pathContainers = SystemAPI.GetSingleton<MinionPathContainers>();
            var globalUpgradeData = SystemAPI.GetSingleton<MinionUpgradeData>();

            // 【修复报错关键点】：先从 Entity 获取 Buffer
            // 假设 pathContainers.TopLane 存的是 Entity
            var topLaneBuffer = SystemAPI.GetBuffer<MinionPathPosition>(pathContainers.TopLane);
            var midLaneBuffer = SystemAPI.GetBuffer<MinionPathPosition>(pathContainers.MidLane); // 中路 Buffer
            var botLaneBuffer = SystemAPI.GetBuffer<MinionPathPosition>(pathContainers.BotLane);

            // ============================================================
            // 逻辑 A: 自动波次刷兵 (Wave Spawning)
            // ============================================================
            foreach (var minionSpawnAspect in SystemAPI.Query<MinionSpawnAspect>())
            {
                minionSpawnAspect.DecrementTimers(deltaTime);
                if (minionSpawnAspect.ShouldSpawn)
                {
                    int totalPrefabs = minionPrefabsBuffer.Length;
                    SystemAPI.TryGetSingletonRW<MinionSpawnIndex>(out var indexRef);
                    int currentIndex = indexRef.ValueRW.Value;
                    var minionPrefab = minionPrefabsBuffer[currentIndex % totalPrefabs].Value;
                    
                    int minionLevel = 0; 
                    int typeIndex = currentIndex % totalPrefabs;
                    
                    indexRef.ValueRW.Value++; // 计数增加

                    // 【核心修改 1】计算固定偏移值 (0, -0.5, 0.5)
                    float fixedOffset = 0f;
                    int mod = currentIndex % 3;
                    if (mod == 1) fixedOffset = -0.5f;      // 第二个兵
                    else if (mod == 2) fixedOffset = 0.5f;  // 第三个兵

                    // 【核心修改 2】传入 Mask 和 固定偏移
                    // Top: X轴偏移
                    SpawnWaveMinion(ecb, minionPrefab, topLaneBuffer, typeIndex, minionLevel, globalUpgradeData, fixedOffset, new float3(1, 0, 0));
                    
                    // Mid: X和Z轴同时偏移 (不考虑向量长度，直接加)
                    SpawnWaveMinion(ecb, minionPrefab, midLaneBuffer, typeIndex, minionLevel, globalUpgradeData, fixedOffset, new float3(1, 0, -1));
                    
                    // Bot: Z轴偏移
                    SpawnWaveMinion(ecb, minionPrefab, botLaneBuffer, typeIndex, minionLevel, globalUpgradeData, fixedOffset, new float3(0, 0, 1));

                    minionSpawnAspect.CountSpawnedInWave++;
                    if (minionSpawnAspect.IsWaveSpawned)
                    {
                        minionSpawnAspect.ResetMinionTimer();
                        minionSpawnAspect.ResetWaveTimer();
                        minionSpawnAspect.ResetSpawnCounter();
                    }
                    else
                    {
                        minionSpawnAspect.ResetMinionTimer();
                    }
                }
            }

            // ============================================================
            // 逻辑 B: 玩家输入生成 (Player Input Spawning)
            // ============================================================
            foreach (var (input, gold, transform, team, tech) in 
                     SystemAPI.Query<RefRO<SpawnMinionInput>, RefRW<PlayerGold>, RefRO<LocalTransform>, RefRO<MobaTeam>
                             , RefRO<PlayerMinionTech>>()
                     .WithAll<Simulate>()) // 确保只处理模拟状态
            {
                // 1. 基础校验
                if (!input.ValueRO.IsSpawning || input.ValueRO.MinionTypeId <= 0) continue;

                int typeIndex = input.ValueRO.MinionTypeId - 1;
                if (typeIndex >= minionPrefabsBuffer.Length) continue;

                // 2. 兵种与金币校验
                if (!globalUpgradeData.BlobRef.IsCreated) continue;
                ref var types = ref globalUpgradeData.BlobRef.Value.UnitTypes;
                if (typeIndex >= types.Length) continue;

                // --- 获取配置数据 ---
                int minionLevel = tech.ValueRO.GetLevel(typeIndex);
                ref var levelData = ref types[typeIndex].Levels[minionLevel];
                
                // [注意] 假设你在 Blob 的 MinionLevelData 结构体里加了 UnitCount 字段
                // 如果没加，你需要去 Blob 定义里加一下。这里暂时假设有。
                // int unitCount = levelData.UnitCount; 
                int unitCount = levelData.UnitCount;
                float totalCost = levelData.SpawnCost;
                if (gold.ValueRO.CurrentValue < totalCost) continue;
                gold.ValueRW.CurrentValue -= totalCost;

                var prefabEntity = minionPrefabsBuffer[typeIndex].Value;
                float3 centerPos = new float3(input.ValueRO.SpawnPosition.x, 0.5f, input.ValueRO.SpawnPosition.y);

                // --- 计算阵列参数 (与 Manager 保持一致) ---
                int rowCapacity = (int)math.ceil(math.sqrt(unitCount));
                float spacing = 1.5f; 
                float3 rowDir = math.normalize(new float3(1, 0, -1)); // y = -x
                float3 colDir = math.normalize(new float3(1, 0, 1));  // y = x
                
                int totalRows = (int)math.ceil((float)unitCount / rowCapacity);
                float totalWidth = (rowCapacity - 1) * spacing;
                float totalHeight = (totalRows - 1) * spacing;
                float3 startOffset = centerPos - (rowDir * totalWidth * 0.5f) - (colDir * totalHeight * 0.5f);

                // --- 计算朝向 ---
                quaternion startRotation;
                if (team.ValueRO.Value == TeamType.Blue)
                    startRotation = quaternion.Euler(0, math.radians(-135), 0);
                else
                    startRotation = quaternion.Euler(0, math.radians(45), 0);

                // --- 准备直达终点 ---
                // 创建一个仅包含终点的 NativeArray (或者到时候直接 append)
                float3 targetBasePos;
                if (team.ValueRO.Value == TeamType.Blue)
                    targetBasePos = midLaneBuffer.IsEmpty ? float3.zero : midLaneBuffer[midLaneBuffer.Length - 1].Value + new float3(10, 0, 10);
                else
                    targetBasePos = midLaneBuffer.IsEmpty ? float3.zero : midLaneBuffer[0].Value - new float3(10, 0, 10);
                

                for (int i = 0; i < unitCount; i++)
                {
                    int rowIndex = i / rowCapacity;
                    int colIndex = i % rowCapacity;
                    float3 spawnPos = startOffset + (colDir * rowIndex * spacing) + (rowDir * colIndex * spacing);

                    // 生成实体
                    var newMinion = CreateMinionEntity(
                        ecb, prefabEntity, spawnPos, team.ValueRO, 
                        typeIndex, minionLevel, 
                        globalUpgradeData,default, false
                    );

                    // 覆盖朝向 (因为 CreateMinionEntity 里默认是 identity 或者 prefab 的朝向)
                    // 需要先把 Scale 拿出来，或者重新 SetTransform
                    // 这里简单处理：再 Set 一次
                    float scale = ((float)minionLevel + 1) / 2;
                    ecb.SetComponent(newMinion, LocalTransform.FromPositionRotationScale(spawnPos, startRotation, scale));

                    // 添加直达终点
                    ecb.AppendToBuffer(newMinion, new MinionPathPosition { Value = targetBasePos });
                }
                
            }
        }

        // --- 封装的 Wave 生成逻辑 ---
        // --- 【修改】使用固定偏移 ---
        private void SpawnWaveMinion(EntityCommandBuffer ecb, Entity prefab, DynamicBuffer<MinionPathPosition> pathBuffer, int typeIndex, int level, in MinionUpgradeData data, float offsetValue, float3 offsetMask)
        {
            if (pathBuffer.IsEmpty) return;

            // 计算最终偏移向量：固定值 * 方向遮罩
            // 例如 Top: -0.5 * (1,0,0) = (-0.5, 0, 0)
            // Mid: -0.5 * (1,0,1) = (-0.5, 0, -0.5)
            float3 finalOffset = offsetValue * offsetMask;

            // 应用偏移
            CreateMinionEntity(ecb, prefab, pathBuffer[0].Value + finalOffset, new MobaTeam { Value = TeamType.Blue }, typeIndex, level, data, pathBuffer, false);
            CreateMinionEntity(ecb, prefab, pathBuffer[^1].Value + finalOffset, new MobaTeam { Value = TeamType.Red }, typeIndex, level, data, pathBuffer, true);
        }

        // --- 核心公共生成函数 ---
        private Entity CreateMinionEntity(
            EntityCommandBuffer ecb, 
            Entity prefab, 
            float3 position, 
            MobaTeam team, 
            int typeIndex, // [新增] 兵种索引
            int level,     // [新增] 等级索引
            in MinionUpgradeData upgradeData,
            DynamicBuffer<MinionPathPosition> pathBufferToCopy, // 这里接收 Buffer
            bool reversePath) 
        {
            // 1. 安全检查：确保 Blob 存在
            if (!upgradeData.BlobRef.IsCreated) return Entity.Null;
            
            // 2. 第一层查找：获取兵种数据
            ref var unitTypes = ref upgradeData.BlobRef.Value.UnitTypes;
            if (typeIndex >= unitTypes.Length) return Entity.Null;
            ref var currentTypeLevels = ref unitTypes[typeIndex].Levels;

            // 3. 第二层查找：获取等级数据
            if (level >= currentTypeLevels.Length) return Entity.Null;
            ref var stats = ref currentTypeLevels[level]; // 拿到最终数据

            var minion = ecb.Instantiate(prefab);

            // 4. 应用数据 (现在是特定的兵种+等级数据)
            ecb.SetComponent(minion, new MaxHitPoints { Value = stats.Hp });
            ecb.SetComponent(minion, new CurrentHitPoints { Value = stats.Hp });
            ecb.SetComponent(minion, new AttackDamage { Value = stats.Attack });
            ecb.SetComponent(minion, new MinionLevel { Value = level });
            
            // [新增] 设置移动速度
            ecb.SetComponent(minion, new CharacterMoveSpeed { Value = stats.MoveSpeed });
            ecb.SetComponent(minion, new OriCharacterMoveSpeed { Value = stats.MoveSpeed });
            // [新增] 添加类型索引组件，供后续系统查询配置使用
            ecb.AddComponent(minion, new MinionTypeIndex { Value = typeIndex });
            ecb.SetComponent(minion, team);
            float scale = ((float)level + 1) / 2; 
            var transform = LocalTransform.FromPosition(position).WithScale(scale);
            ecb.SetComponent(minion, transform);
            ecb.AddComponent(minion, new NpcTargetCheckTimer { 
                Value = UnityEngine.Random.Range(0.1f, 1.0f) // 随机分散压力
            });
            // 路径处理逻辑
            if (pathBufferToCopy.IsCreated && !pathBufferToCopy.IsEmpty)
            {
                if (reversePath)
                {
                    for (var i = pathBufferToCopy.Length - 1; i >= 0; i--)
                        ecb.AppendToBuffer(minion, pathBufferToCopy[i]);
                }
                else
                {
                    for (var i = 0; i < pathBufferToCopy.Length; i++)
                        ecb.AppendToBuffer(minion, pathBufferToCopy[i]);
                }
            }

            return minion;
        }
    }
}