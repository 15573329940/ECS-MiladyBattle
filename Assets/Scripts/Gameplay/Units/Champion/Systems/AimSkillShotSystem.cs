// 1. AimSkillShotSystem.cs
// 只负责计算：鼠标在哪？Input值是多少？
// 别碰 GameObject！
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial struct AimSkillShotSystem : ISystem
    {
        private CollisionFilter _selectionFilter;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MainCameraTag>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _selectionFilter = new CollisionFilter
            {
                BelongsTo = 1 << 5, 
                CollidesWith = 1 << 0 
            };
        }

        public void OnUpdate(ref SystemState state)
        {
            // 注意：这里去掉了 SkillShotUIReference
            foreach (var (aimInput, transform) in SystemAPI
                         .Query<RefRW<AimInput>, LocalTransform>()
                         .WithAll<AimSkillShotTag, OwnerChampTag>()) // 依然依赖 Tag，但只为了计算输入
            {
                var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
                if (!SystemAPI.HasComponent<MainCameraTag>(SystemAPI.GetSingletonEntity<MainCameraTag>())) continue;
                
                var cameraEntity = SystemAPI.GetSingletonEntity<MainCameraTag>();
                var mainCamera = state.EntityManager.GetComponentObject<MainCamera>(cameraEntity).Value;
                
                var mousePosition = Input.mousePosition;
                mousePosition.z = 1000f;
                var worldPosition = mainCamera.ScreenToWorldPoint(mousePosition);

                var selectionInput = new RaycastInput
                {
                    Start = mainCamera.transform.position,
                    End = worldPosition,
                    Filter = _selectionFilter
                };
                
                if (collisionWorld.CastRay(selectionInput, out var closestHit))
                {
                    var directionToTarget = closestHit.Position - transform.Position;
                    directionToTarget.y = 0; // 强制水平
                    directionToTarget = math.normalize(directionToTarget);
                    
                    // 只更新纯数据组件
                    aimInput.ValueRW.Value = directionToTarget;
                }
            }
        }
    }
}