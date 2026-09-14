// 에셋 경로를 한 곳에 모은다
//
// 왜 필요한가
//   같은 경로 문자열이 파일마다 흩어져 있었다. Yeouido63_Post.asset 하나만
//   13 개 파일에 하드코딩돼 있었다. 문자열 자체는 전부 일치했지만, 폴더를
//   옮기는 순간 13 곳을 동시에 고쳐야 하고 하나라도 빠뜨리면 조용히
//   "에셋 없음" 으로 떨어진다.
//
//   const 라 컴파일 타임에 접혀서 실행 비용은 0 이다.
//
// 왜 SetupYeouido63 의 ResolvePkg 를 안 쓰나
//   그쪽은 FBX 위치에서 패키지 루트를 거꾸로 짚는 동적 해석이다. 원래는
//   임베디드 UPM 패키지(Packages/com.virnect.yeouido63)로 옮길 계획이라
//   그렇게 짰는데, 실제로는 Assets/yeouido63 에 그대로 남았다.
//   지금 구조에서는 상수가 정확하고 싸다. 패키지로 옮길 때가 오면
//   여기 Root 한 줄만 바꾸면 된다.

public static class Yeouido63Paths
{
    public const string Root = "Assets/yeouido63";

    public const string SceneDir  = Root + "/Scenes";
    public const string Scene     = SceneDir + "/Yeouido63.unity";
    public const string Post      = SceneDir + "/Yeouido63_Post.asset";

    public const string RuntimeDir = Root + "/Runtime";
    public const string FogDir     = RuntimeDir + "/Fog";
    public const string FogMat     = FogDir + "/M_VolumetricFog.mat";

    public const string GrassDir = RuntimeDir + "/Textures/Grass";
    public const string GrassMat = GrassDir + "/M_GrassBlades.mat";

    public const string DustDir = RuntimeDir + "/Textures/Dust";

    // 렌더 파이프라인 에셋은 yeouido63 폴더 밖이라 Root 를 안 탄다.
    public const string RPAsset  = "Assets/Settings/PC_RPAsset.asset";
    public const string Renderer = "Assets/Settings/PC_Renderer.asset";

    // 캡처 산출물. Temp 는 git 에 안 올라가고 Unity 가 지워도 무방하다.
    public const string CaptureDir = "Temp/Captures";
}
