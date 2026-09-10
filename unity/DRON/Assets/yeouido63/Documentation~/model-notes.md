# 여의도 63빌딩 — Blender → Unity

Blender 씬 `yeouido_3d/yeouido_3dmap.blend` (별도 저작 리포) 를
URP 로 옮긴 것. 인공위성 정사영상 + DEM 기반 실측 도시 맵이다.

## 쓰는 법

메뉴 **`Tools ▸ Yeouido 63 ▸ 씬 셋업`** 한 번 누르면 끝난다.
`Editor/SetupYeouido63.cs` 가 아래를 자동으로 한다.

1. FBX 임포트 설정 (스케일 1, 카메라·라이트 임포트 끔, UInt32 인덱스)
2. `Runtime/HDRI/2K.hdr` 로 `Skybox/Panoramic` 재질 생성 (`Sky_Yeouido.mat`)
3. 새 씬 생성 → 스카이박스·앰비언트·포그·태양·카메라·포스트 볼륨 배치
4. `Scenes/Yeouido63.unity` 로 저장

> 재질 GUID 를 손으로 쓰면 깨지기 쉬워서 .mat/.meta 를 직접 만들지 않고
> 에디터 API 로 만든다.

## 들어 있는 것

| 오브젝트 | 면 | 텍스처 |
|---|---|---|
| `TERRAIN` | 74,884 | 4096² (VWorld 항공영상 0.24 m/px 베이크) |
| `BUILDINGS` | 121,211 | 4096² (아파트 파사드 6종 + 옥상 콘크리트) |
| `ROADLINES` | 7,814 | 2048² |
| `BLD_63SQUARE` | 21 | 2048² (금박 유리 + 멀리언·층선·기계층) |
| `SEA` | 1 | 1024² (한강) |

합계 **오브젝트 5 / 정점 245,233 / 면 203,931**, FBX 15.4 MB + 텍스처 37 MB.

각 오브젝트마다 `_basecolor` / `_roughness` 두 장. 좌표 1 unit = 1 m.

## Blender 와 맞춘 값

| | 값 | 근거 |
|---|---|---|
| 태양 | 고도 45.7° / 방위 298.8° | `2K.hdr` 에서 가장 밝은 지점을 찾아 산출 |
| 태양 세기 | 1.4 | Blender `SUN_STRENGTH` 와 동일 |
| 스카이박스 회전 | 270° | Blender 는 u=0.5 가 −X, Unity Panoramic 은 u=0.5 가 +Z |
| 노출 | −0.45 | Blender `view_settings.exposure` |
| 톤매핑 | Neutral | URP 에 AgX 가 없어서 가장 가까운 것 |
| 카메라 | FOV 25.4° (수직) | Blender `CAM_63_Air` 45 mm / 36 mm 센서 / 16:9 |
| far clip | 30,000 | AOI 가 3.2 km 라 기본 1000 이면 다 잘린다 |

축 변환은 `Blender(x, y, z) → Unity(x, z, y)` (FBX 를 −Z forward / Y up 으로 냈다).

## 뺀 것

나무 6,289 / 가로등 1,473 / 전신주 1,270 은 **뺐다.**
해안 파이프라인 잔재(테트라포드·어선·파라솔 프로토)도 뺐다.

> 나무를 다시 넣으려면 Geometry Nodes 스캐터라 **Realize Instances 를 끼워야** 한다.
> `use_mesh_modifiers=True` 만으로는 인스턴스가 안 나가서 면 0 으로 빠진다.
> `tools/bake_for_unity.py` 의 `realize_scatter()` 참고. 나무만 191,019 면 늘어난다.

## 다시 뽑으려면

```powershell
cd <Blender 소스 리포 루트>
# 1) 절차적 재질 -> 텍스처 베이크 (UV 가 0~1 밖이면 스마트 UV 를 새로 편다)
blender -b yeouido_3d\yeouido_3dmap.blend --python tools\bake_for_unity.py -- --res 2048 --fbx
```

베이크 UV 주의: 이 프로젝트 UV 는 대부분 **미터 단위**다 (`BUILDINGS` u 0~940).
0~1 밖 UV 로 구우면 이미지 밖에 그려져서 빈 텍스처가 나온다.
`tools/bake_for_maya.py` 는 그 검사가 없으니 유니티행에는 쓰지 말 것.
