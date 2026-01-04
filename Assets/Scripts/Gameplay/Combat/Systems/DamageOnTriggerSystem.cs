using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.NetCode;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct DamageOnTriggerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        
            // 1. 获取 ParallelWriter (并行写入器)
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var damageOnTriggerJob = new DamageOnTriggerJob
            {
                // [ReadOnly] 用于读取组件数据
                DamageOnTriggerLookup = SystemAPI.GetComponentLookup<DamageOnTrigger>(true),
                TeamLookup = SystemAPI.GetComponentLookup<MobaTeam>(true),
            
                // [ReadOnly] 用于读取 Buffer 做判断 (HasBuffer)
                // 注意：这里必须传 true (ReadOnly)，因为我们只检查不写入
                DamageBufferLookup = SystemAPI.GetBufferLookup<DamageBufferElement>(true),
                
                // [ReadOnly] 用于检查 "是否已经伤害过"
                AlreadyDamagedLookup = SystemAPI.GetBufferLookup<AlreadyDamagedEntity>(true), 
            
                // [WriteOnly] 所有的写入操作都交给 ECB
                ECB = ecb 
            };

            // 2. 调度 Job
            // ITriggerEventsJob 的标准 API 是 .Schedule，它内部支持物理引擎的并行分发
            state.Dependency = damageOnTriggerJob.Schedule(simulationSingleton, state.Dependency);
        }
    }

    [BurstCompile]
    public struct DamageOnTriggerJob : ITriggerEventsJob
    {
        [ReadOnly] public ComponentLookup<DamageOnTrigger> DamageOnTriggerLookup;
        [ReadOnly] public ComponentLookup<MobaTeam> TeamLookup;
        
        // 【修复】加回这个 Lookup，但标记为 ReadOnly，仅用于检查 HasBuffer
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        
        [ReadOnly] public BufferLookup<AlreadyDamagedEntity> AlreadyDamagedLookup;
    
        // 并行写入器
        public EntityCommandBuffer.ParallelWriter ECB;
        
        public void Execute(TriggerEvent triggerEvent)
        {
            Entity damageDealingEntity;
            Entity damageReceivingEntity;
            
            // 1. 确定谁是攻击者，谁是受害者
            // 这里需要读取 DamageBufferLookup 来判断是否存在 Buffer
            if (DamageOnTriggerLookup.HasComponent(triggerEvent.EntityA) &&
                DamageBufferLookup.HasBuffer(triggerEvent.EntityB))
            {
                damageDealingEntity = triggerEvent.EntityA;
                damageReceivingEntity = triggerEvent.EntityB;
            }
            else if (DamageOnTriggerLookup.HasComponent(triggerEvent.EntityB) &&
                     DamageBufferLookup.HasBuffer(triggerEvent.EntityA))
            {
                damageDealingEntity = triggerEvent.EntityB;
                damageReceivingEntity = triggerEvent.EntityA;
            }
            else
            {
                return;
            }
            
            // 2. 阵营检查 (Friendly Fire Check) - 放在前面效率更高
            if (TeamLookup.TryGetComponent(damageDealingEntity, out var damageDealingTeam) &&
                TeamLookup.TryGetComponent(damageReceivingEntity, out var damageReceivingTeam))
            {
                if (damageDealingTeam.Value == damageReceivingTeam.Value) return;
            }

            // 3. 查重 (防止同一帧多次伤害)
            // 遍历 AlreadyDamagedEntity Buffer
            if (AlreadyDamagedLookup.TryGetBuffer(damageDealingEntity, out var alreadyDamagedBuffer))
            {
                foreach (var alreadyDamagedEntity in alreadyDamagedBuffer)
                {
                    if (alreadyDamagedEntity.Value.Equals(damageReceivingEntity)) return;
                }
            }

            // 4. 执行伤害逻辑 (录制命令)
            var damageOnTrigger = DamageOnTriggerLookup[damageDealingEntity];

            // 使用 BodyIndexA 作为 sortKey，保证多线程录制的确定性
            // 这里的 AppendToBuffer 对应的是 ECB，不是直接写 Buffer
            ECB.AppendToBuffer(triggerEvent.BodyIndexA, damageReceivingEntity, new DamageBufferElement { Value = damageOnTrigger.Value });
            
            // 记录 "我打过你了"
            ECB.AppendToBuffer(triggerEvent.BodyIndexA, damageDealingEntity, new AlreadyDamagedEntity { Value = damageReceivingEntity });
        }
    }
}