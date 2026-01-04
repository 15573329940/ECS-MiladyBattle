Shader "Custom/MinimapScreenSpace"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        // 这些属性将由 C# 代码实时更新，这里只是占位
        _MapCenter ("Map Center (XZ)", Vector) = (0,0,0,0)
        _MapSize ("Map Size", Float) = 100
        _MinimapRect ("Minimap Rect (X,Y,W,H)", Vector) = (0,0,100,100)
    }
    SubShader
    {
        Tags { "RenderType"="Overlay" "Queue"="Overlay+100" "RenderPipeline" = "UniversalPipeline" }
        // 关键设置：关闭深度测试，永远画在最上层
        ZTest Always
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MapCenter;
                float4 _MinimapRect; // xy = 屏幕位置, zw = 宽高
                float _MapSize;
                
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color) 
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // 使用标准宏获取实例的旋转平移矩阵
                float4x4 instanceMatrix = UNITY_MATRIX_M;
                // 提取第 4 列：x=m03, y=m13, z=m23 (即世界坐标)
                float3 worldPos = float3(instanceMatrix[0].w, instanceMatrix[1].w, instanceMatrix[2].w);

                // 1. 归一化 UV 计算 (确保 _MapCenter 在 C# 中也被更新了)
                float2 uv = (worldPos.xz - _MapCenter.xz) / _MapSize + 0.5;
                //uv.y = 1.0 - uv.y;
                // 2. 映射到 UI 屏幕像素
                float2 screenPosPixels = _MinimapRect.xy + uv * _MinimapRect.zw;

                // 3. 转到 Clip Space (-1 ~ 1)
                float2 clipSpacePos = (screenPosPixels / _ScreenParams.xy) * 2.0 - 1.0;
                clipSpacePos.y *= -1.0;
                // 4. 计算缩放后的顶点偏移
                float scale = length(float3(instanceMatrix[0].x, instanceMatrix[1].x, instanceMatrix[2].x));
                float2 localOffset = input.positionOS.xy * scale;
                localOffset.x /= _ScreenParams.x * 0.5; 
                localOffset.y /= _ScreenParams.y * 0.5;

                // 【修改点】将 Z 设为 0.5，确保它在所有剪裁平面的安全范围内 [cite: 16]
                output.positionCS = float4(clipSpacePos + localOffset, 0.5, 1.0);
                output.color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return input.color;
            }
            ENDHLSL
        }
    }
}