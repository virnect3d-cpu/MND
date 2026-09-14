# MND

여의도 63빌딩 일대를 URP 로 재현한 Unity 프로젝트다.
실측 도시 모델 위에 볼류메트릭 구름·바람·먼지·물을 얹어 옥상 시점의 대기를 만든다.

## 구성

| 경로 | 내용 |
|---|---|
| `unity/DRON/` | Unity 프로젝트 본체 |
| `3D/` | 원본 3D 소스 자리 (현재 비어 있음) |

Unity 프로젝트 안은 이렇게 나뉜다.

| 경로 | 내용 |
|---|---|
| `Assets/yeouido63/` | 도시 에셋 + 씬 셋업 툴. 임베디드 UPM 패키지 (`com.virnect.yeouido63`) |
| `Assets/VolumetricClouds/` | 구름 렌더러. 서드파티 + 로컬 패치 3 건 |
| `Assets/Settings/` | URP 렌더러·파이프라인 에셋, 볼륨 프로파일 |
| `Assets/Scenes/` | `SampleScene`(템플릿 기본) |

메인 씬은 `Assets/yeouido63/Scenes/Yeouido63.unity` 다.

## 요구 사항

- Unity **6000.0.59f2**
- URP **17.0.4**

## 시작하기

1. Unity Hub 에서 `unity/DRON` 을 연다.
2. `Assets/yeouido63/Scenes/Yeouido63.unity` 를 연다.
3. 씬이 비어 보이면 메뉴 **`Tools ▸ Yeouido 63 ▸ 씬 셋업`** 을 한 번 돌린다.
   재질·스카이박스·태양·카메라·포스트까지 잡고 씬을 저장한다.

카메라는 옥상 눈높이(y = 1.75m)에 선다. 항공 시점 카메라(`CAM_63_Air`)도
있지만 지금은 꺼져 있다.

## 씬 셋업 도구

`Tools ▸ Yeouido 63 ▸` 아래에 개별 셋업·진단 메뉴가 있다.
구름·먼지·잔디·바람·반사 프로브처럼 층별로 나뉘어 있어 필요한 것만 다시 돌릴 수 있다.

진단 쪽(`구름 순서 판정`, `구름 떨림 측정` 등)은 숫자를 찍어주는 도구다.
눈으로 "나아진 것 같다" 로 끝내지 않기 위한 것이라, 렌더링을 건드린 뒤에는
같은 도구로 다시 재서 비교하면 된다.

### 값의 소유자가 나뉘어 있다

같은 값을 두 스크립트가 쓰면 나중에 돈 쪽이 이겨서 조용히 되돌아간다.
실제로 구름 짙기·스텝 수·안개 사거리에서 겪은 문제라, 값마다 주인을 정해 뒀다.

| 값 | 소유자 |
|---|---|
| 구름 짙기, 레이 스텝 수, 안개 사거리 | `TuneAtmosphere.cs` |
| 구름 속도, 드리프트 | `SetupCloudHandle.cs` |
| 바람 방위 | `SyncWind.cs` |
| 렌더러 피처 부착·최초 생성 | `SetupVolumetrics.cs` |

`CLOUD_Layer` 오브젝트는 `[ExecuteAlways]` 라 **씬을 여는 것만으로 자기 값을
프로파일에 쓴다.** 프로파일만 고치면 다음에 씬을 열 때 되돌아가므로,
반드시 대응하는 셋업 메뉴를 같이 돌려야 한다.

## 볼류메트릭 구름

HDRP 의 볼류메트릭 구름을 URP 로 포팅한
[UnityVolumetricCloudsURP](https://github.com/jiaozi158/UnityVolumetricCloudsURP) (MIT) 을 쓴다.

업스트림을 그대로 두지 않고 **로컬 패치 3 건**이 들어가 있다.
설정으로는 못 고치는 문제들이라 셰이더를 직접 건드렸다.

1. 얇은 지오메트리 위 구름 누수 — 저해상도 깊이를 한 점만 읽어서 생긴 문제
2. 구름이 계단으로 움직임 — 위치 누적값이 `half` 라 느린 속도에서 드러남
3. 레이 시작점 지터 복구 — 포팅 과정에서 `* stepS` 가 누락돼 지터가 무력화돼 있었음

증상·원인·측정값과 **업스트림 갱신 시 다시 적용하는 법**은
`Assets/VolumetricClouds/THIRD_PARTY.md` 에 정리돼 있다.
구름을 건드리기 전에 그 문서를 먼저 읽어라.

## 에셋 문서

도시 에셋 자체(폴더 구성, 모델 제작 근거, 태양 각도, UV 단위)는
`Assets/yeouido63/README.md` 와 `Documentation~/model-notes.md` 를 본다.

## Git LFS

바이너리 에셋(FBX·텍스처·HDRI)은 Git LFS 로 관리한다.
클론하기 전에 LFS 가 설치돼 있어야 한다.

```bash
git lfs install
```

LFS 도입 이전 시점은 `prelfs/main`, `prelfs/feat` 태그로 남겨 뒀다.
