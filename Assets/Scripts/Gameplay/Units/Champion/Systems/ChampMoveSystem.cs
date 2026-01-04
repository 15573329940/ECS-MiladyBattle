using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile] // 1. 为整个系统开启 Burst
    public partial struct ChampMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GamePlayingTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 2. 调度并行 Job，将 3.24ms 的压力分摊到所有 CPU 核心
            var job = new ChampMoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };
            
            // 使用 ScheduleParallel 而不是 Schedule，彻底消灭主线程阻塞
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct ChampMoveJob : IJobEntity
        {
            public float DeltaTime;

            // 3. 将原本 foreach 的内容移入 Execute
            public void Execute(ref LocalTransform transform, in ChampMoveTargetPosition movePosition, in CharacterMoveSpeed moveSpeed)
            {
                var moveTarget = movePosition.Value;
                moveTarget.y = transform.Position.y;
                
                // 距离判断
                if (math.distancesq(transform.Position, moveTarget) < 0.1f) return;

                // 向量计算
                var moveDirection = math.normalize(moveTarget - transform.Position);
                var moveVector = moveDirection * moveSpeed.Value * DeltaTime;
                
                // 更新组件
                transform.Position += moveVector;
                transform.Rotation = quaternion.LookRotationSafe(moveDirection, math.up());
            }
        }
    }
}