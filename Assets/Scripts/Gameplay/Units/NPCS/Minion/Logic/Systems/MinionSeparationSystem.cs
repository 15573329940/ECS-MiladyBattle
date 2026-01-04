using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct MinionSeparationJobSystem : ISystem
    {
        
        private ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState state)
        {
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

            _transformLookup.Update(ref state);

            float separationSpeed = 8.0f; 
            float baseRadius = 0.5f;      

            var job = new SeparationJob
            {
                CollisionWorld = physicsWorld.CollisionWorld,
                TransformLookup = _transformLookup,
                DeltaTime = SystemAPI.Time.DeltaTime,
                SeparationSpeed = separationSpeed,
                BaseRadius = baseRadius
            };

            job.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SeparationJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] 
        [NativeDisableContainerSafetyRestriction] 
        public ComponentLookup<LocalTransform> TransformLookup; 

        public float DeltaTime;
        public float SeparationSpeed;
        public float BaseRadius;

        // 【修改点】在参数里加上 in CharacterMoveSpeed moveSpeed
        private void Execute(Entity entity, ref LocalTransform transform, in PhysicsCollider collider, in CharacterMoveSpeed moveSpeed)
        {
            // ==========================================================
            // 【核心逻辑】如果正在攻击（速度为0），则“定如磐石”，不受斥力影响
            // ==========================================================
            if (moveSpeed.Value <= 0.01f) 
            {
                return;
            }

            if (!collider.Value.IsCreated) return;

            float myScale = transform.Scale;
            float myRadius = BaseRadius * myScale;
            float3 myPos = transform.Position;

            float queryRadius = myRadius * 3.0f; 

            CollisionFilter filter = collider.Value.Value.GetCollisionFilter();
            filter.CollidesWith &= ~(1u << 3);
            var hits = new NativeList<DistanceHit>(Allocator.Temp);

            var input = new PointDistanceInput
            {
                Position = myPos,
                MaxDistance = queryRadius, 
                Filter = filter
            };

            if (CollisionWorld.CalculateDistance(input, ref hits))
            {
                float3 totalRepulsion = float3.zero;
                int count = 0;

                foreach (var hit in hits)
                {
                    if (hit.Entity == entity) continue;

                    if (TransformLookup.TryGetComponent(hit.Entity, out var otherTransform))
                    {
                        float3 otherPos = otherTransform.Position;
                        float otherScale = otherTransform.Scale;
                        float otherRadius = BaseRadius * otherScale;

                        float3 dir = myPos - otherPos;
                        dir.y = 0; 

                        float distSq = math.lengthsq(dir);
                        float minDist = (myRadius + otherRadius) * 1.05f; 

                        if (distSq < minDist * minDist)
                        {
                            if (distSq < 0.0001f)
                            {
                                var random = Unity.Mathematics.Random.CreateFromIndex((uint)entity.Index);
                                float2 randomDir = random.NextFloat2Direction();
                                totalRepulsion += new float3(randomDir.x, 0, randomDir.y);
                            }
                            else
                            {
                                totalRepulsion += math.normalize(dir);
                            }
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    if (math.lengthsq(totalRepulsion) > 0.001f)
                    {
                        float3 finalDir = math.normalize(totalRepulsion);
                        transform.Position += finalDir * SeparationSpeed * DeltaTime;
                    }
                }
            }
            hits.Dispose();
        }
    }
}