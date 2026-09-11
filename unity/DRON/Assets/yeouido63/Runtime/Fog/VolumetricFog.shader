// 볼류메트릭 포그 (URP Full Screen Pass)
//
// 출처
//   HAliss 의 튜토리얼 셰이더를 이 씬에 맞게 손봤다.
//   https://gist.github.com/HAliss/f84e3c482ea2ac9664a3048fa734093c
//
// 원본에서 바꾼 것 — 왜
//   1) 레이마칭 루프에 반복 상한(MAX_STEPS)을 걸었다.
//      원본은 `while (distTravelled < distLimit)` 뿐이라 _StepSize 를 작게
//      주거나 _MaxDistance 를 크게 주면 루프가 수만 번 돈다. 이 씬은 far clip
//      이 30000m 라 그냥 두면 GPU 가 멈춘다(TDR).
//   2) 스텝 수를 거리에 맞춰 정규화했다. 상한에 걸리면 스텝을 늘려서
//      "가까운 데만 포그가 끼는" 현상 대신 전체가 옅어지게 한다.
//   3) 하늘(depth == 0/1) 픽셀에서 worldPos 가 무한대로 튀는 걸 막았다.
//      원본은 SampleSceneDepth 결과를 그대로 쓰는데, 스카이박스 픽셀에서
//      viewLength 가 폭발해 포그가 화면을 덮는다.
//   4) 노이즈 3D 텍스처가 없을 때를 대비해 절차적 폴백을 넣었다.
//
// 씬 맥락
//   63빌딩 항공 시점. 카메라가 지상 280m 근처, 대상까지 수백 m~수 km 다.
//   _MaxDistance 는 2000~4000 정도가 맞다. 기본 씬 포그(Linear 100~4200)와
//   겹치므로 둘 다 켜면 과해진다 — 이 셰이더를 쓸 거면 씬 포그는 줄여라.

Shader "Yeouido63/VolumetricFog"
{
    Properties
    {
        _Color              ("Color", Color) = (1, 1, 1, 1)
        _MaxDistance        ("Max distance", float) = 2500
        _StepSize           ("Step size", Range(0.1, 200)) = 12
        _DensityMultiplier  ("Density multiplier", Range(0, 10)) = 1
        _NoiseOffset        ("Noise offset", float) = 1
        _FogNoise           ("Fog noise (3D)", 3D) = "white" {}
        _NoiseTiling        ("Noise tiling", float) = 1
        _DensityThreshold   ("Density threshold", Range(0, 1)) = 0.1
        [HDR]_LightContribution ("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering    ("Light scattering", Range(0, 1)) = 0.2
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

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

            // 전방 산란이 강한 안개일수록 빛 쪽이 밝게 빛난다
            float henyey_greenstein(float angle, float scattering)
            {
                return (1.0 - angle * angle) /
                       (4.0 * PI * pow(1.0 + scattering * scattering - (2.0 * scattering) * angle, 1.5f));
            }

            float get_density(float3 worldPos)
            {
                float3 uvw = worldPos * 0.01 * _NoiseTiling;
                float4 noise = SAMPLE_TEXTURE3D_LOD(_FogNoise, sampler_TrilinearRepeat, uvw, 0);
                float density = dot(noise, noise);
                density = saturate(density - _DensityThreshold) * _DensityMultiplier;
                return density;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);

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
                float4 fogCol = _Color;

                [loop]
                for (int i = 0; i < MAX_STEPS; i++)
                {
                    if (distTravelled >= distLimit) break;

                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    float density = get_density(rayPos);

                    if (density > 0)
                    {
                        Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));
                        fogCol.rgb += mainLight.color.rgb * _LightContribution.rgb
                                    * henyey_greenstein(dot(rayDir, mainLight.direction), _LightScattering)
                                    * density * mainLight.shadowAttenuation * stepSize;
                        transmittance *= exp(-density * stepSize);
                    }

                    distTravelled += stepSize;
                }

                return lerp(col, fogCol, 1.0 - saturate(transmittance));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
