# Changelog

## [0.1.0] - 2026-09-09

### Added
- `Assets/ALL` 에 흩어져 있던 여의도 63 에셋을 임베디드 UPM 패키지로 감쌌어요.
- `Virnect.Yeouido63.Editor` asmdef 추가 (패키지 안 스크립트는 asmdef 없으면 컴파일 안 됨).

### Changed
- `SetupYeouido63.cs` 경로 상수를 `Packages/com.virnect.yeouido63/...` 기준으로 교체.
- `3D/TXT/APT/tiles` → `Runtime/Textures/Tiles`, `HDRI` → `Runtime/HDRI`,
  `SCENES` → `Scenes`, FBX → `Runtime/Models`.

### Removed
- 빈 `3D/MODLE` 폴더.
- `yeouido_63.fbm` (FBX 내장 텍스처 추출 캐시, `Runtime/Textures` 와 15장 중복).
- 파사드 원본 사진 `*.jpg` 는 `Documentation~/APT_refs/` 로 빼서 빌드에서 제외.
