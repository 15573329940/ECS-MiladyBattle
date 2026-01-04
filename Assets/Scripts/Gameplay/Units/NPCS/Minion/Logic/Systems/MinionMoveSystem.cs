using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct MinionMoveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GamePlayingTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            // [修改] 引入 NpcTargetEntity 和 ComponentLookup 来获取目标位置
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            foreach (var (transform, pathPositions, pathIndex
                         , moveSpeed, targetEntity) in SystemAPI.Query<
                             RefRW<LocalTransform>, 
                             DynamicBuffer<MinionPathPosition>, 
                             RefRW<MinionPathIndex>, 
                             CharacterMoveSpeed,
                             RefRO<NpcTargetEntity>>() // [新增] 读取目标
                         .WithAll<Simulate>())
            {
                float3 curTargetPosition;

                // --- [新增逻辑] 追逐判定 ---
                bool isChasing = false;
                
                // 如果有目标，且目标依然存在
                if (targetEntity.ValueRO.Value != Entity.Null && transformLookup.HasComponent(targetEntity.ValueRO.Value))
                {
                    // 目标位置
                    curTargetPosition = transformLookup[targetEntity.ValueRO.Value].Position;
                    isChasing = true;
                }
                else
                {
                    // --- 原有逻辑：走兵线路径 ---
                    curTargetPosition = pathPositions[pathIndex.ValueRO.Value].Value;
                    if (math.distance(curTargetPosition, transform.ValueRO.Position) <= 1.5)
                    {
                        if(pathIndex.ValueRO.Value >= pathPositions.Length - 1) continue;
                        pathIndex.ValueRW.Value++;
                        curTargetPosition = pathPositions[pathIndex.ValueRO.Value].Value;
                    }
                }
                // -------------------------

                // 通用移动逻辑 (不管是追人还是走兵线，都走这里)
                curTargetPosition.y = transform.ValueRO.Position.y; // 忽略高度差
                // 只有当距离足够远才移动（防止重叠抖动），或者单纯让 NpcAttackSystem 控制刹车
                if (math.distancesq(curTargetPosition, transform.ValueRO.Position) > 0.01f)
                {
                    var curHeading = math.normalizesafe(curTargetPosition - transform.ValueRO.Position);
                    transform.ValueRW.Position += curHeading * moveSpeed.Value * deltaTime;
                    transform.ValueRW.Rotation = quaternion.LookRotationSafe(curHeading, math.up());
                }
            }
        }
    }
}