# 서드파티 코드

이 폴더는 외부 프로젝트를 그대로 가져온 것이다. **직접 수정하지 마라** —
업스트림을 갱신할 때 충돌한다.

이 씬에 맞춘 설정은 값마다 소유자가 다르다. 고칠 때 그 파일만 고쳐라.
한 값을 두 스크립트가 쓰면 나중에 돈 쪽이 이겨서 조용히 되돌아간다
(실제로 짙기·스텝·안개 사거리에서 겪었다).

| 값 | 소유자 |
|---|---|
| `densityMultiplier`, `numPrimarySteps`, 안개 `_MaxDistance` | `TuneAtmosphere.cs` |
| `globalSpeed`, `shapeSpeed`, `erosionSpeed`, 드리프트 | `SetupCloudHandle.cs` |
| 바람 방위 | `SyncWind.cs` |
| 피처 부착·최초 생성 | `SetupVolumetrics.cs` |

`CLOUD_Layer` 씬 오브젝트는 `[ExecuteAlways]` 라 **씬을 여는 것만으로
자기 값을 프로파일에 쓴다.** 프로파일만 고치면 다음에 씬을 열 때
되돌아가므로, 반드시 `Tools/Yeouido 63/구름 레이어 오브젝트 만들기` 를
돌려 컴포넌트까지 같이 맞춰야 한다.

> **예외 1건 — 로컬 패치가 들어가 있다.**
> `VolumetricClouds.shader` 의 깊이 샘플링을 한 곳 고쳤다. 설정으로는
> 못 고치는 업스트림 버그라 어쩔 수 없었다. 아래 "로컬 패치" 절을 보고,
> 업스트림을 갱신하면 반드시 다시 적용해라.

## UnityVolumetricCloudsURP

- 출처: https://github.com/jiaozi158/UnityVolumetricCloudsURP
- 라이선스: MIT (`LICENSE.md` 참고)
- 가져온 커밋: main (2026-09-11 시점)
- 원본 요구사항: Unity 2022.2 / URP 14 이상, Shader Model 3.5 이상

HDRP 의 볼류메트릭 구름을 URP 로 포팅한 것. 레포는 URP 14 기준이지만
`RecordRenderGraph` 경로가 이미 구현돼 있어 이 프로젝트의 URP 17.0.4
(Unity 6000.0.59f2) 에서 수정 없이 컴파일된다.

### 알려진 제약 (업스트림 README)

- 직교(Orthographic) 카메라 미지원 — 우리 카메라는 원근이라 무관
- Custom Cloud Map 오버라이드는 아직 WIP
- 구름 그림자를 켜면 메인 디렉셔널 라이트의 쿠키를 덮어쓴다
- 행성 반지름/중심을 바꾸려면 별도의 Physically Based Sky 패키지가 필요

### 이 씬에서의 설정

`Tools/Yeouido 63/볼류메트릭 셋업` 이 자동으로 붙인다.

- `PC_Renderer` 에 `VolumetricCloudsURP` 렌더러 피처
- `Yeouido63_Post.asset` 에 `Sky/Volumetric Clouds (URP)` 오버라이드
- `state = Enabled`, `densityMultiplier = 0.06`, `globalSpeed = 0.02`
- `bottomAltitude = 250`, `altitudeRange = 120`, `numPrimarySteps = 48`

밀도는 기본값(0.4)에서 계속 내려왔다. 0.22 -> 0.14 -> 0.06 이다.
카메라 포스트프로세싱이 꺼져 있어서 한동안 화면에 안 나왔고, 켜고 보니
하늘 전체에 옅은 베일처럼 덮여 스카이박스 구름 디테일까지 뭉갰다.

`numPrimarySteps` 는 기본 32 도 24 도 부족했다. 24 에서는 구름 가장자리가
스프레이 뿌린 것처럼 점 무늬로 부서진다 — 레이마칭 간격이 밀도 변화를
못 따라가서 생기는 표본 부족이다. 24/32/40/48 을 훑어 48 부터 깨끗했다.

시점은 항공이 아니라 옥상 눈높이다(카메라 y=1.75m). 예전 주석이
항공 시점 기준이었는데, 그건 지금 비활성인 `CAM_63_Air` 얘기다.

## 로컬 패치 — 얇은 지오메트리 위 구름 누수

**파일**: `VolumetricClouds.shader`, Pass 0 "Volumetric Clouds" 의 `frag`
(`_CameraDepthTexture` 를 읽는 곳, 검색어 `[여의도63 수정]`)

### 증상

안테나 탑(격자 구조) 위로 구름이 덮였다. 탑 꼭대기는 27.6m 이고 구름층은
250~370m 라 구름이 탑 앞에 올 방법이 물리적으로 없는데도, 탑 안쪽 픽셀의
4~5% 가 구름 색으로 오염됐다. 링 하나하나가 뿌옇게 먹혀 실루엣이 뭉갰다.

### 원인

이 패스는 `resolutionScale` 0.5 로 도는데 깊이는 point sampler 로 **한 점만**
읽는다. 저해상도 픽셀 하나가 화면 2x2 를 담당하므로, 그 한 점이 격자 링 사이
빈틈(= 하늘, far clip)에 걸리면 2x2 전체가 `isOccluded = false` 가 되고
`maxRayLength` 가 스카이박스 거리(200000)로 튄다. 그러면 구름이 링 위까지
칠해진다.

### 설정으로는 못 고친다 — 다 시험했다

| 시도 | 누수율 |
|---|---|
| 기준 (0.5, Bilinear, 시간누적 0.95, MSAA 4x) | 4.46% |
| resolutionScale 0.5 -> 1.0 | 4.24% |
| upscaleMode Bilinear -> Bilateral | 4.21% |
| temporalAccumulationFactor 0.95 -> 0 | 변화 없음 |
| MSAA 4x -> 1x | 2.53% |
| **셰이더 패치 적용** | **0.32%** |

해상도를 두 배로 올려도 거의 안 줄었다. 원인이 구름을 그리는 해상도가
아니라 깊이를 한 점만 읽는 것이기 때문이다.

### 패치 내용

담당 영역 2x2 의 깊이를 모두 읽어 **가장 가까운 값**을 쓴다. 하나라도 탑이면
그 픽셀은 가려진 것으로 친다. 보수적으로 가리는 쪽이라 구름이 새지 않는다.
역방향 Z 에서는 값이 클수록 가까우므로 `max` 가 최근접이다.

텍셀 크기는 `_CameraDepthTexture_TexelSize.xy` 를 쓴다.
`_ScreenParams` 는 지금 그리는 대상(절반 해상도) 기준이라 쓰면 안 된다.

처음엔 `GetDimensions` 로 직접 물어봤는데 두 가지가 틀렸다. `_TexelSize`
가 없다고 본 게 사실과 달랐고(`DeclareDepthTexture.hlsl` 이 텍스처 바로
다음 줄에서 선언한다), `_CameraDepthTexture` 는 `TEXTURE2D_X` 라 대부분의
API 에서 `Texture2DArray` 로 펼쳐져 2 인자 `GetDimensions` 오버로드가
아예 없다. 상수 버퍼 읽기라 비용도 이쪽이 싸다.

### 비용

깊이 샘플이 픽셀당 1 -> 5 회로 는다. 절반 해상도라 전체 화면의 1/4 픽셀에서만
돌고, 깊이 텍스처는 캐시에 잘 남는 편이라 체감 비용은 작다. 실측은 안 했다.

### 검증 방법

`Tools/Yeouido 63/구름 순서 판정` (`ProbeCloudOrder.cs`) 을 돌리면 누수율이
숫자로 나온다. 2% 아래면 정상이다.
