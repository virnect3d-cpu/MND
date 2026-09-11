# 서드파티 코드

이 폴더는 외부 프로젝트를 그대로 가져온 것이다. **직접 수정하지 마라** —
업스트림을 갱신할 때 충돌한다. 이 씬에 맞춘 설정은 전부
`Assets/yeouido63/Editor/SetupVolumetrics.cs` 에 있다.

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
- `state = Enabled`, `densityMultiplier = 0.22`, `globalSpeed = 2.5`

밀도를 기본값(0.4)이 아니라 0.22 로 낮춘 이유 — 이 씬은 항공 시점이라
기본 밀도면 구름이 화면을 덮는다.

구름 바닥 고도는 기본값 1200m 를 그대로 뒀다. 63빌딩이 250m 라 구름이
한참 위에 뜨는데, 실제 적운 운저가 그 정도라 맞는 그림이다.
