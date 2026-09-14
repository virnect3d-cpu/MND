// 구름 짙기와 안개 사거리를 조정한다
//
// 메뉴: Tools/Yeouido 63/대기 튜닝
//
// 왜 필요한가
//   포스트프로세싱을 켜고 나서야 볼류메트릭 구름이 화면에 실제로 뭘 하는지
//   보였다. ProbeClouds 로 재 보니 픽셀 45.31% 를 바꾼다 — 하늘 전체에
//   옅은 베일처럼 덮여 있었다. 스카이박스에 이미 괜찮은 구름이 그려져
//   있는데 그 위에 저해상도(resolutionScale 0.5) 막을 한 겹 더 씌우니
//   하늘의 파랑이 죽고 스카이박스 디테일까지 뭉갰다.
//
//   짙기를 낮추면 볼륨감은 남기면서 스카이박스를 덜 가린다. 레이마칭
//   조기 종료도 빨라져서 비용이 같이 내려간다.
//
//   안개 _MaxDistance 는 카메라 far clip 보다 크면 그만큼이 순수 낭비다.
//   실제 카메라(main_came)의 far 는 1000 인데 1200 이 들어가 있었다.
//   씬에 아무것도 없는 200m 구간을 스텝 9 개나 더 돌았다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TuneAtmosphere
{
    const string PostPath = Yeouido63Paths.Post;
    const string FogMat   = Yeouido63Paths.FogMat;

    // 0.14 는 하늘을 통째로 덮었다. 볼륨감만 남기는 선.
    public const float CloudDensity = 0.06f;

    // 주 스텝 수. 24 -> 48.
    //
    //   탑 주변 구름 가장자리가 스프레이 뿌린 것처럼 부서지는 문제가
    //   있었다. 해상도(0.5->1.0), 업스케일 방식(Bilinear->Bilateral),
    //   시간누적(0.95->0) 을 각각 꺼 봤지만 셋 다 가장자리가 그대로였다 —
    //   같은 자리를 잘라 나란히 놓으니 구분이 안 갔다.
    //
    //   범인은 레이마칭 스텝 수였다. 24 는 구름 밀도가 변하는 속도를
    //   못 따라가서 표본이 듬성듬성 잡히고, 그게 점 무늬로 보인다.
    //   64 로 올리니 통째로 사라졌고, 24/32/40/48 을 훑어 보니 48 부터
    //   깨끗했다.
    //
    //   resolutionScale 이 0.5 라 실제 비용은 전체 해상도 24 스텝의
    //   절반 수준이다.
    public const int CloudSteps = 48;

    // far clip 1000 안쪽. 100m 는 지평선 근처 여유로 남긴다.
    public const float FogMaxDistance = 900f;

    [MenuItem("Tools/Yeouido 63/대기 튜닝")]
    public static void Run()
    {
        TuneClouds();
        TuneFog();
    }

    static void TuneClouds()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[대기] 프로파일 없음"); return; }

        VolumeComponent clouds = null;
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") { clouds = c; break; }
        if (clouds == null) { Debug.LogError("[대기] 구름 오버라이드 없음"); return; }

        if (clouds.GetType().GetField("densityMultiplier")?.GetValue(clouds)
            is VolumeParameter<float> p)
        {
            float before = p.value;
            p.value = CloudDensity;
            p.overrideState = true;
            Debug.Log($"[대기] 구름 짙기 {before:F3} -> {p.value:F3}");
        }
        else Debug.LogError("[대기] densityMultiplier 를 못 잡았다");

        // 스텝 수를 반드시 오버라이드까지 켠다. 값만 쓰고 스위치가 꺼져
        // 있으면 렌더러가 기본값을 쓴다 — 이 시스템에서 제일 흔한 함정이다.
        if (clouds.GetType().GetField("numPrimarySteps")?.GetValue(clouds)
            is VolumeParameter<int> s)
        {
            int before = s.value;
            s.value = CloudSteps;
            s.overrideState = true;
            Debug.Log($"[대기] 구름 주스텝 {before} -> {s.value} (가장자리 부서짐 해결)");
        }
        else Debug.LogError("[대기] numPrimarySteps 를 못 잡았다");

        EditorUtility.SetDirty(post);
    }

    static void TuneFog()
    {
        var fog = AssetDatabase.LoadAssetAtPath<Material>(FogMat);
        if (fog == null) { Debug.LogError("[대기] 안개 머티리얼 없음"); return; }

        float before = fog.GetFloat("_MaxDistance");
        fog.SetFloat("_MaxDistance", FogMaxDistance);
        EditorUtility.SetDirty(fog);

        // 스텝 수를 같이 찍는다. 이 숫자가 안개 비용을 거의 다 결정한다.
        float step = fog.GetFloat("_StepSize");
        Debug.Log($"[대기] 안개 사거리 {before:F0} -> {FogMaxDistance:F0} m " +
                  $"(스텝 {before / step:F0} -> {FogMaxDistance / step:F0} 회, " +
                  $"스텝 크기 {step:F0} m)");

        AssetDatabase.SaveAssets();
    }
}
