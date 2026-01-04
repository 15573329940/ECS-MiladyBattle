using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    public struct TextInstanceData
    {
        public float3 StartPos;
        public int DigitIndex;
        public float StartTime;
        public float4 Color;
        public float OffsetX;
        public float Scale; // 【新增】每个字可以有独立的大小
    }
    
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class FloatingTextRenderSystem : SystemBase
    {
        private NativeList<TextInstanceData> _activeTexts;
        
        // --- 核心优化：持久化 Native 缓存 (复用内存，避免每帧 new) ---
        private NativeList<Matrix4x4> _nativeMatrices;
        private NativeList<float> _nativeDigitIndices;
        private NativeList<float> _nativeStartTimes;
        private NativeList<Vector4> _nativeColors;
        private NativeList<float> _nativeWidths;

        // --- 渲染用的 Managed Arrays (Graphics API 必须用) ---
        private Matrix4x4[] _managedMatrices = new Matrix4x4[1023];
        private float[] _managedDigitIndices = new float[1023];
        private float[] _managedStartTimes = new float[1023];
        private Vector4[] _managedColors = new Vector4[1023];
        private float[] _managedCharWidths = new float[1023];

        private MaterialPropertyBlock _props;
        
        // Shader IDs
        private static readonly int PropDigit = Shader.PropertyToID("_DigitIndex");
        private static readonly int PropStartTime = Shader.PropertyToID("_StartTime");
        private static readonly int PropColor = Shader.PropertyToID("_BaseColor");
        private static readonly int PropWidth = Shader.PropertyToID("_CharWidth");

        protected override void OnCreate()
        {
            _activeTexts = new NativeList<TextInstanceData>(2000, Allocator.Persistent);
            
            // 初始化缓存容器
            _nativeMatrices = new NativeList<Matrix4x4>(2000, Allocator.Persistent);
            _nativeDigitIndices = new NativeList<float>(2000, Allocator.Persistent);
            _nativeStartTimes = new NativeList<float>(2000, Allocator.Persistent);
            _nativeColors = new NativeList<Vector4>(2000, Allocator.Persistent);
            _nativeWidths = new NativeList<float>(2000, Allocator.Persistent);

            _props = new MaterialPropertyBlock();
        }

        protected override void OnDestroy()
        {
            if (_activeTexts.IsCreated) _activeTexts.Dispose();
            if (_nativeMatrices.IsCreated) _nativeMatrices.Dispose();
            if (_nativeDigitIndices.IsCreated) _nativeDigitIndices.Dispose();
            if (_nativeStartTimes.IsCreated) _nativeStartTimes.Dispose();
            if (_nativeColors.IsCreated) _nativeColors.Dispose();
            if (_nativeWidths.IsCreated) _nativeWidths.Dispose();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<FloatingTextConfig>(out var config)) return;
            
            // 获取本地阵营
            TeamType localTeamId = TeamType.None;
            foreach (var team in SystemAPI.Query<RefRO<MobaTeam>>().WithAll<GhostOwnerIsLocal>())
            {
                localTeamId = team.ValueRO.Value;
                break;
            }
            if (localTeamId == TeamType.None) return;

            float currentTime = (float)SystemAPI.Time.ElapsedTime;
            if (_activeTexts.Length > _activeTexts.Capacity - 500) 
            {
                _activeTexts.Capacity = _activeTexts.Capacity * 2;
            }

            // =========================================================
            // 1. [优化] 清理过期数据 (Job 化)
            // =========================================================
            if (_activeTexts.Length > 0)
            {
                Dependency = new CleanupExpiredTextsJob
                {
                    ActiveTexts = _activeTexts,
                    CurrentTime = currentTime
                }.Schedule(Dependency);
                Dependency.Complete();
            }

            // =========================================================
            // 2. [优化] 收集新飘字 (直接写入列表，无需出队循环)
            // =========================================================
            var ecb = SystemAPI.GetSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>()
                        .CreateCommandBuffer(World.Unmanaged).AsParallelWriter();
            
            var newTextsQueue = new NativeQueue<TextInstanceData>(Allocator.TempJob);
            
            Dependency = new CollectNewTextJob
            {
                Ecb = ecb,
                // 使用 ParallelWriter 直接追加到列表末尾
                NewTextsQueue = newTextsQueue.AsParallelWriter(),
                LocalTeamId = localTeamId,
                CurrentTime = currentTime
            }.ScheduleParallel(Dependency);

            // 渲染前必须完成数据收集和清理
            Dependency.Complete(); 

            while (newTextsQueue.TryDequeue(out var textData))
            {
                _activeTexts.Add(textData);
            }
            newTextsQueue.Dispose();

            int count = _activeTexts.Length;
            if (count == 0) return;

            // =========================================================
            // 3. 计算矩阵 (保持 Job 化)
            // =========================================================
            _nativeMatrices.ResizeUninitialized(count);
            _nativeDigitIndices.ResizeUninitialized(count);
            _nativeStartTimes.ResizeUninitialized(count);
            _nativeColors.ResizeUninitialized(count);
            _nativeWidths.ResizeUninitialized(count);

            Quaternion camRot = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

            new PrepareRenderDataJob
            {
                InputData = _activeTexts.AsArray(),
                CameraRot = camRot,
                CurrentTime = currentTime,
                OutMatrices = _nativeMatrices.AsArray(),
                OutDigits = _nativeDigitIndices.AsArray(),
                OutStartTimes = _nativeStartTimes.AsArray(),
                OutColors = _nativeColors.AsArray(),
                OutWidths = _nativeWidths.AsArray()
            }.Schedule(count, 64).Complete(); 

            // =========================================================
            // 4. 批量渲染提交 (保持不变)
            // =========================================================
            RenderBatches(config, count);
        }

       // --- 新增 Job: 清理过期 ---
        [BurstCompile]
        public struct CleanupExpiredTextsJob : IJob
        {
            public NativeList<TextInstanceData> ActiveTexts;
            public float CurrentTime;

            public void Execute()
            {
                for (int i = ActiveTexts.Length - 1; i >= 0; i--)
                {
                    if (CurrentTime - ActiveTexts[i].StartTime > 2.0f)
                    {
                        ActiveTexts.RemoveAtSwapBack(i);
                    }
                }
            }
        }

        // --- 修改 Job: 直接写入列表 ---
       [BurstCompile]
    public partial struct CollectNewTextJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;
        public NativeQueue<TextInstanceData>.ParallelWriter NewTextsQueue;
        public TeamType LocalTeamId;
        public float CurrentTime;

        private void Execute(Entity entity, [ChunkIndexInQuery] int sortKey,
            ref ClientDamageCursor cursor,
            in DynamicBuffer<DamageVisualBufferElement> buffer, 
            in LocalTransform transform, 
            in MobaTeam mobaTeam)
        {
            if (mobaTeam.Value == LocalTeamId) return;
            if (buffer.IsEmpty) return;

            NetworkTick maxTick = cursor.LastTick;
            bool hasUpdates = false;
            float3 basePos = transform.Position + new float3(0, 2.5f, 0);
            bool hasSpawnedVFX = false;

            foreach (var item in buffer)
            {
                if (!cursor.LastTick.IsValid || item.Tick.IsNewerThan(cursor.LastTick))
                {
                    float4 color = item.IsGold ? new float4(1, 1, 0, 1) : new float4(1, 0.2f, 0.2f, 1);
                    float baseScale = item.IsGold ? 0.75f : 0.5f; 

                    // 1. 找回火花特效 (Index 12)
                    if (!item.IsGold && !hasSpawnedVFX)
                    {
                         NewTextsQueue.Enqueue(new TextInstanceData {
                            StartPos = basePos + new float3(0, -1, 0),
                            DigitIndex = 12, 
                            StartTime = CurrentTime, 
                            Color = new float4(1), 
                            OffsetX = 0,
                            Scale = 0.75f
                        });
                        hasSpawnedVFX = true;
                    }

                    int val = item.Value;
                    if (val > 0)
                    {
                        // 2. 找回精确的字符数量计算（用于中心对齐）
                        int numDigits = 0;
                        int tempVal = val;
                        while (tempVal > 0) { tempVal /= 10; numDigits++; }
                        
                        float charWidth = baseScale; 
                        int totalChars = numDigits + (item.IsGold ? 2 : 0);
                        // 计算起始 X 偏移，让数字串居中
                        float currentX = -(totalChars * charWidth) / 2.0f + (charWidth / 2.0f);

                        // 3. 找回金币前置图标 (Index 10)
                        if (item.IsGold)
                        {
                            NewTextsQueue.Enqueue(new TextInstanceData { 
                                StartPos = basePos, DigitIndex = 10, StartTime = CurrentTime, 
                                Color = color, OffsetX = currentX, Scale = baseScale 
                            });
                            currentX += charWidth;
                        }

                        // 4. 找回从左到右的正向拆分逻辑
                        int divisor = 1;
                        for (int k = 0; k < numDigits - 1; k++) divisor *= 10;
                        for (int k = 0; k < numDigits; k++)
                        {
                            int digit = (val / divisor) % 10;
                            NewTextsQueue.Enqueue(new TextInstanceData { 
                                StartPos = basePos, DigitIndex = digit, StartTime = CurrentTime, 
                                Color = color, OffsetX = currentX, Scale = baseScale 
                            });
                            currentX += charWidth;
                            divisor /= 10;
                        }

                        // 5. 找回金币后置图标 (Index 11)
                        if (item.IsGold)
                        {
                            NewTextsQueue.Enqueue(new TextInstanceData { 
                                StartPos = basePos, DigitIndex = 11, StartTime = CurrentTime, 
                                Color = color, OffsetX = currentX, Scale = baseScale 
                            });
                        }
                    }

                    if (!maxTick.IsValid || item.Tick.IsNewerThan(maxTick)) maxTick = item.Tick;
                    hasUpdates = true;
                }
            }
            if (hasUpdates) cursor.LastTick = maxTick;
        }
    }

        // --- Job 2: 计算矩阵 ---
        [BurstCompile]
        public struct PrepareRenderDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<TextInstanceData> InputData;
            public Quaternion CameraRot;
            public float CurrentTime;

            [WriteOnly] public NativeArray<Matrix4x4> OutMatrices;
            [WriteOnly] public NativeArray<float> OutDigits;
            [WriteOnly] public NativeArray<float> OutStartTimes;
            [WriteOnly] public NativeArray<Vector4> OutColors;
            [WriteOnly] public NativeArray<float> OutWidths;

            public void Execute(int i)
            {
                var data = InputData[i];
                
                // 【修改】直接使用传入的 Scale，不再写死
                float scale = data.Scale;
                float widthRatio = 1.0f;
                
                // 特殊字符 (Digit 12) 依然保持加宽逻辑
                if (data.DigitIndex == 12) 
                { 
                    // scale 已经在前面赋值了 (0.6f)
                    widthRatio = 2.0f; 
                }

                float3 worldPos = data.StartPos + (math.mul(CameraRot, new float3(data.OffsetX, 0, 0)));
                
                OutMatrices[i] = Matrix4x4.TRS(worldPos, CameraRot, new Vector3(scale * widthRatio, scale, scale));
                
                OutDigits[i] = data.DigitIndex;
                OutStartTimes[i] = CurrentTime - data.StartTime;
                OutColors[i] = data.Color;
                OutWidths[i] = widthRatio;
            }
        }
        private void RenderBatches(FloatingTextConfig config, int count)
        {
            int batchIndex = 0;
            while (batchIndex < count)
            {
                // 1. 清理上一批次的属性
                _props.Clear();
        
                // 2. 计算当前批次的大小（实例化渲染单次上限为 1023）
                int drawCount = math.min(1023, count - batchIndex);

                // 3. 将 Job 计算好的 NativeArray 快速拷贝到托管数组 (Managed Arrays)
                // 这是 Graphics API 的硬性要求，必须转为托管数组才能传入
                NativeArray<Matrix4x4>.Copy(_nativeMatrices, batchIndex, _managedMatrices, 0, drawCount);
                NativeArray<float>.Copy(_nativeDigitIndices, batchIndex, _managedDigitIndices, 0, drawCount);
                NativeArray<float>.Copy(_nativeStartTimes, batchIndex, _managedStartTimes, 0, drawCount);
                NativeArray<Vector4>.Copy(_nativeColors, batchIndex, _managedColors, 0, drawCount);
                NativeArray<float>.Copy(_nativeWidths, batchIndex, _managedCharWidths, 0, drawCount);

                // 4. 将数组传递给 Shader 属性块 (MaterialPropertyBlock)
                // 这些名称必须与你的 FloatingText.shader 中的变量名严格对应
                _props.SetFloatArray(PropDigit, _managedDigitIndices);
                _props.SetFloatArray(PropStartTime, _managedStartTimes);
                _props.SetVectorArray(PropColor, _managedColors);
                _props.SetFloatArray(PropWidth, _managedCharWidths);

                // 5. 正式发起渲染指令
                // 这行代码执行后，Frame Debugger 才会出现绘制过程
                Graphics.DrawMeshInstanced(
                    config.QuadMesh, 
                    0, 
                    config.Material, 
                    _managedMatrices, 
                    drawCount, 
                    _props
                );

                // 6. 移动到下一个批次
                batchIndex += drawCount;
            }
        }
    }
}