// 바람에 흔들리는 잔디 (URP / Forward)
//
// 왜 직접 짰나
//   URP Lit 로는 정점을 흔들 수 없다. Shader Graph 를 쓸 수도 있지만 .shadergraph
//   는 바이너리라 리뷰가 안 되고, 하는 일이 정점 오프셋 하나뿐이라 코드가 짧다.
//
// 흔드는 방식
//   정점 컬러 A 에 "밑둥 0 / 끝 1" 가중치가 구워져 있다(PlantRooftopGrass 참고).
//   그 가중치로 흔들림을 곱하므로 밑둥은 고정되고 끝만 눕는다. 밑둥까지 움직이면
//   포기 전체가 미끄러지는 것처럼 보인다.
//
//   위상은 월드 XZ 로 준다. 포기마다 위치가 다르니 자동으로 어긋나서, 바람이
//   벌판을 훑고 지나가는 것처럼 보인다. 같은 위상으로 흔들면 전부 동시에 움직여
//   젤리처럼 된다.

Shader "Yeouido63/GrassWind"
{
    Properties
    {
        _BaseMap        ("Base Map", 2D) = "white" {}
        _BaseColor      ("Tint", Color) = (1,1,1,1)
        _Smoothness     ("Smoothness", Range(0,1)) = 0.18
        _WindStrength   ("Wind Strength (m)", Range(0,1)) = 0.12
        _WindSpeed      ("Wind Speed", Range(0,5)) = 1.3
        _WindFreq       ("Wind Frequency", Range(0,2)) = 0.22
        _Cutoff         ("Alpha Cutoff", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off        // 쿼드 양면

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Smoothness, _WindStrength, _WindSpeed, _WindFreq, _Cutoff;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);

                // 밑둥 고정, 끝만 흔들림 (정점 컬러 A = 높이 가중치)
                float sway = v.color.a;
                float t = _Time.y * _WindSpeed;
                // 월드 XZ 로 위상을 어긋내 바람이 훑고 가게 한다
                float phase = (posWS.x + posWS.z) * _WindFreq;
                float2 off;
                off.x = sin(t + phase) + 0.35 * sin(t * 2.3 + phase * 1.7);
                off.y = cos(t * 0.8 + phase * 1.3);
                posWS.xz += off * sway * sway * _WindStrength;   // sway^2 = 끝으로 갈수록 급격히

                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;

                // 잔디 실루엣: 쿼드를 위로 갈수록 좁게 깎아 풀잎처럼 만든다.
                // 알파 텍스처를 따로 받지 않으려는 것 — Grass003 은 지면 타일이라
                // 알파가 없다.
                float2 c = i.uv - float2(0.5, 0.0);
                float taper = 1.0 - i.uv.y * 0.85;                  // 위로 갈수록 좁아짐
                float blade = step(abs(c.x), taper * 0.5);
                float noise = frac(sin(dot(floor(i.uv * 8.0), float2(12.9898, 78.233))) * 43758.5453);
                clip(blade * (noise * 0.4 + 0.6) - _Cutoff);

                float3 nWS = normalize(i.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(nWS, mainLight.direction));
                // 풀은 얇아서 빛이 통과한다. 반투과를 조금 섞어야 납작해 보이지 않는다.
                float wrap = saturate(ndl * 0.6 + 0.4);

                float3 sh = SampleSH(nWS);
                float3 col = tex.rgb * (mainLight.color * wrap * mainLight.shadowAttenuation + sh);

                // 밑둥을 살짝 어둡게 — 자기 그림자 대신. 그림자 캐스팅은 꺼 놨다.
                col *= lerp(0.55, 1.0, i.uv.y);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
