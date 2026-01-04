using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class MinimapGPUSystem : SystemBase
    {
        // 渲染用的托管数组（必须保留，因为 Graphics API 限制）
        private Matrix4x4[] _managedMatrices = new Matrix4x4[1023];
        private Vector4[] _managedColors = new Vector4[1023];
        private MaterialPropertyBlock _props;

        // Shader 属性 ID
        private static readonly int PropColor = Shader.PropertyToID("_Color");
        private static readonly int PropMapSize = Shader.PropertyToID("_MapSize");
        private static readonly int PropMinimapRect = Shader.PropertyToID("_MinimapRect");
        private static readonly int PropMapCenter = Shader.PropertyToID("_MapCenter");

        // 计算矩阵和颜色的并行 Job
        [BurstCompile]
        public partial struct PrepareMinimapJob : IJobEntity
        {
            [WriteOnly] public NativeArray<Matrix4x4> OutMatrices;
            [WriteOnly] public NativeArray<Vector4> OutColors;
            public float BaseScale;
            public Vector4 ColorBlue;
            public Vector4 ColorRed;

            // Execute 会在工作线程并行执行
            public void Execute([EntityIndexInQuery] int index, in LocalTransform transform, in MobaTeam team)
            {
                
                OutMatrices[index] = Matrix4x4.TRS(transform.Position, Quaternion.identity, new Vector3(BaseScale, BaseScale, BaseScale));

                // 设置颜色
                OutColors[index] = (team.Value == TeamType.Blue) ? ColorBlue : ColorRed;
            }
        }

        protected override void OnCreate()
        {
            _props = new MaterialPropertyBlock();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<MinimapConfig>(out var config)) return;
            if (config.DotMesh == null || config.DotMaterial == null || config.BGMaterial == null) return;
            config.DotMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000000f);
            Vector4 rectVec = MinimapUISync.ScreenRectVector;
            if (rectVec.z <= 0) return;

            // 1. 绘制背景 (逻辑保持不变，因为只有一笔)
            DrawMinimapBackground(config, rectVec);

            // 2. 准备小兵数据
            var query = SystemAPI.QueryBuilder().WithAll<LocalTransform, MobaTeam>().Build();
            int entityCount = query.CalculateEntityCount();
            if (entityCount == 0) return;

            // 分配临时原生数组，用于 Job 写入
            var nativeMatrices = new NativeArray<Matrix4x4>(entityCount, Allocator.TempJob);
            var nativeColors = new NativeArray<Vector4>(entityCount, Allocator.TempJob);

            // 3. 调度 Job (将原本主线程的 for 循环移入这里)
            var job = new PrepareMinimapJob
            {
                OutMatrices = nativeMatrices,
                OutColors = nativeColors,
                BaseScale = config.BaseScale,
                ColorBlue = new Vector4(0, 0, 1, 1),
                ColorRed = new Vector4(1, 0, 0, 1)
            };

            // 运行并行计算
            Dependency = job.ScheduleParallel(query, Dependency);
            
            // 因为接下来的 Graphics 调用是托管代码且需要数据，必须在这里 Complete
            Dependency.Complete();

            // 4. 批量提交渲染 (每 1023 个一组)
            RenderMinimapDots(config, entityCount, rectVec, nativeMatrices, nativeColors);

            // 5. 释放临时数组
            nativeMatrices.Dispose();
            nativeColors.Dispose();
        }

        private void DrawMinimapBackground(MinimapConfig config, Vector4 rectVec)
        {
            _props.Clear();
            _props.SetVector(PropMinimapRect, rectVec);
            Matrix4x4 bgMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * rectVec.z);
            Graphics.DrawMesh(config.DotMesh, bgMatrix, config.BGMaterial, 0, null, 0, _props);
        }

        private void RenderMinimapDots(MinimapConfig config, int totalCount, Vector4 rectVec, NativeArray<Matrix4x4> nativeMatrices, NativeArray<Vector4> nativeColors)
        {
            int batchIndex = 0;
            while (batchIndex < totalCount)
            {
                int drawCount = math.min(1023, totalCount - batchIndex);
                _props.Clear();

                // 核心：使用优化的 NativeArray.Copy 快速将数据搬运到托管数组
                NativeArray<Matrix4x4>.Copy(nativeMatrices, batchIndex, _managedMatrices, 0, drawCount);
                NativeArray<Vector4>.Copy(nativeColors, batchIndex, _managedColors, 0, drawCount);

                // 设置 Shader 全局参数 
                _props.SetVector(PropMinimapRect, rectVec);
                _props.SetFloat(PropMapSize, config.MapSize);
                _props.SetVector(PropMapCenter, Vector4.zero);
                _props.SetVectorArray(PropColor, _managedColors);

                Graphics.DrawMeshInstanced(
                    config.DotMesh,
                    0,
                    config.DotMaterial,
                    _managedMatrices,
                    drawCount,
                    _props,
                    ShadowCastingMode.Off,
                    false,
                    0,
                    null,
                    LightProbeUsage.Off
                );

                batchIndex += drawCount;
            }
        }
    }
}