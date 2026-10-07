Shader "OperationBlacktide/Lobby/HangarGround"
{
    Properties
    {
        _BaseMap("Matching hangar plate",2D)="black"{}
        _Exposure("Plate exposure",Float)=1
        _ShadowStrength("Contact shadow strength",Range(0,1))=.8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "GroundForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_TexelSize;
            float _Exposure;
            float _ShadowStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float4 screen:TEXCOORD0; float3 positionWS:TEXCOORD1; float2 uv:TEXCOORD2; };
            Varyings Vert(Attributes i)
            {
                Varyings o; VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.screen=ComputeScreenPos(p.positionCS);o.uv=i.uv;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Match the full static plate pixel-for-pixel; only the live character's shadow
                // changes the image. Static crate highlights and reflections are already in it.
                float2 uv=i.screen.xy/i.screen.w;
                float aspect=_ScaledScreenParams.x/_ScaledScreenParams.y;
                float imageAspect=_BaseMap_TexelSize.z/_BaseMap_TexelSize.w;
                uv=(uv-.5)*float2(min(1.0,aspect/imageAspect),min(1.0,imageAspect/aspect))+.5;
                half3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_Exposure;
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                color*=lerp(1.0,light.shadowAttenuation,_ShadowStrength);
                return half4(color,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
