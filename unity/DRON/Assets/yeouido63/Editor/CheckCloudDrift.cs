// 구름 드리프트가 실제로 도는지 확인한다
//
// 메뉴: Tools/Yeouido 63/구름 드리프트 확인
//
// 디스크의 .asset 을 보는 것으로는 판단할 수 없다. 프로파일은 세이브
// 시점에만 기록되므로, 값이 0 으로 보여도 메모리에서는 돌고 있을 수 있고
// 그 반대일 수도 있다. 메모리 값을 시간차로 두 번 읽어 비교한다.

using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

public static class CheckCloudDrift
{
    const string Flag     = "Temp/yeouido63_checkdrift.flag";
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[구름확인] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/구름 드리프트 확인")]
    public static void Run()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[구름확인] 프로파일 없음"); return; }

        VolumeComponent clouds = null;
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") { clouds = c; break; }
        if (clouds == null) { Debug.LogError("[구름확인] VolumetricClouds 오버라이드 없음"); return; }

        var handle = Object.FindFirstObjectByType<CloudLayerHandle>();
        Debug.Log($"[구름확인] 핸들 {(handle == null ? "없음" : handle.name)}, " +
                  $"enabled={(handle != null && handle.enabled)}, " +
                  $"driftSpeed={(handle == null ? -1f : handle.driftSpeed)}");

        var t0 = Read(clouds);
        float start = Time.realtimeSinceStartup;

        // 3 초 뒤에 다시 읽는다. 12 m/s 면 36 m 쯤 움직여 있어야 한다.
        void Poll()
        {
            if (Time.realtimeSinceStartup - start < 3f) return;
            EditorApplication.update -= Poll;

            var t1 = Read(clouds);
            var d = t1 - t0;
            float elapsed = Time.realtimeSinceStartup - start;

            Debug.Log($"[구름확인] {elapsed:F1}초 동안 shapeOffset " +
                      $"({t0.x:F1},{t0.z:F1}) -> ({t1.x:F1},{t1.z:F1}), " +
                      $"변화 ({d.x:F1},{d.z:F1}) = {d.magnitude / Mathf.Max(elapsed, 0.01f):F1} m/s");

            if (d.magnitude < 0.01f)
                Debug.LogWarning("[구름확인] 안 움직인다. 에디터가 Update 를 안 돌리는 중일 수 있다.");
            else
                Debug.Log("[구름확인] 드리프트 정상 동작.");
        }
        EditorApplication.update += Poll;
    }

    static Vector3 Read(VolumeComponent clouds)
    {
        var f = clouds.GetType().GetField("shapeOffset");
        if (f == null) return Vector3.zero;
        return f.GetValue(clouds) is VolumeParameter<Vector3> p ? p.value : Vector3.zero;
    }
}

