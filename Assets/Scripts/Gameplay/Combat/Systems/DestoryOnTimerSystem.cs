using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace TMG.NFE_Tutorial
{
    // 如果这是预测物体，用 Predicted 组；如果是插值物体，建议改为 SimulationSystemGroup
    // 为了保险起见，这里先保持 Predicted，能跑通再说
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct DestroyOnTimerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
            var currentTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;

            new DestroyTimerJob
            {
                ECB = ecb,
                CurrentTick = currentTick
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    // 【关键修改】去掉了 [WithAll(typeof(Simulate))]，确保插值物体也能被销毁
    [WithNone(typeof(DestroyEntityTag))] 
    public partial struct DestroyTimerJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public NetworkTick CurrentTick;

        private void Execute(Entity entity, [ChunkIndexInQuery] int sortKey, in DestroyAtTick destroyAtTick)
        {
            if (!destroyAtTick.Value.IsValid) return;
            // 只要 Tick 到了，就打上销毁标签
            if (CurrentTick.Equals(destroyAtTick.Value) || CurrentTick.IsNewerThan(destroyAtTick.Value))
            {
                ECB.AddComponent<DestroyEntityTag>(sortKey, entity);
            }
        }
    }
}