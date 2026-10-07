Shader "OperationBlacktide/Combat Additive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SwapUV ("Rotate tracer UV", Float) = 0
        _LaserBeam ("Continuous laser profile", Float) = 0
        _Intensity ("Effect brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _SwapUV;
                float _LaserBeam;
                float _Intensity;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = lerp(input.uv, input.uv.yx, _SwapUV);
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_LaserBeam > .5)
                {
                    // A narrow bright core and soft orange halo, constant along the entire beam.
                    half across = abs(input.uv.y * 2 - 1);
                    half core = 1 - smoothstep(0.0, 0.32, across);
                    half glow = 1 - smoothstep(0.0, 1.0, across);
                    return half4(input.color.rgb * (1 + core), input.color.a * (core + glow * .35));
                }
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                color.rgb *= _Intensity;
                return color;
            }
            ENDHLSL
        }
    }
}
