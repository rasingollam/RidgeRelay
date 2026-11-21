Shader "RidgeRelay/Power Glow"
{
    Properties { [MainColor] _BaseColor ("Glow", Color) = (1.8,1.8,1.8,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            half4 _BaseColor;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float edge = pow(saturate(1 - abs(input.uv.y - 0.5) * 2),1.7);
                float ends = smoothstep(0,0.05,input.uv.x) * smoothstep(0,0.05,1-input.uv.x);
                float core=pow(edge,6);
                half3 color=lerp(input.color.rgb*_BaseColor.rgb,half3(3,3,3),core*0.45);
                return half4(color, input.color.a * (edge*0.4+core*0.6) * ends);
            }
            ENDHLSL
        }
    }
}
