using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    
    [UpdateInGroup(typeof(SimulationSystemGroup))] // 改为通用组
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)] // 关键：只在 Server 世界运行！
    public partial struct NpcAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<GamePlayingTag>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<MinionUpgradeData>(); 
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            var upgradeData = SystemAPI.GetSingleton<MinionUpgradeData>().BlobRef; // [新增]
            state.Dependency = new NpcAttackJob
            {
                UpgradeData = upgradeData, // [新增] 传入 Blob
                
                CurrentTick = networkTime.ServerTick,
                TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                ECB = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct NpcAttackJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<MinionUpgradeBlob> UpgradeData; // [新增]
        [ReadOnly] public NetworkTick CurrentTick;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        
        public EntityCommandBuffer.ParallelWriter ECB;

        [BurstCompile]
        private void Execute(ref DynamicBuffer<NpcAttackCooldown> attackCooldown,
            ref CharacterMoveSpeed moveSpeed,
            in OriCharacterMoveSpeed oriCharacterMoveSpeed,
            in NpcAttackProperties attackProperties,
            in NpcTargetEntity targetEntity, 
            Entity npcEntity, 
            MobaTeam team, 
            [ChunkIndexInQuery] int sortKey,
            in MinionTypeIndex typeIndex,
            in MinionLevel currentLevel,
            in AttackDamage attackDamage,
            in NpcAttackRange attackRange) // [新增] 攻击距离参数
        {
            // 1. 目标丢失检查：恢复速度
            if (!TransformLookup.HasComponent(targetEntity.Value))
            {
                moveSpeed.Value = oriCharacterMoveSpeed.Value;
                return;
            }
            if (!attackCooldown.GetDataAtTick(CurrentTick, out var cooldownExpirationTick))
            {
                cooldownExpirationTick.Value = NetworkTick.Invalid;
            }

            // 2. [核心修改] 距离判定
            var myPos = TransformLookup[npcEntity].Position;
            var targetPos = TransformLookup[targetEntity.Value].Position;
            float distSq = math.distancesq(myPos, targetPos);
            
            // 如果距离 大于 攻击距离
            if (distSq > attackRange.Value * attackRange.Value)
            {
                // [关键] 恢复移动速度，让 MoveMinionSystem 继续追逐
                moveSpeed.Value = oriCharacterMoveSpeed.Value;
                return; // 不攻击，继续追
            }

            // 3. 进入攻击距离：刹车
            moveSpeed.Value = 0f;

            // ... Cooldown 判定和开火逻辑保持不变 ...
            var canAttack = !cooldownExpirationTick.Value.IsValid ||
                            CurrentTick.IsNewerThan(cooldownExpirationTick.Value);
            if (!canAttack) return;

            var spawnPosition = TransformLookup[npcEntity].Position + attackProperties.FirePointOffset;
            var targetPosition = TransformLookup[targetEntity.Value].Position;
            
            var newAttack = ECB.Instantiate(sortKey, attackProperties.AttackPrefab);
            
            ECB.SetComponent(sortKey, newAttack, new DamageOnTrigger { Value = attackDamage.Value });
            float scale = 1.0f;
            if (UpgradeData.IsCreated && typeIndex.Value < UpgradeData.Value.UnitTypes.Length)
            {
                ref var levels = ref UpgradeData.Value.UnitTypes[typeIndex.Value].Levels;
                if (currentLevel.Value < levels.Length)
                {
                    scale = levels[currentLevel.Value].ProjectileScale;
                }
            }
            
            var newAttackTransform = LocalTransform.FromPositionRotation(spawnPosition,
                quaternion.LookRotationSafe(targetPosition - spawnPosition, math.up())).WithScale(((float)currentLevel.Value+1)/2);;

            ECB.SetComponent(sortKey, newAttack, newAttackTransform);
            ECB.SetComponent(sortKey, newAttack, team);

            var newCooldownTick = CurrentTick;
            newCooldownTick.Add(attackProperties.CooldownTickCount);
            attackCooldown.AddCommandData(new NpcAttackCooldown { Tick = CurrentTick, Value = newCooldownTick });
        }
    }
}