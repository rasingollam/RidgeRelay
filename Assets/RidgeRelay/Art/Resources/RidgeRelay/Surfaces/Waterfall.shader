Shader "RidgeRelay/Waterfall"
{
    Properties { _BaseColor ("Water", Color) = (0.38,0.85,0.85,0.9) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            half4 _BaseColor;
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),
                    lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float vertical = input.uv.y + _Time.y * 0.65;
                float streams = noise(float2(input.uv.x*32,vertical*8));
                float droplets = noise(float2(input.uv.x*89,vertical*47));
                float foam = smoothstep(0.22,0.72,streams*0.65+droplets*0.35);
                float edgeNoise=noise(float2(input.uv.y*12,_Time.y*0.7))*0.05;
                float edges = smoothstep(edgeNoise,0.08+edgeNoise,input.uv.x) * smoothstep(edgeNoise,0.08+edgeNoise,1-input.uv.x);
                half3 color = lerp(_BaseColor.rgb, half3(0.91,0.99,0.97), foam);
                return half4(MixFog(color,input.fog), edges * (0.60 + foam * 0.37));
            }
            ENDHLSL
        }
    }
}
