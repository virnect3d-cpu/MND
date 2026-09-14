// 구름이 탑 실루엣과 만나는 경계를 확대해서 연속 프레임으로 찍는다
//
// 메뉴: Tools/Yeouido 63/구름 경계 진단
//
// 왜 필요한가
//   "구름이 움직이거나 탑이랑 부딪치면 알파 에러가 난다" 는 현상은
//   기본 화각의 정지 한 장으로는 안 잡힌다. 탑이 화면에서 작고,
//   움직일 때만 나오는 거라면 한 프레임으로는 비교 대상이 없다.
//
//   그래서 두 가지를 한다.
//     1) 탑 쪽으로 화각을 좁혀(FOV 를 줄여) 경계를 확대한다
//     2) shapeOffset 을 조금씩 밀면서 여러 장을 찍는다
//   장 사이에서 경계 픽셀이 어떻게 달라지는지 보면 원인이 갈린다.
//
//   해상도 절반(resolutionScale 0.5)에서 올라오는 업샘플이 원인이면
//   탑 가장자리에 계단/후광이 정지 상태에서도 보인다.
//   시간 누적(temporal) 이 원인이면 정지 상태에선 깨끗한데 움직이는
//   장에서만 끌린 자국이 남는다. 그 차이를 보려는 것이다.
//
// 카메라는 건드렸다가 원래대로 되돌린다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProbeCloudEdge
{
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string OutDir   = "Temp/Captures";
    const int    W = 1280, H = 720;

    // 탑 쪽을 확대해서 본다. FOV 를 좁히면 같은 픽셀 수에 경계가 더 크게
    // 잡혀서 1~2 픽셀짜리 후광도 눈에 들어온다.
    const float ZoomFov = 14f;

    [MenuItem("Tools/Yeouido 63/구름 경계 진단")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[경계] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;

        var offsetField = clouds.GetType().GetField("shapeOffset");
        var offset = offsetField?.GetValue(clouds) as VolumeParameter<Vector3>;
        if (offset == null) { Debug.LogError("[경계] shapeOffset 을 못 잡았다"); return; }

        // 탑을 화면 중앙에 두기 위해 카메라를 돌린다.
        var tower = FindTower();
        float prevFov = cam.fieldOfView;
        var prevRot = cam.transform.rotation;

        if (tower != null)
        {
            cam.transform.rotation = Quaternion.LookRotation(
                tower.position - cam.transform.position, Vector3.up);
            Debug.Log($"[경계] 탑({tower.name}) 을 향해 카메라를 돌렸다.");
        }
        else Debug.LogWarning("[경계] 탑을 못 찾아서 현재 방향 그대로 확대만 한다.");

        cam.fieldOfView = ZoomFov;

        var baseOffset = offset.value;
        Directory.CreateDirectory(OutDir);

        try
        {
            // 구름을 조금씩 밀면서 4 장. 0.6m 씩이면 절반 해상도에서
            // 한두 픽셀 움직이는 정도라, 움직임이 원인일 때 차이가 드러난다.
            for (int i = 0; i < 4; i++)
            {
                offset.value = baseOffset + new Vector3(i * 0.6f, 0f, 0f);
                offset.overrideState = true;
                Shot(cam, $"edge_{i}");
            }
            Debug.Log($"[경계] 4 장 저장: {OutDir}/edge_0..3.png  (FOV {ZoomFov}, " +
                      $"shapeOffset x 를 0.6m 씩 밀었다)");
        }
        finally
        {
            offset.value = baseOffset;
            cam.fieldOfView = prevFov;
            cam.transform.rotation = prevRot;
        }
    }

    // 카메라는 인자로 받는다. Camera.main 을 매번 다시 찾으면 그 사이에
    // 오브젝트가 지워졌을 때 죽은 참조를 잡는다 — 실제로 한 번 터졌다.
    static void Shot(Camera cam, string name)
    {
        // MSAA 를 끈 RT 로 받는다. 읽어야 하니 resolve 가 끼면 안 된다.
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    static VolumeComponent FindClouds()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[경계] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[경계] 구름 오버라이드 없음");
        return null;
    }

    // 탑은 이름이 확실치 않으니 후보를 훑는다. 못 찾으면 null.
    static Transform FindTower()
    {
        string[] hints = { "tower", "안테나", "antenna", "spire", "탑", "mast" };
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string n = r.name.ToLowerInvariant();
            foreach (var h in hints)
                if (n.Contains(h)) return r.transform;
        }

        // 이름으로 못 찾으면 가장 높이 솟은 렌더러를 쓴다.
        Transform best = null; float bestY = float.MinValue;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            float top = r.bounds.max.y;
            if (top > bestY) { bestY = top; best = r.transform; }
        }
        return best;
    }
}
