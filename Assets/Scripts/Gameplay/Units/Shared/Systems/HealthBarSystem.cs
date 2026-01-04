using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Collections; // 引用这个以使用 NativeDisableParallelForRestriction

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct HealthBarSystem : ISystem
    {
        // 声明查找表 (Lookup)，用于在 Job 中随机访问子物体数据
        private ComponentLookup<URPMaterialPropertyFillAmount> _fillAmountLookup;
        private ComponentLookup<URPMaterialPropertyBaseColor> _baseColorLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UIPrefabs>();
            // 初始化 Lookup
            _fillAmountLookup = state.GetComponentLookup<URPMaterialPropertyFillAmount>(isReadOnly: false);
            _baseColorLookup = state.GetComponentLookup<URPMaterialPropertyBaseColor>(isReadOnly: false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 1. 每帧必须更新 Lookup (因为 World 里的架构可能变了)
            _fillAmountLookup.Update(ref state);
            _baseColorLookup.Update(ref state);

            float dt = SystemAPI.Time.DeltaTime;
            float lerpSpeed = 5.0f;

            // 2. 调度 Job
            // 使用 ScheduleParallel 来真正利用多核 CPU
            var job = new UpdateHealthBarJob
            {
                DeltaTime = dt,
                LerpSpeed = lerpSpeed,
                // 传入 Lookup
                FillLookup = _fillAmountLookup,
                ColorLookup = _baseColorLookup
            };

            job.ScheduleParallel(); 
        }

        [BurstCompile]
        public partial struct UpdateHealthBarJob : IJobEntity
        {
            public float DeltaTime;
            public float LerpSpeed;

            // 重要：因为我们是在并行处理多个父物体，而这些父物体可能会指向不同的子物体。
            // 虽然逻辑上一个子物体只属于一个父物体，但 Job Safety System 默认认为随机写入是不安全的。
            // 这里我们需要加上这个 Attribute 来告诉 Unity："我知道我在干什么，不会有两个父物体同时写同一个血条"。
            [NativeDisableParallelForRestriction] 
            public ComponentLookup<URPMaterialPropertyFillAmount> FillLookup;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<URPMaterialPropertyBaseColor> ColorLookup;

            // Query 部分不需要改，IJobEntity 会自动帮我们将组件解包传入
            // in 关键字表示只读，提高性能
            void Execute(in CurrentHitPoints current, in MaxHitPoints max, in HealthBarRef barRef)
            {
                if (max.Value == 0) return;

                // 1. 获取子物体 Entity
                Entity barEntity = barRef.BarEntity;

                // 2. 检查子物体是否存在该组件 (安全检查)
                if (!FillLookup.HasComponent(barEntity)) return;

                // 3. 计算目标值
                float targetPercent = (float)current.Value / max.Value;

                // 4. 读取当前视觉值 (从 Lookup 中读)
                float currentVisualPercent = FillLookup[barEntity].Value;

                // 5. 计算插值
                float newVisualPercent = math.lerp(currentVisualPercent, targetPercent, DeltaTime * LerpSpeed);
                
                if (math.abs(newVisualPercent - targetPercent) < 0.001f)
                {
                    newVisualPercent = targetPercent;
                }

                // 6. 写回组件 (写入 Lookup)
                // 更新 Fill Amount
                var fillProp = FillLookup[barEntity];
                fillProp.Value = newVisualPercent;
                FillLookup[barEntity] = fillProp;

                // 同步更新颜色 (如果有)
                if (ColorLookup.HasComponent(barEntity))
                {
                    var colorProp = ColorLookup[barEntity];
                    // 假设你的 Shader 逻辑是用 x 传参
                    colorProp.Value = new float4(newVisualPercent, 0, 0, 0);
                    ColorLookup[barEntity] = colorProp;
                }
            }
        }
    }
}