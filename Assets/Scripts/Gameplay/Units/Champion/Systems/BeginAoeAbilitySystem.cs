using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Collections;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial struct BeginAoeAbilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            // 依然保持 NFE 的预测检查逻辑
            if (!networkTime.IsFirstTimeFullyPredictingTick) return;

            // 使用 ParallelWriter 允许 Job 多线程并发写入
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            // 调度并行 Job 处理所有实体
            var job = new BeginAoeAbilityJob
            {
                Ecb = ecb,
                CurrentTick = networkTime.ServerTick,
                BatchSize = networkTime.SimulationStepBatchSize,
                IsServer = state.WorldUnmanaged.IsServer()
            };

            // 使用 ScheduleParallel 将压力完全转移到 Job Worker 线程
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct BeginAoeAbilityJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NetworkTick CurrentTick;
            public int BatchSize;
            public bool IsServer;

            // [ChunkIndexInQuery] 用于多线程写入 ECB 时保持确定性
            public void Execute(Entity entity, [ChunkIndexInQuery] int sortKey, AoeAspect aoe)
            {
                var isOnCooldown = true;
                var curTargetTicks = new AbilityCooldownTargetTicks();

                // 核心冷却检测逻辑移入 Job 内部，由多核并行处理
                for (var i = 0u; i < BatchSize; i++)
                {
                    var testTick = CurrentTick;
                    testTick.Subtract(i);

                    if (!aoe.CooldownTargetTicks.GetDataAtTick(testTick, out curTargetTicks))
                    {
                        curTargetTicks.AoeAbility = NetworkTick.Invalid;
                    }

                    if (curTargetTicks.AoeAbility == NetworkTick.Invalid ||
                        !curTargetTicks.AoeAbility.IsNewerThan(CurrentTick))
                    {
                        isOnCooldown = false;
                        break;
                    }
                }

                if (isOnCooldown) return;

                if (aoe.ShouldAttack)
                {
                    // 使用 ParallelWriter 的实例化接口
                    var newAoeAbility = Ecb.Instantiate(sortKey, aoe.AbilityPrefab);
                    var abilityTransform = LocalTransform.FromPosition(aoe.AttackPosition);
                    Ecb.SetComponent(sortKey, newAoeAbility, abilityTransform);
                    Ecb.SetComponent(sortKey, newAoeAbility, aoe.Team);

                    if (IsServer) return;

                    var newCooldownTargetTick = CurrentTick;
                    newCooldownTargetTick.Add(aoe.CooldownTicks);
                    curTargetTicks.AoeAbility = newCooldownTargetTick;

                    var nextTick = CurrentTick;
                    nextTick.Add(1u);
                    curTargetTicks.Tick = nextTick;

                    aoe.CooldownTargetTicks.AddCommandData(curTargetTicks);
                }
            }
        }
    }
}