// 구름이 탑을 덮는지(순서 문제)를 픽셀로 판정한다
//
// 메뉴: Tools/Yeouido 63/구름 순서 판정
//
// 가설
//   "탑이 구름보다 앞에 있어야 하는데 카메라가 순서를 헷갈려서
//    구름이 탑 위에 덮인다."
//
// 어떻게 판정하나
//   구름을 켠 장과 끈 장을 같은 카메라 위치에서 찍는다.
//   그리고 탑 픽셀만 골라서 두 장을 비교한다.
//
//     탑 픽셀이 구름 켤 때 달라진다  -> 구름이 탑을 덮고 있다 (순서 문제 O)
//     탑 픽셀이 그대로다             -> 탑이 제대로 구름을 가린다 (순서 문제 X)
//
//   탑 픽셀은 구름 끈 장에서 고른다. 켠 장에서 고르면 이미 구름에
//   덮인 픽셀을 하늘로 오인해서 표본이 오염된다.
//
// 왜 이게 그럴듯한가
//   구름 렌더러 피처는 BeforeRenderingTransparents(450)에 들어가고
//   depthTexture 가 꺼져 있다. 즉 구름은 자기 깊이를 씬 깊이에 안 쓴다.
//   구름이 불투명 지오메트리의 깊이를 읽어서 스스로 잘리는 구조라,
//   그 깊이 읽기가 어긋나면 탑 위로 새어 나온다.
//
//   특히 탑은 얇은 격자라 구름이 절반 해상도로 그려지면 탑 사이
//   빈틈의 깊이를 잡아서 탑 실루엣 위까지 구름을 칠할 수 있다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProbeCloudOrder
{
    const string PostPath = Yeouido63Paths.Post;
    const string OutDir   = Yeouido63Paths.CaptureDir;
    const int    W = 1280, H = 720;
    const float  ZoomFov = 14f;

    [MenuItem("Tools/Yeouido 63/구름 순서 판정")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[순서] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;

        var state = clouds.GetType().GetField("state")?.GetValue(clouds)
                    as VolumeParameter<bool>;
        if (state == null) { Debug.LogError("[순서] state 를 못 잡았다"); return; }

        bool baseState = state.value;
        var tower = FindTower();
        float prevFov = cam.fieldOfView;
        var   prevRot = cam.transform.rotation;
        Directory.CreateDirectory(OutDir);

        try
        {
            if (tower != null)
                cam.transform.rotation = Quaternion.LookRotation(
                    tower.position - cam.transform.position, Vector3.up);
            cam.fieldOfView = ZoomFov;

            state.value = false; state.overrideState = true;
            var off = Shot(cam, "order_off");

            state.value = true;  state.overrideState = true;
            var on  = Shot(cam, "order_on");

            Judge(off, on);
            Object.DestroyImmediate(off);
            Object.DestroyImmediate(on);
        }
        finally
        {
            state.value = baseState; state.overrideState = true;
            cam.fieldOfView = prevFov;
            cam.transform.rotation = prevRot;
        }
    }

    static void Judge(Texture2D off, Texture2D on)
    {
        var a = off.GetPixels32();
        var b = on.GetPixels32();
        int w = off.width, h = off.height;

        // 탑 픽셀을 "구름 끈 장" 에서 고른다.
        //   빨간 격자: r 이 b 보다 확실히 세다
        //   회색 구조물: 셋 다 어둡다
        // 하늘은 b 가 높으므로 자연히 빠진다.
        int towerPx = 0, changed = 0;
        long sumDiff = 0; int maxDiff = 0;

        // 가장자리 1px 은 판정에서 뺀다. 이웃을 봐야 안쪽인지 알 수 있다.
        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
        {
            int i = y * w + x;
            var c = a[i];
            bool isTower = (c.r > c.b + 25) || (c.r < 105 && c.g < 105 && c.b < 120);
            if (!isTower) continue;

            // 실루엣 경계는 뺀다. 거기는 안티앨리어싱 때문에 원래 섞인다.
            // 4 이웃이 전부 탑인 "안쪽" 픽셀만 본다.
            if (!IsTower(a[i - 1]) || !IsTower(a[i + 1]) ||
                !IsTower(a[i - w]) || !IsTower(a[i + w])) continue;

            towerPx++;
            var d = b[i];
            int diff = Mathf.Abs(c.r - d.r) + Mathf.Abs(c.g - d.g) + Mathf.Abs(c.b - d.b);
            if (diff > 8) { changed++; sumDiff += diff; }
            if (diff > maxDiff) maxDiff = diff;
        }

        float pct = towerPx > 0 ? 100f * changed / towerPx : 0f;
        Debug.Log($"[순서] 탑 안쪽 픽셀 {towerPx:N0} 개 중 구름 켜서 달라진 건 " +
                  $"{changed:N0} 개 ({pct:F2}%), 최대 차이 {maxDiff}, " +
                  $"평균 차이 {(changed > 0 ? (float)sumDiff / changed : 0f):F1}");

        if (pct < 2f)
            Debug.Log("[순서] -> 탑이 구름을 제대로 가린다. 순서 문제가 아니다.\n" +
                      "        가장자리가 거슬린다면 그건 구름 자체의 표본 문제다.");
        else if (pct < 20f)
            Debug.LogWarning("[순서] -> 탑 일부가 구름에 덮인다. 얇은 부분 위주로 새는 중.\n" +
                             "        절반 해상도 구름이 탑 사이 빈틈 깊이를 잡는 전형적인 증상이다.");
        else
            Debug.LogError("[순서] -> 탑이 구름에 통째로 덮인다. 순서/깊이 문제가 맞다.");
    }

    static bool IsTower(Color32 c)
        => (c.r > c.b + 25) || (c.r < 105 && c.g < 105 && c.b < 120);

    static Texture2D Shot(Camera cam, string name)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            for (int i = 0; i < 6; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            return tex;
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
        if (post == null) { Debug.LogError("[순서] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[순서] 구름 오버라이드 없음");
        return null;
    }

    static Transform FindTower()
    {
        string[] hints = { "tower", "안테나", "antenna", "spire", "탑", "mast" };
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string n = r.name.ToLowerInvariant();
            foreach (var h in hints) if (n.Contains(h)) return r.transform;
        }
        Transform best = null; float bestY = float.MinValue;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.bounds.max.y > bestY) { bestY = r.bounds.max.y; best = r.transform; }
        return best;
    }
}
