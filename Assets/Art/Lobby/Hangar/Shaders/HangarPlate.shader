Shader "OperationBlacktide/Lobby/HangarPlate"
{
    Properties { _BaseMap("Hangar plate", 2D) = "black" {} _Exposure("Plate exposure", Float) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Background" }
        Pass
        {
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_TexelSize;
            float _Exposure;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                o.screen=ComputeScreenPos(o.positionCS); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv=i.screen.xy/i.screen.w;
                float aspect=_ScaledScreenParams.x/_ScaledScreenParams.y;
                float imageAspect=_BaseMap_TexelSize.z/_BaseMap_TexelSize.w;
                float2 crop=float2(min(1.0,aspect/imageAspect),min(1.0,imageAspect/aspect));
                uv=(uv-.5)*crop+.5;
                return half4(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_Exposure,1);
            }
            ENDHLSL
        }
    }
}
