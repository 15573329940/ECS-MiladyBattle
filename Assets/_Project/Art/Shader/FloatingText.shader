Shader "Custom/FloatingDamageText"
{
    Properties
    {
        _MainTex ("Font Texture Atlas", 2D) = "white" {}
        
        // 以下属性由 C# 代码通过 MaterialPropertyBlock 传入，这里仅作调试默认值
        [PerRendererData] _DigitIndex ("Digit Index", Float) = 0
        [PerRendererData] _StartTime ("Start Time", Float) = 0
        [PerRendererData] _BaseColor ("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off // 双面渲染，防止billboard旋转时背面剔除

        Pass
        {
            Name "FloatingText"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // 1. 保持原有的 Instancing 支持
            #pragma multi_compile_instancing
            
            // 【新增】2. 添加这一行来修复报错
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            // 【建议】DOTS 通常需要 Compute Shader 能力，建议加上这个（如果没有的话）
            #pragma target 4.5
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID // 1. 声明 Instancing ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float age : TEXCOORD1; // 传递生命周期给片元做透明度
                UNITY_VERTEX_INPUT_INSTANCE_ID // 2. 声明 Instancing ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // 3. 定义 Instancing 属性块 (必须与 C# 的 props 名称一致)
            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float, _DigitIndex)
                UNITY_DEFINE_INSTANCED_PROP(float, _StartTime)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                // 【新增】字符宽度 (默认为1，特效为2)
                UNITY_DEFINE_INSTANCED_PROP(float, _CharWidth)
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input); 
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float age = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _StartTime);
                output.age = age;

                // 【修改 1】提前获取 Index，用于判断是不是特效
                float index = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _DigitIndex);
                // 只要 index >= 11.5 (即 12)，isVFX 就是 1，否则是 0
                float isVFX = step(11.5, index); 

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                float3 positionWS = vertexInput.positionWS;

                // 【修改 2】差异化移动逻辑
                // 只有普通文字 (isVFX=0) 才加 age * 1.5 的向上偏移
                // 特效 (isVFX=1) 偏移量为 0，保持原地不动
                positionWS.y += (1.0 - isVFX) * age * 1.5;

                output.positionCS = TransformWorldToHClip(positionWS);

                // --- UV 逻辑 (保持不变) ---
                float width = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _CharWidth);
                float2 uv = input.uv;
                uv.x = (uv.x * width + index) / 14.0;
                output.uv = uv;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
                float index = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _DigitIndex);
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // 判断特效
                float isVFX = step(11.5, index);

                // 去白底 (保持之前的逻辑)
                float minChan = min(texColor.r, min(texColor.g, texColor.b));
                float contentMask = 1.0 - smoothstep(0.9, 0.98, minChan);

                // 颜色混合 (保持之前的逻辑)
                half3 finalRGB = lerp(baseColor.rgb, texColor.rgb, isVFX);

                half4 finalColor;
                finalColor.rgb = finalRGB;
                
                // 【修改 3】分离淡出逻辑
                // A. 文字：1.5秒 开始淡出，2.0秒 完全消失 (飘得久)
                float textFade = 1.0 - smoothstep(1.5, 2.0, input.age);
                
                // B. 特效：0.3秒 开始淡出，0.5秒 完全消失 (短促有力)
                float vfxFade = 1.0 - smoothstep(0.3, 0.5, input.age);
                
                // 根据 isVFX 选择使用哪一个淡出值
                float alphaFade = lerp(textFade, vfxFade, isVFX);

                // 最终 Alpha
                finalColor.a = contentMask * baseColor.a * alphaFade;

                clip(finalColor.a - 0.01);
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}