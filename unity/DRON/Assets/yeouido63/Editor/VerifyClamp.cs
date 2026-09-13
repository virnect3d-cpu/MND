// 셰이더 Range 선언에 값이 잘리는지 실제로 확인한다
//
// 메뉴: Tools/Yeouido 63/클램프 검증
//
// 왜 필요한가
//   .mat 파일에 0.035 라고 적혀 있어도 셰이더 프로퍼티가
//   Range(0, 0.02) 면 실제로 쓰이는 값이 0.02 일 수 있다. 디스크의
//   숫자와 GPU 에 가는 숫자가 다른 경우라 파일만 봐서는 모른다.
//
//   material.GetFloat 으로 되읽어서 쓴 값이 그대로 나오는지 본다.

using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class VerifyClamp
{
    const string Flag   = "Temp/yeouido63_clamp.flag";
    const string FogMat = "Assets/yeouido63/Runtime/Fog/M_VolumetricFog.mat";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[클램프] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/클램프 검증")]
    public static void Run()
    {
        var fog = AssetDatabase.LoadAssetAtPath<Material>(FogMat);
        if (fog == null) { Debug.LogError("[클램프] 안개 머티리얼 없음"); return; }

        var sb = new StringBuilder();
        sb.AppendLine("=== 클램프 검증 ===");

        // 지금 들어 있는 값
        sb.AppendLine($"현재 _HeightFalloff = {fog.GetFloat("_HeightFalloff"):F6}");
        sb.AppendLine($"현재 _HeightBase    = {fog.GetFloat("_HeightBase"):F3}");

        // 0.035 를 다시 써 보고 되읽는다. 그대로 나오면 안 잘린 것이고,
        // 0.02 로 바뀌면 Range 선언에 잘린 것이다.
        fog.SetFloat("_HeightFalloff", 0.035f);
        float read = fog.GetFloat("_HeightFalloff");
        sb.AppendLine($"0.035 쓰고 되읽기 -> {read:F6}  " +
                      $"{(Mathf.Abs(read - 0.035f) < 1e-5f ? "안 잘림 (Range 무시됨)" : "잘림!")}");

        // 셰이더가 실제로 보는 값. 머티리얼 프로퍼티 시트를 거치므로
        // GetFloat 과 다를 수 있는지 확인한다.
        var shader = fog.shader;
        int idx = shader.FindPropertyIndex("_HeightFalloff");
        if (idx >= 0)
        {
            var range = shader.GetPropertyRangeLimits(idx);
            sb.AppendLine($"셰이더 선언 범위: [{range.x}, {range.y}]");
        }

        // 고도별 밀도배율. 이게 실제로 아지랑이를 좌우한다.
        float f = read, b = fog.GetFloat("_HeightBase");
        sb.AppendLine("고도별 밀도배율 exp(-(y-base)*falloff):");
        foreach (float y in new[] { 20f, 60f, 150f, 250f, 370f })
            sb.AppendLine($"  {y,4:F0} m -> {Mathf.Exp(-Mathf.Max(0f, y - b) * f):F5}");

        CheckVolumeClamps(sb);
        Debug.Log(sb.ToString());
    }

    // VolumeParameter 의 min/max 가 [NonSerialized] 라, 에셋에서 복원될 때
    // 0 으로 남을 수 있다는 지적이 있었다. 그러면 ClampedFloatParameter(0,1)
    // 짜리가 max=0 이 되어 짙기와 속도를 통째로 0 으로 삼킨다 — 구름이
    // 사라지는데 로그는 안 남는 종류의 사고다. 실제로 그런지 찍어 본다.
    static void CheckVolumeClamps(StringBuilder sb)
    {
        const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
        var post = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(PostPath);
        if (post == null) { sb.AppendLine("프로파일 없음"); return; }

        UnityEngine.Rendering.VolumeComponent clouds = null;
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") { clouds = c; break; }
        if (clouds == null) { sb.AppendLine("구름 오버라이드 없음"); return; }

        sb.AppendLine("=== VolumeParameter min/max 복원 확인 ===");
        foreach (var name in new[] { "globalOrientation", "densityMultiplier",
                                     "shapeSpeedMultiplier", "erosionSpeedMultiplier",
                                     "bottomAltitude", "altitudeRange" })
        {
            var fi = clouds.GetType().GetField(name);
            if (fi == null) { sb.AppendLine($"  {name}: 필드 없음"); continue; }

            var val = fi.GetValue(clouds);
            switch (val)
            {
                case UnityEngine.Rendering.ClampedFloatParameter cp:
                    sb.AppendLine($"  {name}: Clamped[{cp.min}, {cp.max}] = {cp.value}" +
                                  (cp.max == 0f ? "   <-- max=0, 값이 삼켜진다!" : ""));
                    break;
                case UnityEngine.Rendering.MinFloatParameter mp:
                    sb.AppendLine($"  {name}: Min[{mp.min}] = {mp.value}");
                    break;
                default:
                    sb.AppendLine($"  {name}: {val?.GetType().Name}");
                    break;
            }
        }
    }
}
