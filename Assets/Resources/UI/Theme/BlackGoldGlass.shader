Shader "OperationBlacktide/UI/Black Gold Glass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 glass:TEXCOORD1; float4 state:TEXCOORD2; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 glass:TEXCOORD1; float4 state:TEXCOORD2; float4 screen:TEXCOORD3; float4 local:TEXCOORD4; };
            sampler2D _BlackGoldBackdrop;
            float _BlackGoldBackdropAvailable;
            float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.screen=ComputeScreenPos(o.vertex); o.color=v.color; o.uv=v.uv; o.glass=v.glass; o.state=v.state; return o;
            }
            float shape(float2 p,float2 size,float radius)
            { float2 q=abs(p-size*.5)-size*.5+radius; return length(max(q,0))+min(max(q.x,q.y),0)-radius; }
            fixed4 frag(v2f i):SV_Target
            {
                float2 size=i.glass.xy, p=i.uv*size;
                float kind=i.glass.z, hover=i.glass.w, pressed=i.state.x, disabled=i.state.y;
                float primary=1-step(.1,abs(kind-2));
                float panel=1-step(.1,kind);
                float input=1-step(.1,abs(kind-3));
                float tab=1-step(.1,abs(kind-4));
                float rail=1-step(.1,abs(kind-5));
                // Square plates; only the main action has a small machined corner.
                float d=shape(p,size,0);
                d=max(d,lerp(-10000,(p.x-p.y-size.x+8)*.7071,primary));
                float aa=max(fwidth(d),.7);
                float inside=1-smoothstep(-aa,aa,d);
                float2 screen=i.screen.xy/i.screen.w;
                float2 lens=(i.uv-.5)*.0015*hover;
                float2 stepUV=float2(2,2)/_ScreenParams.xy*(1+hover);
                float3 backdrop=tex2D(_BlackGoldBackdrop,screen+lens).rgb*.2;
                backdrop+=tex2D(_BlackGoldBackdrop,screen+lens+stepUV*float2(1,1)).rgb*.15;
                backdrop+=tex2D(_BlackGoldBackdrop,screen+lens+stepUV*float2(-1,1)).rgb*.15;
                backdrop+=tex2D(_BlackGoldBackdrop,screen+lens+stepUV*float2(1,-1)).rgb*.15;
                backdrop+=tex2D(_BlackGoldBackdrop,screen+lens-stepUV).rgb*.15;
                backdrop+=tex2D(_BlackGoldBackdrop,screen+lens+stepUV*float2(2,0)).rgb*.1;
                backdrop+=tex2D(_BlackGoldBackdrop,screen+lens-stepUV*float2(2,0)).rgb*.1;
                #ifndef UNITY_COLORSPACE_GAMMA
                    backdrop=LinearToGammaSpace(backdrop);
                #endif
                float luminance=dot(backdrop,float3(.299,.587,.114));
                backdrop=lerp(luminance.xxx,backdrop,.42);
                // Warm metal, amber transmission and an almost-white specular core.
                float3 gold=float3(1,.796,.369);
                float3 goldLight=float3(1,.961,.784);
                float3 amber=float3(1,.58,.12);
                float selected=i.state.z;
                float3 base=float3(.015,.020,.023)+backdrop*(.10+hover*.10)*_BlackGoldBackdropAvailable;
                // A broad reflection moves across the smoked lens as pointer/focus emphasis rises.
                float diagonal=i.uv.x+i.uv.y*.28;
                float reflection=exp2(-pow((diagonal-lerp(.24,.80,hover))*9,2));
                base+=reflection*hover*.045+hover*float3(.016,.018,.020);

                // The primary action has a lit gold interior beneath a dark translucent shell.
                // Keep the centre dark enough for the warm-white label, including while hovered.
                float depth=max(-d,0);
                float edge=1-smoothstep(.65,1.65,depth);
                float innerEdge=exp2(-abs(depth-3.2)*2);
                float edgeGlow=exp2(-depth/5.5);
                float pool=exp2(-pow((i.uv.x-.53)*2.1,2)-pow((i.uv.y+.12)*2.8,2));
                float floorLight=exp2(-i.uv.y*15)*(.25+.60*exp2(-pow((i.uv.x-.54)*3,2)));
                float facet=smoothstep(.87,.89,diagonal)*(1-smoothstep(.96,.98,diagonal));
                float3 lensBody=float3(.019,.022,.024)+backdrop*.09*_BlackGoldBackdropAvailable;
                lensBody+=gold*pool*(.28+hover*.18)+amber*floorLight*(.35+hover*.22);
                lensBody+=gold*edgeGlow*(.055+hover*.035)+gold*innerEdge*.09;
                lensBody+=reflection*(.055+hover*.075)*float3(.86,.88,.86);
                lensBody+=facet*float3(.040,.033,.018);
                lensBody+=pow(saturate(i.uv.y),12)*float3(.030,.034,.036);
                // Irregular bright catches make the rim read as metal instead of a flat outline.
                float catchLight=exp2(-pow((i.uv.x-.17)*6,2))*saturate(i.uv.y);
                catchLight+=exp2(-pow((i.uv.x-.80)*7,2))*saturate(1-i.uv.y);
                float rimStrength=.48+.25*i.uv.y+hover*.16;
                float3 rim=lerp(gold*rimStrength,goldLight,saturate(catchLight*(.85+hover*.25)));
                lensBody=lerp(lensBody,rim,edge);
                base=lerp(base,lensBody,primary);
                float bottom=1-smoothstep(.7,1.5,p.y);
                float top=1-smoothstep(.7,1.5,size.y-p.y);
                float left=1-smoothstep(.7,1.5,p.x);
                float right=1-smoothstep(.7,1.5,size.x-p.x);
                float corners=max((top+bottom)*(1-step(10,min(p.x,size.x-p.x))),
                    (left+right)*(1-step(10,min(p.y,size.y-p.y))));
                float lines=saturate(bottom*(1-rail)+corners*panel*.65);
                float marker=bottom*max(tab*max(selected,hover*.5),input*hover);
                marker=max(marker,left*(1-tab)*(1-panel)*(1-input)*(1-rail)*selected);
                base=lerp(base,float3(.43,.46,.46),lines*.26*(1-primary));
                float markerGlow=exp2(-p.y/3.5)*max(tab*max(selected,hover*.5),input*hover);
                base+=gold*markerGlow*.10*(1-primary)*(1-rail);
                base=lerp(base,lerp(gold,goldLight,hover*.45),marker*(1-primary));
                base*=1-pressed*.18;
                base=lerp(base,dot(base,float3(.299,.587,.114)).xxx*.65,disabled*.8);
                float opacity=lerp(.58,.92,hover);
                opacity=lerp(opacity,.90,panel);
                opacity=lerp(opacity,.97,primary);
                opacity=lerp(opacity,lerp(.18,.72,hover),input);
                opacity=lerp(opacity,hover*.55+selected*.14,tab);
                opacity=lerp(opacity,.94*pow(saturate(1-i.uv.x),.6),rail);
                opacity=max(opacity,lines*.26*(1-rail));
                opacity=max(opacity,marker);
                float alpha=inside*saturate(opacity)*(1-disabled*.22)*i.color.a;
                float3 result=base;
                #ifndef UNITY_COLORSPACE_GAMMA
                    result=GammaToLinearSpace(result);
                #endif
                #ifdef UNITY_UI_CLIP_RECT
                    alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(alpha-.001);
                #endif
                return fixed4(result,alpha);
            }
            ENDCG
        }
    }
}
