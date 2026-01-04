using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using Unity.Mathematics;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(SimulationSystemGroup))] 
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct NpcTargetingSystem : ISystem
    {
        private CollisionFilter _npcAttackFilter;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _npcAttackFilter = new CollisionFilter
            {
                BelongsTo = 1 << 6, 
                CollidesWith = 1 << 1 | 1 << 2 | 1 << 4 
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            state.Dependency = new NpcTargetingJob
            {
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                CollisionFilter = _npcAttackFilter,
                MobaTeamLookup = SystemAPI.GetComponentLookup<MobaTeam>(true),
                TowerTagLookup = SystemAPI.GetComponentLookup<TowerTag>(true),
                DeltaTime = dt // 传入 DeltaTime
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct NpcTargetingJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public CollisionFilter CollisionFilter;
        [ReadOnly] public ComponentLookup<MobaTeam> MobaTeamLookup;
        [ReadOnly] public ComponentLookup<TowerTag> TowerTagLookup;
        
        public float DeltaTime;

        private void Execute(Entity npcEntity, ref NpcTargetEntity targetEntity, 
            ref NpcTargetCheckTimer timer, // 【新增】引用计时器
            in LocalTransform transform,
            in NpcTargetRange targetRange) 
        {
            // 1. 计时器倒数
            timer.Value -= DeltaTime;

            // 2. 只有当计时器 <= 0 时，才允许“换目标”或“搜寻新目标”
            bool allowSearch = timer.Value <= 0;

            // 3. 检查现有目标是否有效
            if (targetEntity.Value != Entity.Null)
            {
                // 如果目标实体不存在了（被销毁了），重置目标
                if (!MobaTeamLookup.HasComponent(targetEntity.Value))
                {
                    targetEntity.Value = Entity.Null;
                    // 目标没了，应该立即允许搜索，不要傻等
                    allowSearch = true; 
                }
                else
                {
                    // 目标还在，检查距离
                    var targetPos = CollisionWorld.Bodies[CollisionWorld.GetRigidBodyIndex(targetEntity.Value)].WorldFromBody.pos;
                    float distSq = math.distancesq(transform.Position, targetPos);
                    
                    // 如果超出了攻击范围，且允许搜索，则尝试换人
                    // 如果没超出范围，就死咬不放，return
                    if (distSq <= targetRange.Value * targetRange.Value)
                    {
                        return; // 继续锁定当前目标，无需开销
                    }
                }
            }

            // 4. 如果还没到搜索时间，且当前没有目标，那就只能干等，节省性能
            if (!allowSearch) return;

            // 5. === 重置计时器 (关键) ===
            // 找到目标后歇 0.5 秒；找不到也歇 0.5 秒，别一直死循环找
            timer.Value = 0.5f; 

            // 6. === 开始昂贵的物理搜索 ===
            var hits = new NativeList<DistanceHit>(Allocator.TempJob);

            if (CollisionWorld.OverlapSphere(transform.Position, targetRange.Value, ref hits, CollisionFilter))
            {
                var closestTowerDist = float.MaxValue;
                var closestUnitDist = float.MaxValue;
                
                var bestTower = Entity.Null;
                var bestUnit = Entity.Null;

                foreach (var hit in hits)
                {
                    if(!MobaTeamLookup.TryGetComponent(hit.Entity, out var mobaTeam)) continue;
                    if(mobaTeam.Value == MobaTeamLookup[npcEntity].Value) continue;

                    if (TowerTagLookup.HasComponent(hit.Entity))
                    {
                        if (hit.Distance < closestTowerDist)
                        {
                            closestTowerDist = hit.Distance;
                            bestTower = hit.Entity;
                        }
                    }
                    else
                    {
                        if (hit.Distance < closestUnitDist)
                        {
                            closestUnitDist = hit.Distance;
                            bestUnit = hit.Entity;
                        }
                    }
                }

                if (bestTower != Entity.Null) targetEntity.Value = bestTower;
                else targetEntity.Value = bestUnit;
            }
            else
            {
                targetEntity.Value = Entity.Null;
            }

            hits.Dispose();
        }
    }
}