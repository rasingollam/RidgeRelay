Shader "RidgeRelay/Canyon Sky"
{
    Properties
    {
        _Horizon ("Horizon", Color) = (0.56,0.78,0.96,1)
        _Zenith ("Zenith", Color) = (0.10,0.42,0.78,1)
        _DaySky ("Upper hemisphere panorama", 2D) = "white" {}
        _UseTexture ("Use panorama", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            half4 _Horizon, _Zenith;
            TEXTURE2D(_DaySky); SAMPLER(sampler_DaySky);
            half _UseTexture;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float gradient = pow(saturate(direction.y), 0.55);
                half3 color = lerp(_Horizon.rgb, _Zenith.rgb, gradient);
                float2 uv=float2(atan2(direction.x,direction.z)/6.2831853+0.5,
                    0.30+asin(saturate(direction.y))*0.44563384);
                color=lerp(color,SAMPLE_TEXTURE2D(_DaySky,sampler_DaySky,uv).rgb,_UseTexture);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
