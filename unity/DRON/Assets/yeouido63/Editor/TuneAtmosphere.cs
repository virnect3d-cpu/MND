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
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string FogMat   = "Assets/yeouido63/Runtime/Fog/M_VolumetricFog.mat";

    // 0.14 는 하늘을 통째로 덮었다. 볼륨감만 남기는 선.
    public const float CloudDensity = 0.06f;

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

        var fi = clouds.GetType().GetField("densityMultiplier");
        if (fi == null) { Debug.LogError("[대기] densityMultiplier 필드 없음"); return; }

        if (fi.GetValue(clouds) is VolumeParameter<float> p)
        {
            float before = p.value;
            p.value = CloudDensity;
            p.overrideState = true;
            EditorUtility.SetDirty(post);
            Debug.Log($"[대기] 구름 짙기 {before:F3} -> {p.value:F3}");
        }
        else Debug.LogError("[대기] densityMultiplier 타입이 예상과 다르다");
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
