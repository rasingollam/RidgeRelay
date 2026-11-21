Shader "RidgeRelay/Shield Bubble"
{
    Properties { [MainColor] _BaseColor ("Shell", Color) = (0.15,1.05,0.72,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };
            half4 _BaseColor;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float rim = pow(1 - saturate(dot(normalize(input.normalWS),
                    GetWorldSpaceNormalizeViewDir(input.positionWS))), 2);
                float2 cell = input.uv * float2(14,7);
                cell.x += floor(cell.y) * 0.5;
                float2 edge = abs(frac(cell) - 0.5);
                float seam = smoothstep(0.43,0.49,max(edge.x,edge.y * 0.87 + edge.x * 0.45));
                half3 color = _BaseColor.rgb * (0.9 + rim * 1.2) + seam * half3(0.12,0.32,0.22);
                return half4(color, 0.04 + rim * 0.32 + seam * 0.14);
            }
            ENDHLSL
        }
    }
}
