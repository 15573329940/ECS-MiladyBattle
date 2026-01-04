Shader "Custom/ECS_HealthBar_Fix"
{
    Properties
    {
        // ========================================================
        // 【核心修改】
        // 这里不要写 _FillAmount！不要写！
        // 只要这里不写，SRP Batcher 就不会检查它，也就不会报紫色错。
        // ========================================================
        
        _ColorHigh ("High HP Color", Color) = (0.2, 1.0, 0.2, 1.0)
        _ColorLow ("Low HP Color", Color) = (1.0, 0.2, 0.2, 1.0)
        _BgColor ("Background Color", Color) = (0.1, 0.1, 0.1, 0.8)
        _BorderColor ("Border Color", Color) = (1, 1, 1, 1)
        _BorderWidth ("Border Width", Range(0, 0.1)) = 0.02
        // _ScaleXY 也不要写
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent+100" 
            "RenderPipeline"="UniversalPipeline" 
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "HealthBar"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ========================================================
            // 【DOTS 数据通道】
            // 虽然 Properties 里没有，但这里必须定义。
            // C# 组件会根据变量名 "_FillAmount" 自动找到这里。
            // ========================================================
            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float, _FillAmount)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorHigh)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ColorLow)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BgColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BorderColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _BorderWidth)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ScaleXY) 
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // 读取 DOTS 里的 Scale 数据
                float4 scaleRaw = float4(1, 0.2, 1, 1); // 默认值
                #if defined(DOTS_INSTANCING_ON)
                   scaleRaw = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ScaleXY);
                   // 如果 C# 还没传值(0)，就用默认值防止消失
                   if (length(scaleRaw) < 0.001) scaleRaw = float4(1, 0.2, 1, 1);
                #endif

                float3 centerWorld = TransformObjectToWorld(float3(0, 0, 0));
                float3 viewPos = TransformWorldToView(centerWorld);
                viewPos += float3(IN.positionOS.xy * scaleRaw.xy, 0);

                OUT.positionCS = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // 读取 FillAmount
                float fill = 1.0;
                #if defined(DOTS_INSTANCING_ON)
                    fill = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _FillAmount);
                #endif

                float borderWidth = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BorderWidth);
                float4 colorLow = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ColorLow);
                float4 colorHigh = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ColorHigh);
                float4 bgColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BgColor);
                float4 borderColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BorderColor);

                float2 border = step(borderWidth, IN.uv) * step(IN.uv, 1.0 - borderWidth);
                float isContent = border.x * border.y;
                float fillMask = step(IN.uv.x, fill);
                float4 healthColor = lerp(colorLow, colorHigh, fill);
                float4 contentColor = lerp(bgColor, healthColor, fillMask);
                float4 finalColor = lerp(borderColor, contentColor, isContent);

                return finalColor;
            }
            ENDHLSL
        }
    }
}