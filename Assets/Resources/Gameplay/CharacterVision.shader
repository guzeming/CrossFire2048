Shader "OperationBlacktide/TrainingVision"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Character sight"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_SightDistances);
            SAMPLER(sampler_SightDistances);
            float4 _SightOrigin, _SightForward, _SightSettings, _SightFeather;
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float2 offset = world.xz - _SightOrigin.xz;
                float distance = length(offset);
                float facing = dot(offset / max(.001, distance), _SightForward.xz);
                float cone = smoothstep(_SightSettings.z, _SightFeather.y, facing);
                float nearby = 1 - smoothstep(max(0, _SightSettings.y - _SightFeather.x), _SightSettings.y, distance);
                float range = 1 - smoothstep(_SightSettings.x - _SightFeather.x, _SightSettings.x, distance);
                float angle = atan2(offset.x, offset.y) / (2 * PI);
                // Point samples with a conservative neighbour minimum avoid leaking through thin walls.
                float step = 1 / _SightFeather.z;
                float index = floor(frac(angle) * _SightFeather.z);
                float a = SAMPLE_TEXTURE2D(_SightDistances, sampler_SightDistances, float2((index + .5) * step, .5)).r;
                float b = SAMPLE_TEXTURE2D(_SightDistances, sampler_SightDistances, float2((index + 1.5) * step, .5)).r;
                float limit = min(a, b);
                float obstruction = 1 - smoothstep(limit, limit + _SightFeather.x, distance);
                float sight = max(cone, nearby) * range * obstruction;
                return half4(.025, .035, .055, (1 - sight) * _SightSettings.w);
            }
            ENDHLSL
        }
    }
}
