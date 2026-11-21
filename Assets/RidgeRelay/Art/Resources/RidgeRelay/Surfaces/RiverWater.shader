Shader "RidgeRelay/River Water"
{
    Properties
    {
        _BaseColor ("Shallow water", Color) = (0.10,0.65,0.65,1)
        _DeepColor ("Deep water", Color) = (0.035,0.30,0.41,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fog : TEXCOORD1;
            };
            half4 _BaseColor, _DeepColor;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 flow = input.positionWS.xz * 0.35 + float2(_Time.y * 0.18, -_Time.y * 0.25);
                float wave = sin(flow.x * 2.8 + sin(flow.y * 1.8)) *
                    sin(flow.y * 3.2 - sin(flow.x * 1.9));
                float broad = sin(flow.x * 0.38 + flow.y * 0.25) * 0.5 + 0.5;
                half3 color = lerp(_DeepColor.rgb, _BaseColor.rgb, broad * 0.6 + 0.25);
                float glint = pow(saturate(wave), 8);
                color += half3(0.25,0.38,0.35) * glint * 0.5;
                color += half3(0.035,0.13,0.13) * wave;
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
    }
}
