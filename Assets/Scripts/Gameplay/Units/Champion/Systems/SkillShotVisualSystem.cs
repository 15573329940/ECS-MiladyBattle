// 3. SkillShotVisualSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    // 定义一个 Cleanup 组件来管理 GameObject 的生命周期
    public class SkillShotVisualObj : ICleanupComponentData
    {
        public GameObject Instance;
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))] // 关键：在渲染层运行
    public partial class SkillShotVisualSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            // A. 【创建】：有 Tag (Enabled) 但没 UI 的，创建它
            foreach (var (transform, entity) in SystemAPI.Query<LocalTransform>()
                         .WithAll<AimSkillShotTag, GhostOwnerIsLocal>() // 只显示给本机玩家
                         .WithNone<SkillShotVisualObj>() // 还没有 UI
                         .WithEntityAccess())
            {
                var prefab = SystemAPI.ManagedAPI.GetSingleton<UIPrefabs>().SkillShot;
                var instance = Object.Instantiate(prefab, transform.Position, Quaternion.identity);
                
                // 添加 Cleanup 组件持有引用
                ecb.AddComponent(entity, new SkillShotVisualObj { Instance = instance });
            }

            // B. 【更新】：有 UI 的，根据 Input 更新位置和旋转
            foreach (var (transform, visual, aimInput) in SystemAPI.Query<LocalTransform, SkillShotVisualObj, RefRO<AimInput>>())
            {
                if (visual.Instance == null) continue;

                // 1. 同步位置
                visual.Instance.transform.position = transform.Position;

                // 2. 同步旋转 (逻辑从你原来的 AimSystem 搬过来的)
                var direction = aimInput.ValueRO.Value;
                if (!direction.Equals(float3.zero))
                {
                    var angleRag = math.atan2(direction.z, direction.x);
                    var angleDeg = math.degrees(angleRag);
                    visual.Instance.transform.rotation = Quaternion.Euler(0f, -angleDeg, 0f);
                }
            }

            // C. 【销毁】：不再瞄准 (Disabled) 或 实体被删 的，销毁 UI
            foreach (var (visual, entity) in SystemAPI.Query<SkillShotVisualObj>().WithEntityAccess())
            {
                bool needDestroy = false;

                // 检查实体是否存在
                if (!SystemAPI.Exists(entity)) 
                {
                    needDestroy = true;
                }
                // 检查 Tag 是否被禁用 (Tag Disabled = 停止瞄准)
                else if (SystemAPI.HasComponent<AimSkillShotTag>(entity) && 
                         !SystemAPI.IsComponentEnabled<AimSkillShotTag>(entity))
                {
                    needDestroy = true;
                }
                // 检查 Tag 是否彻底没了 (防御性代码)
                else if (!SystemAPI.HasComponent<AimSkillShotTag>(entity))
                {
                    needDestroy = true;
                }

                if (needDestroy)
                {
                    if (visual.Instance != null) Object.Destroy(visual.Instance);
                    ecb.RemoveComponent<SkillShotVisualObj>(entity);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}