#ifndef URP_VOLUMETRIC_CLOUDS_DEFINES_HLSL
#define URP_VOLUMETRIC_CLOUDS_DEFINES_HLSL

CBUFFER_START(UnityPerMaterial)
float _Seed;
// [여의도63 수정] half -> float. 이 둘이 레이마칭 스텝 간격을 정한다.
//
//   VolumetricClouds.hlsl:61 에서
//       float stepS = min(totalDistance / _NumPrimarySteps, _MaxStepSize);
//   로 stepS 가 나오고, 이게 표본 간격이자 시작점 지터의 폭이다.
//
//   _MaxStepSize 는 보통 수백~수천 단위라 half 의 표현 간격이 벌어지는
//   구간에 들어간다. 거기서 값이 계단으로 떨어지면 스텝 간격이 프레임마다
//   튀고, 밀도 표본 위치가 통째로 흔들려 구름이 덜컥거린다.
//   위치 누적값을 float 로 올린 것(패치 2)과 같은 병이다.
//
//   _NumPrimarySteps 는 나눗셈의 분모라 같이 올린다. 48 정도는 half 로도
//   정확하지만, stepS 를 float 로 계산시키려면 분모도 float 여야 중간에서
//   정밀도가 깎이지 않는다.
float _NumPrimarySteps;
half _NumLightSteps;
float _MaxStepSize;
float _HighestCloudAltitude;
float _LowestCloudAltitude;
// [여의도63 수정] half -> float. 구름 흐름 오프셋이다.
//
//   half 는 값이 커질수록 표현 간격이 벌어진다. 1024 근처에서는 간격이
//   1.0 이나 된다. 이 씬의 드리프트는 아주 느려서(0.025) 한 프레임
//   이동량이 그 간격보다 훨씬 작다. 그러면 오프셋이 한동안 그대로
//   있다가 어느 순간 한 칸 튀고, 구름이 미끄러지는 대신 계단으로
//   움직인다 — "움직이면서 드르륵" 의 직접 원인이다.
//
//   느린 속도일수록 심해진다. 빠르면 매 프레임 표현 간격을 넘어가서
//   오히려 덜 티가 난다. 속도를 낮춘 뒤에 도드라진 이유가 이것이다.
//
//   CBUFFER 안에서 half 는 어차피 32 비트 슬롯을 차지하므로
//   레이아웃과 C# 쪽 대입은 영향 없다.
float4 _ShapeNoiseOffset;
float _VerticalShapeNoiseOffset;
half4 _WindDirection;
// [여의도63 수정] half -> float. 바람이 구름 위치를 미는 누적 변위다.
// _ShapeNoiseOffset 과 같은 이유로 half 면 이동이 계단이 된다.
// (VolumetricCloudsUtilities.hlsl:338, :345 에서 위치에 더해진다)
float4 _WindVector;
// [여의도63 수정] half -> float. 위와 같은 누적 변위다.
float _VerticalShapeWindDisplacement;
float _VerticalErosionWindDisplacement;
half _MediumWindSpeed;
half _SmallWindSpeed;
half _AltitudeDistortion;
half _DensityMultiplier;
half _PowderEffectIntensity;
half _ShapeScale;
half _ShapeFactor;
half _ErosionScale;
half _ErosionFactor;
half _ErosionOcclusion;
half _MicroErosionScale;
half _MicroErosionFactor;
half _FadeInStart;
half _FadeInDistance;
half _MultiScattering;
half4 _ScatteringTint;
half _AmbientProbeDimmer;
half _SunLightDimmer;
float _EarthRadius;
// [여의도63 수정] half -> float. 재투영 블렌드 가중치다.
// 디노이즈 패스에서 velocity(float)와 같이 쓰이는데 여기서 half 로
// 떨어지면 그 정밀도가 도로 깎인다. 레이아웃은 안 변한다.
float _AccumulationFactor;
half _NormalizationFactor;
half _CloudNearPlane;
CBUFFER_END

// Ambient Probe (unity_SH)
half4 clouds_SHAr;
half4 clouds_SHAg;
half4 clouds_SHAb;
half4 clouds_SHBr;
half4 clouds_SHBg;
half4 clouds_SHBb;
half4 clouds_SHC;

half _ImprovedTransmittanceBlend;
float _PostExposure; // Exposure from the ColorAdjustments override
half3 _SunColor;

#ifndef URP_PHYSICALLY_BASED_SKY_DEFINES_INCLUDED
float4 _PlanetCenterRadius;
#endif

#endif