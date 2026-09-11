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

        // 속도는 먼지 풍속에서 역산한 값이다.
        //
        //   위상항이 sin(t*_WindSpeed + d*_WindFreq) 이므로 등위상선은
        //   t*S + d*F = const, 즉 물결이 지면을 훑는 속도는 S/F [m/s] 다.
        //   먼지 대표 풍속 14.79 m/s 에 맞추려면 S = 14.79 * F.
        //   F=0.22 를 유지하면 S=3.25 다 (이전 1.3 은 5.9 m/s 라 먼지의 40%).
        //
        //   _WindSpeed 하나만 올리면 안 된다. 그건 "얼마나 빨리 떠느냐"고
        //   눈에 보이는 바람 속도는 S/F 라서, F 를 같이 건드리면 도로 어긋난다.
        _WindSpeed      ("Wind Speed", Range(0,5)) = 3.25
        _WindFreq       ("Wind Frequency", Range(0,2)) = 0.22
        // 바람 방향 (XZ, 정규화해서 씀). 먼지 파티클과 같은 값을 넣어야
        // 둘이 같은 바람으로 읽힌다 — SyncWind 가 맞춰 준다.
        _WindDir        ("Wind Direction (XZ)", Vector) = (0.7071, 0, 0.7071, 0)
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
                float4 _WindDir;
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

                // 바람 방향. 먼지와 같은 축으로 눕혀야 같은 바람으로 읽힌다.
                float2 wdir = normalize(_WindDir.xz + 1e-6);
                float2 wside = float2(-wdir.y, wdir.x);   // 직교축

                // 위상은 바람을 따라 흐르는 거리로 준다.
                //   예전엔 (x + z) 를 썼는데, 그건 항상 대각선 방향으로만
                //   물결이 흘러서 바람 방향을 바꿔도 결이 따라오지 않았다.
                //   바람 축에 투영하면 바람이 부는 쪽으로 결이 밀려간다.
                float phase = dot(posWS.xz, wdir) * _WindFreq;

                // 바람 축으로 눕고, 직교축으로는 살짝만 흔들린다.
                //   예전엔 x 와 z 를 독립적으로 흔들어 끝이 원을 그렸다.
                //   그래서 특정 방향으로 눕는 느낌이 없었다.
                //   풀은 바람 방향으로 눕고 그 자리에서 떤다.
                //
                //   along 을 0 중심이 아니라 양수 쪽으로 치우치게 만든다.
                //   sin 은 평균이 0 이라 앞뒤로 같은 만큼 흔들려서 바람이
                //   아니라 진동으로 보인다. 상수 항을 더해 늘 바람 방향으로
                //   기울어 있게 하고 그 위에서 흔들리게 한다.
                //
                //   계수는 계산해서 정했다. wave 는 두 사인의 합이라 범위가
                //   -1.348 ~ +1.102 로 비대칭이다. 0.55 + 0.45*wave 로 뒀더니
                //   최솟값이 -0.057 이라 한 순간 바람 반대로 눕었고, 그때
                //   오프셋 각도가 45 도에서 -65 도로 튀었다.
                //   0.62 + 0.38*wave 면 +0.108 ~ +1.039 라 늘 양수다.
                float wave  = sin(t + phase) + 0.35 * sin(t * 2.3 + phase * 1.7);
                float along = 0.62 + 0.38 * wave;         // 늘 눕되 세기가 출렁
                float side  = 0.18 * cos(t * 0.8 + phase * 1.3);

                float2 off = wdir * along + wside * side;
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
    // 폴백을 두면 안 된다. URP Lit 을 폴백으로 걸었더니 머티리얼 생성 때마다
    // "State comes from an incompatible keyword space" 경고와 함께 에디터가
    // 스택을 덤프했다 — 이 패스가 선언한 multi_compile 키워드 공간과 Lit 의
    // 것이 안 맞아서다. 이 셰이더는 URP 전용이라 폴백이 필요 없다.
    Fallback Off
}
