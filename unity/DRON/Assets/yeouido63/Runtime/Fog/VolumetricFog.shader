// 볼류메트릭 포그 (URP Full Screen Pass)
//
// 출처
//   HAliss 의 튜토리얼 셰이더를 이 씬에 맞게 손봤다.
//   https://gist.github.com/HAliss/f84e3c482ea2ac9664a3048fa734093c
//
// 원본에서 바꾼 것 — 왜
//   1) 레이마칭 루프에 반복 상한(MAX_STEPS)을 걸었다.
//      원본은 `while (distTravelled < distLimit)` 뿐이라 _StepSize 를 작게
//      주거나 _MaxDistance 를 크게 주면 루프가 수만 번 돈다.
//      주의: MAX_STEPS 는 실제로 걸리는 상한이 아니다. 154행의
//      max(_StepSize, distLimit/MAX_STEPS) 가 스텝 크기의 하한을 잡아 주므로
//      반복 수를 실제로 정하는 건 _StepSize 다. 지금 값(22, 거리 900)에서
//      반복은 41 회고 128 에는 닿지 않는다. 128 은 _StepSize 를 아주 작게
//      줬을 때만 의미가 있는 안전장치다.
//   2) 스텝 수를 거리에 맞춰 정규화했다. 상한에 걸리면 스텝을 늘려서
//      "가까운 데만 포그가 끼는" 현상 대신 전체가 옅어지게 한다.
//   3) 하늘(depth == 0/1) 픽셀에서 worldPos 가 무한대로 튀는 걸 막았다.
//      원본은 SampleSceneDepth 결과를 그대로 쓰는데, 스카이박스 픽셀에서
//      viewLength 가 폭발해 포그가 화면을 덮는다.
//   4) 스테레오 매크로를 맞췄다 (_BlitTexture 는 TEXTURE2D_X 다).
//
//   주의: 노이즈 3D 텍스처는 폴백이 없다. 비면 Unity 기본 "white" 가
//   물려 밀도가 상수가 되고 화면이 단색으로 덮인다. _FogNoise 가
//   비어 있지 않은지 머티리얼에서 확인해라.
//
// 씬 맥락
//   이 주석은 원래 항공 시점(카메라 고도 280m, far clip 30000) 기준으로
//   쓰였는데, 그건 지금 비활성인 CAM_63_Air 얘기다. 실제로 쓰는 카메라는
//   옥상 눈높이의 main_came 이고 far clip 이 1000, 고도가 1.75m 다.
//
//   그래서 _MaxDistance 는 far clip 을 넘길 이유가 없다. 1200 으로 두면
//   씬에 아무것도 없는 200m 구간을 스텝 9 개나 더 도는 낭비다.
//
//   씬 포그(Linear)와 겹치면 과해지니 둘 다 켜지 마라. 현재 씬 포그는
//   start 1800 이라 far clip 1000 안에서는 한 번도 안 걸린다 — 꺼도 화면이
//   안 변하고 셰이더 배리언트만 줄어든다.

Shader "Yeouido63/VolumetricFog"
{
    Properties
    {
        _Color              ("Color", Color) = (1, 1, 1, 1)
        _MaxDistance        ("Max distance", float) = 900
        _StepSize           ("Step size", Range(0.1, 200)) = 12
        _DensityMultiplier  ("Density multiplier", Range(0, 10)) = 1
        _NoiseOffset        ("Noise offset", float) = 1
        _FogNoise           ("Fog noise (3D)", 3D) = "white" {}
        _NoiseTiling        ("Noise tiling", float) = 1
        _DensityThreshold   ("Density threshold", Range(0, 1)) = 0.1
        [HDR]_LightContribution ("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering    ("Light scattering", Range(0, 1)) = 0.2

        // 황사용 — 안개를 바람에 흘리고 고도에 따라 옅게 만든다
        _WindDir            ("Wind direction (XZ)", Vector) = (1, 0, 0.35, 0)
        _WindSpeed          ("Wind speed (m/s)", Range(0, 30)) = 6
        // 상한 0.02 는 낮았다. 구름 고도(250m)의 아지랑이를 죽이려면 0.035 가
        // 필요한데, SetFloat 은 Range 를 무시해서 코드로는 들어가지만
        // 인스펙터 슬라이더를 한 번 건드리면 0.02 로 스냅돼 되돌아간다.
        // 값이 왜 혼자 바뀌었는지 찾기 어려운 종류의 함정이라 상한을 올린다.
        _HeightFalloff      ("Height falloff (1/m)", Range(0, 0.05)) = 0.0025
        _HeightBase         ("Height base (world Y)", float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off

        Pass
        {
            Name "VolumetricFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            // _MAIN_LIGHT_SHADOWS_SCREEN 은 뺐다.
            //
            //   그 배리언트에서는 TransformWorldToShadowCoord 가 섀도맵
            //   좌표가 아니라 화면 좌표를 돌려주고, 그림자를 스크린스페이스
            //   버퍼에서 읽는다. 그 버퍼는 불투명 표면 깊이 기준으로 만든
            //   것이라, 레이마칭 중간 지점을 넣으면 그 지점이 아니라
            //   "그 픽셀의 표면" 그림자가 나온다. 안개 전 구간이 같은 값을
            //   받아 볼류메트릭 그림자가 통째로 뭉개진다.
            //
            //   에러가 안 나고 그림만 틀리는 종류라 제일 늦게 발견된다.
            //   빼면 URP 가 일반 섀도맵 경로로 폴백한다.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // 레이마칭 반복 상한. 이게 없으면 파라미터 조합에 따라 드라이버가 죽는다.
            #define MAX_STEPS 128

            float4 _Color;
            float  _MaxDistance;
            float  _DensityMultiplier;
            float  _StepSize;
            float  _NoiseOffset;
            TEXTURE3D(_FogNoise);
            float  _DensityThreshold;
            float  _NoiseTiling;
            float4 _LightContribution;
            float  _LightScattering;
            float4 _WindDir;
            float  _WindSpeed;
            float  _HeightFalloff;
            float  _HeightBase;

            // 전방 산란이 강한 안개일수록 빛 쪽이 밝게 빛난다
            float henyey_greenstein(float angle, float scattering)
            {
                // scattering=1 이고 빛을 정면으로 보면 밑이 1+1-2 = 0 이 되어
                // pow(0,1.5)=0, 즉 0 으로 나눈다. _LightScattering 슬라이더가
                // 1 까지 열려 있어서 도달 가능한 값이다 — 현재 0.35 라 여유가
                // 있지만 끝까지 밀면 화면에 inf 가 터진다. 하한을 잡아 둔다.
                float denom = 1.0 + scattering * scattering - (2.0 * scattering) * angle;
                return (1.0 - angle * angle) / (4.0 * PI * pow(max(1e-4, denom), 1.5f));
            }

            float get_density(float3 worldPos)
            {
                // 바람에 실려 흐르게 한다. 노이즈를 월드에 고정해 두면 카메라가
                // 멈췄을 때 완전히 정지한 판때기로 보인다 — 황사는 흘러야 한다.
                float3 wind = float3(_WindDir.x, 0, _WindDir.z) * (_WindSpeed * _Time.y);
                float3 uvw = (worldPos - wind) * 0.01 * _NoiseTiling;

                float4 noise = SAMPLE_TEXTURE3D_LOD(_FogNoise, sampler_TrilinearRepeat, uvw, 0);
                float density = dot(noise, noise);

                // 두 번째 옥타브를 반대로 흘려 결이 뭉개지지 않게 한다.
                // 단일 옥타브만 쓰면 같은 무늬가 평행이동만 해서 눈에 띈다.
                float3 uvw2 = (worldPos + wind * 0.45) * 0.031 * _NoiseTiling;
                float4 n2 = SAMPLE_TEXTURE3D_LOD(_FogNoise, sampler_TrilinearRepeat, uvw2, 0);
                density = density * 0.72 + dot(n2, n2) * 0.28;

                // 고도 감쇠 — 황사는 지표에 깔리고 위로 갈수록 옅다.
                // 이게 없으면 하늘까지 균일하게 누레져서 색필터처럼 보인다.
                density *= exp(-max(0.0, worldPos.y - _HeightBase) * _HeightFalloff);

                density = saturate(density - _DensityThreshold) * _DensityMultiplier;

                // 미터 단위 소광계수로 환산한다.
                //
                //   투과율은 exp(-density * 거리[m]) 로 계산된다. 그래서 density
                //   는 "미터당" 소광계수다. 여기 들어오는 값이 0.14 만 돼도
                //   7m 마다 빛이 1/e 로 줄어든다는 뜻이라, 2600m 를 행군하면
                //   투과율이 0 이 된다 — 실제로 화면이 베이지 단색으로 덮였다.
                //
                //   현실의 황사는 가시거리가 1~2km 수준이고 그때 소광계수는
                //   대략 0.002~0.004 /m 다. 슬라이더를 0~1 범위로 쓰면서 이
                //   영역에 닿게 하려면 1/400 쯤으로 눌러야 한다.
                //
                //   슬라이더 의미를 바꾸는 대신 여기서 환산하는 이유 —
                //   _DensityMultiplier 를 0.0008 같은 값으로 두면 인스펙터에서
                //   사실상 조절이 불가능해진다.
                const float METERS_PER_UNIT = 1.0 / 400.0;
                return density * METERS_PER_UNIT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // _X 매크로로 읽어야 한다. Blit.hlsl 이 _BlitTexture 를
                // TEXTURE2D_X 로 선언하는데, 대부분의 그래픽스 API 에서
                // 그건 Texture2DArray 로 펼쳐진다. 비-X 매크로로 읽으면
                // 단일 뷰에서는 배열 인덱스가 0 으로 접혀 우연히 돌지만
                // 스테레오에서는 깨진다. 눈 인덱스도 같이 세워 준다.
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, IN.texcoord);

                float depth = SampleSceneDepth(IN.texcoord);

                // 하늘 픽셀은 깊이가 far 다. 그대로 쓰면 viewLength 가 폭발한다.
                // 리버스드-Z 여부에 관계없이 "깊이가 없다"를 잡아낸다.
                #if UNITY_REVERSED_Z
                    bool isSky = (depth <= 1e-6);
                #else
                    bool isSky = (depth >= 1.0 - 1e-6);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);
                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float viewLength = length(viewDir);
                float3 rayDir = viewLength > 1e-5 ? viewDir / viewLength : float3(0, 0, 1);

                // 하늘이면 표면이 없으니 최대 거리까지만 행군한다
                float distLimit = isSky ? _MaxDistance : min(viewLength, _MaxDistance);

                // 스텝 수를 상한에 맞춰 조절. 밴딩 대신 전체가 옅어지는 쪽이 낫다.
                float stepSize = max(_StepSize, distLimit / MAX_STEPS);

                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.zw;
                // 시작점을 픽셀마다 흩어 밴딩을 깬다
                float distTravelled = InterleavedGradientNoise(
                    pixelCoords, (int)(_Time.y / max(HALF_EPS, unity_DeltaTime.x))) * _NoiseOffset;

                float transmittance = 1;

                // 산란광을 따로 모은다.
                //
                //   원래는 fogCol.rgb 에 그대로 더하면서 stepSize 까지 곱했다.
                //   stepSize 가 18m 라 스텝마다 18 배씩 쌓여서, 스텝이 많아질수록
                //   화면이 하얗게 날아갔다(_LightContribution 이 1 근처면 즉시 폭발).
                //   그 식은 _LightContribution 이 0.01 수준일 때만 성립한다.
                //
                //   여기서는 각 스텝의 기여를 "그 지점까지의 투과율 x 그 스텝에서
                //   흡수된 양" 으로 가중해 더한다. 흡수량의 총합이 1 을 넘지
                //   않으므로 스텝 수나 크기를 바꿔도 밝기가 흔들리지 않는다.
                float3 scattered = 0;

                [loop]
                for (int i = 0; i < MAX_STEPS; i++)
                {
                    if (distTravelled >= distLimit) break;

                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    float density = get_density(rayPos);

                    if (density > 0)
                    {
                        Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));

                        // 이 스텝에서 빠져나간 빛의 비율 (0~1)
                        float stepTrans = exp(-density * stepSize);
                        float absorbed  = transmittance * (1.0 - stepTrans);

                        float phase = henyey_greenstein(dot(rayDir, mainLight.direction), _LightScattering);

                        scattered += mainLight.color.rgb * _LightContribution.rgb
                                   * phase * mainLight.shadowAttenuation * absorbed;

                        transmittance *= stepTrans;
                    }

                    distTravelled += stepSize;

                    // 거의 다 막혔으면 더 행군해도 화면에 변화가 없다
                    if (transmittance < 0.01) break;
                }

                float fogAmount = 1.0 - saturate(transmittance);

                // 안개 자체 색 + 그 안에서 산란된 햇빛
                float3 fogRGB = _Color.rgb + scattered;

                return float4(lerp(col.rgb, fogRGB, fogAmount), col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
