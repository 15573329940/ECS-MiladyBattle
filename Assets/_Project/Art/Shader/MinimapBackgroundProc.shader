Shader "Custom/MinimapBackgroundProc"
{
    Properties
    {
        // 背景底色 (深色泥土)
        [MainColor] _BaseColor ("Base Color", Color) = (0.1, 0.12, 0.15, 1)
        // 道路颜色 (浅灰色石板路)
        _LaneColor ("Lane Color", Color) = (0.4, 0.4, 0.45, 1)
        // 道路粗细
        _LaneThickness ("Lane Thickness", Range(0.01, 0.2)) = 0.08
        // 道路边缘柔化
        _LaneSmoothness ("Lane Smoothness", Range(0.001, 0.05)) = 0.01

        // 占位属性
        _MinimapRect ("Minimap Rect", Vector) = (0,0,100,100)
    }
    SubShader
    {
        // Queue=Overlay+90 确保在点(Overlay+100)之前绘制，但在普通UI之后
        Tags { "RenderType"="Overlay" "Queue"="Overlay+90" "RenderPipeline" = "UniversalPipeline" }
        
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 1. 声明支持 Instancing
            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                // 2. 输入结构体必须包含 Instance ID
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                // 3. 输出结构体必须包含 Instance ID
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LaneColor;
                float4 _MinimapRect;
                float _LaneThickness;
                float _LaneSmoothness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 4. 【至关重要】初始化 ID
                UNITY_SETUP_INSTANCE_ID(input);
                // 5. 【至关重要】传递 ID 到片元 (哪怕没用到也要传，防止报错)
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.uv = input.uv;

                // --- 屏幕映射逻辑 ---
                float2 screenPosPixels = _MinimapRect.xy + input.uv * _MinimapRect.zw;
                float2 clipSpacePos = (screenPosPixels / _ScreenParams.xy) * 2.0 - 1.0;

                // 如果你需要上下翻转，请取消注释下面这行
                 clipSpacePos.y *= -1.0; 

                // 6. Z 设为 0.5 (修正裁剪问题)
                output.positionCS = float4(clipSpacePos, 0.5, 1.0);
                
                return output;
            }

            // 距离计算辅助函数
            float distToLine(float2 pt, float2 p1, float2 p2) {
                float2 pa = pt - p1, ba = p2 - p1;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 7. 片元着色器也建议初始化 ID
                UNITY_SETUP_INSTANCE_ID(input);

                float2 uv = input.uv;
                
                // --- 画线逻辑 ---
                // 中路
                float distMid = distToLine(uv, float2(0,0), float2(1,1));
                // 上路 (左边缘+上边缘)
                float distTopEdge = min(uv.x, 1.0 - uv.y) - _LaneThickness * 0.5;
                // 下路 (下边缘+右边缘)
                float distBotEdge = min(uv.y, 1.0 - uv.x) - _LaneThickness * 0.5;

                // 取最小值
                float minDist = min(distMid, min(distTopEdge, distBotEdge));

                // 混合颜色
                float laneMask = 1.0 - smoothstep(_LaneThickness - _LaneSmoothness, _LaneThickness + _LaneSmoothness, minDist);
                half4 finalColor = lerp(_BaseColor, _LaneColor, laneMask);

                return finalColor;
            }
            ENDHLSL
        }
    }
}