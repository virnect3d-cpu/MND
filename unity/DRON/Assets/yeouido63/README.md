# Yeouido 63 City (`com.virnect.yeouido63`)

여의도 63빌딩 일대 실측 도시 에셋. Blender 에서 구운 걸 URP 로 옮긴 거예요.
프로젝트 `Assets/yeouido63/` 밑에 임베디드로 들어가 있어서 읽기 전용이 아니라
씬·재질을 그대로 저장할 수 있어요.

## 폴더

| 경로 | 내용 |
|---|---|
| `Editor/` | 씬 셋업 툴 (`Tools ▸ Yeouido 63 ▸ 씬 셋업`) + 리로드 자동 실행 |
| `Runtime/Models/` | `yeouido_63.fbx` (5 오브젝트 / 203,931 면) |
| `Runtime/Materials/` | `M_*.mat` 14 종 — 셋업 툴이 만들고 덮어씀 |
| `Runtime/Textures/` | 구운 지형·바다·63빌딩 텍스처 |
| `Runtime/Textures/Tiles/` | 아파트 파사드 타일 6종 + 콘크리트/타일붙임/옥상 |
| `Runtime/HDRI/` | `1K.hdr` / `2K.hdr` / `Sky_Yeouido.mat` |
| `Scenes/` | `Yeouido63.unity` + `Yeouido63_Post.asset` |
| `Documentation~/` | 모델 제작 노트 + 파사드 원본 사진 (Unity 가 무시하는 폴더) |

## 쓰는 법

메뉴 **`Tools ▸ Yeouido 63 ▸ 씬 셋업`** 한 번. 재질·스카이박스·태양·카메라·포스트까지
다 잡고 `Scenes/Yeouido63.unity` 로 저장해요.

경로는 `Editor/SetupYeouido63.cs` 위쪽 `Pkg` / `Root` 상수 한 곳에 모여 있어요.
패키지 id 를 바꾸면 `Pkg` 만 고치면 돼요.

자세한 제작 근거(태양 각도, UV 미터 단위, 뺀 것)는 `Documentation~/model-notes.md`.

## 의존성

- Unity 6000.0+
- URP 17.0.0+
