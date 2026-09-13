// 씬의 실제 렌더링 비용을 잰다
//
// 메뉴: Tools/Yeouido 63/성능 측정
//
// 왜 필요한가
//   "무거울 것 같다" 는 추측으로는 어디를 고쳐야 할지 못 정한다. 드로콜,
//   삼각형 수, 파티클 수, 잔디 배치 수를 실제로 세어 놓고 시작한다.
//
//   에디터 통계라 빌드와 정확히 같지는 않다. 절대값보다 "무엇이 지배적인가"
//   를 보는 용도다.

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProfileScene
{
    const string Flag = "Temp/yeouido63_profile.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[성능] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/성능 측정")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 여의도 63 씬 성능 측정 ===");

        // --- 렌더러 ---
        var renderers = Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        int totalTris = 0, totalVerts = 0;
        int shadowCasters = 0;
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            // 인스턴스 1 개당 삼각형. 배치로 묶여 있어도 실제 지오메트리는 이만큼이다.
            totalTris += mf.sharedMesh.triangles.Length / 3;
            totalVerts += mf.sharedMesh.vertexCount;
            if (r.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
        }

        sb.AppendLine($"MeshRenderer  {renderers.Length} 개, 삼각형 {totalTris:N0}, 정점 {totalVerts:N0}");
        sb.AppendLine($"  그림자 드리우는 것 {shadowCasters} 개");

        // 삼각형이 어디 몰렸는지. 총량만 알면 어디를 줄일지 못 정한다.
        sb.AppendLine("  무거운 순:");
        var heavy = renderers
            .Select(r => new { r, mf = r.GetComponent<MeshFilter>() })
            .Where(x => x.mf != null && x.mf.sharedMesh != null)
            .Select(x => new { x.r, tris = x.mf.sharedMesh.triangles.Length / 3 })
            .OrderByDescending(x => x.tris);
        foreach (var x in heavy)
            sb.AppendLine($"    {x.tris,9:N0}  {x.r.name}  " +
                          $"(그림자 {x.r.shadowCastingMode}, 정적 {x.r.gameObject.isStatic})");

        // 머티리얼이 몇 종인지 — 드로콜 배칭의 상한을 좌우한다.
        var mats = renderers
            .SelectMany(r => r.sharedMaterials)
            .Where(m => m != null)
            .Distinct()
            .ToArray();
        sb.AppendLine($"  머티리얼 {mats.Length} 종");
        foreach (var m in mats)
            sb.AppendLine($"    {m.name}  (shader: {(m.shader == null ? "없음" : m.shader.name)}, " +
                          $"queue {m.renderQueue}, GPU instancing {m.enableInstancing})");

        // --- 파티클 ---
        var systems = Object.FindObjectsByType<ParticleSystem>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int maxParticles = 0;
        sb.AppendLine($"ParticleSystem {systems.Length} 개");
        foreach (var ps in systems)
        {
            var main = ps.main;
            maxParticles += main.maxParticles;
            var rend = ps.GetComponent<ParticleSystemRenderer>();

            // 화면을 덮는 면적이 오버드로의 핵심이다. 알갱이 크기와 개수를 같이 본다.
            float sizeMax = main.startSize.constantMax;
            sb.AppendLine($"  {ps.name}: 최대 {main.maxParticles}, 크기~{sizeMax:F3}m, " +
                          $"모드 {(rend == null ? "?" : rend.renderMode.ToString())}, " +
                          $"머티리얼 {(rend == null || rend.sharedMaterial == null ? "없음" : rend.sharedMaterial.name)}");
        }
        sb.AppendLine($"  파티클 합계 최대 {maxParticles:N0}");

        // --- 조명 / 그림자 ---
        var lights = Object.FindObjectsByType<Light>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        sb.AppendLine($"Light {lights.Length} 개");
        foreach (var l in lights)
            sb.AppendLine($"  {l.name}: {l.type}, 그림자 {l.shadows}, 강도 {l.intensity:F2}");

        // --- 품질 설정 ---
        sb.AppendLine($"품질: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        sb.AppendLine($"  그림자 거리 {QualitySettings.shadowDistance} m, " +
                      $"캐스케이드 {QualitySettings.shadowCascades}, " +
                      $"해상도 {QualitySettings.shadowResolution}");
        sb.AppendLine($"  안티앨리어싱 {QualitySettings.antiAliasing}x, VSync {QualitySettings.vSyncCount}");

        var urp = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine($"  파이프라인: {(urp == null ? "내장" : urp.name)}");

        Debug.Log(sb.ToString());
    }
}
