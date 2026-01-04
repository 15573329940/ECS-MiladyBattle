using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct StructureAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<GamePlayingTag>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        // 即使只有几个塔，ScheduleParallel 的开销也可以忽略不计
        // 主要是为了利用 Burst 的高性能和确定性
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();

            state.Dependency = new StructureAttackJob
            {
                CurrentTick = networkTime.ServerTick,
                TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                ECB = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    [WithNone(typeof(CharacterMoveSpeed))] // 显式排除小兵，只选塔/基地
    public partial struct StructureAttackJob : IJobEntity
    {
        [ReadOnly] public NetworkTick CurrentTick;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        public EntityCommandBuffer.ParallelWriter ECB;

        // 这里去掉了 MinionLevel，也不需要 sortKey (除非你要用来做随机种子，否则用 ChunkIndex 即可)
        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int sortKey,
            ref DynamicBuffer<NpcAttackCooldown> attackCooldown,
            ref NpcTargetEntity targetEntity,
            in NpcAttackProperties attackProperties,
            in AttackDamage attackDamage,
            in NpcAttackRange attackRange,
            in MobaTeam team,
            in LocalTransform transform) // 直接拿自己的 Transform
        {
            // 1. 检查目标是否存在
            if (!TransformLookup.HasComponent(targetEntity.Value))
            {
                targetEntity.Value = Entity.Null;
                return;
            }

            // 2. 检查距离 (塔不需要追，超出范围就放弃)
            var myPos = transform.Position;
            var targetPos = TransformLookup[targetEntity.Value].Position;
            
            if (math.distancesq(myPos, targetPos) > attackRange.Value * attackRange.Value)
            {
                // 超出范围，直接丢失目标，等待 TargetingSystem 下次扫描
                targetEntity.Value = Entity.Null;
                return;
            }

            // 3. 冷却检查
            if (!attackCooldown.GetDataAtTick(CurrentTick, out var cooldownExpirationTick))
            {
                cooldownExpirationTick.Value = NetworkTick.Invalid;
            }

            bool canAttack = !cooldownExpirationTick.Value.IsValid || CurrentTick.IsNewerThan(cooldownExpirationTick.Value);
            if (!canAttack) return;

            // 4. 发射子弹
            var spawnPosition = myPos + attackProperties.FirePointOffset;
            
            // 实例化子弹
            var newAttack = ECB.Instantiate(sortKey, attackProperties.AttackPrefab);
            
            // 设置伤害 (没有等级加成，直接用面板数值)
            ECB.SetComponent(sortKey, newAttack, new DamageOnTrigger { Value = attackDamage.Value });
            
            // 设置位置和朝向
            var newAttackTransform = LocalTransform.FromPositionRotation(
                spawnPosition,
                quaternion.LookRotationSafe(targetPos - spawnPosition, math.up())
            );
            // 注意：塔的子弹可能不需要缩放，如果需要，可以在 NpcAttackProperties 里加个 Scale 字段
            
            ECB.SetComponent(sortKey, newAttack, newAttackTransform);
            ECB.SetComponent(sortKey, newAttack, team); // 设置阵营，防止打到自己人

            // 5. 更新冷却
            var newCooldownTick = CurrentTick;
            newCooldownTick.Add(attackProperties.CooldownTickCount);
            attackCooldown.AddCommandData(new NpcAttackCooldown { Tick = CurrentTick, Value = newCooldownTick });
        }
    }
}