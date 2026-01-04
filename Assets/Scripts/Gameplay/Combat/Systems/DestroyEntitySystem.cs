using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct DestroyEntitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        // 主线程逻辑：只处理“游戏结束”这种特殊、低频事件
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            
            // 1. 【主线程部分】处理 Game Over 逻辑
            // 这一步不需要访问 LocalTransform，所以不会卡住主线程等待 MoveJob
            var ecbMain = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            
            // 使用 WithEntityAccess 单独查询，不涉及 Transform
            foreach (var (team, entity) in SystemAPI.Query<RefRO<MobaTeam>>()
                         .WithAll<DestroyEntityTag, GameOverOnDestroyTag>()
                         .WithEntityAccess())
            {
                if (state.WorldUnmanaged.IsServer())
                {
                    var gameOverPrefab = SystemAPI.GetSingleton<MobaPrefabs>().GameOverEntity;
                    var gameOverEntity = ecbMain.Instantiate(gameOverPrefab);
                    
                    var losing = team.ValueRO.Value;
                    var winning = losing == TeamType.Blue ? TeamType.Red : TeamType.Blue;
                    
                    Debug.Log($"{winning.ToString()} Team Won!!"); // 这里可以安全使用 Log
                    ecbMain.SetComponent(gameOverEntity, new WinningTeam { Value = winning });
                    
                    // 顺手移除 Tag 防止重复触发，真正的销毁交给下面的 Job
                    ecbMain.RemoveComponent<GameOverOnDestroyTag>(entity);
                }
            }

            // 2. 【Job 部分】处理批量销毁/隐藏
            // 这是原本最耗时的部分，现在放入 Burst Job 并行处理
            // 它会跟在 MinionMoveSystem 后面自动执行，不会卡死主线程
            var destroyJob = new DestroyEntityJob
            {
                ECB = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                IsServer = state.WorldUnmanaged.IsServer()
            };
            
            state.Dependency = destroyJob.ScheduleParallel(state.Dependency);
            if (!state.WorldUnmanaged.IsServer())
            {
                var hideJob = new ClientHideDeadJob();
                state.Dependency = hideJob.ScheduleParallel(state.Dependency);
            }
        }
    }

    [BurstCompile]
    public partial struct DestroyEntityJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public bool IsServer;

        // 注意：这里我们请求 Transform，但因为是在 Job 里，Unity 会自动安排依赖顺序
        private void Execute(Entity entity, [ChunkIndexInQuery] int sortKey, ref LocalTransform transform, in DestroyEntityTag tag)
        {
            if (IsServer)
            {
                // 服务端：彻底销毁
                ECB.DestroyEntity(sortKey, entity);
            }
            else
            {
                // 客户端：移到远处隐藏
                // 这种简单的数学赋值在 Burst 里只要 0.001ms
                //transform.Position = new float3(10000f, 10000f, 10000f);
                transform.Scale = 0f;
            }
        }
    }
    [BurstCompile]
    [WithNone(typeof(DestroyEntityTag))] // 有 Tag 的已经被上面的 Job 处理了，这里处理还没 Tag 但血没了的
    public partial struct ClientHideDeadJob : IJobEntity
    {
        private void Execute(ref LocalTransform transform, in CurrentHitPoints hp)
        {
            if (hp.Value <= 0)
            {
                transform.Scale = 0f; // 立即隐藏！
            }
        }
    }
}