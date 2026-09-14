// 탑 누수가 MSAA / 깊이 복사 때문인지 본다
//
// 메뉴: Tools/Yeouido 63/구름 누수 - MSAA 시험
//
// 여기까지 밝혀진 것
//   탑 꼭대기는 27.6m, 구름층은 250~370m 다. 탑이 구름보다 222m 아래라
//   구름이 탑 앞에 올 방법이 물리적으로 없다. 그런데 탑 안쪽 픽셀의
//   4~5% 가 구름을 켜면 색이 달라진다. 명백한 누수다.
//
//   구름 해상도를 0.5 -> 1.0 으로 올려도 4.78% -> 4.24% 로 거의 안 줄었다.
//   그러니 원인은 구름을 그리는 해상도가 아니다.
//
// 남은 용의자 — 깊이 텍스처
//   구름은 _CameraDepthTexture 를 읽어서 "여기까지만 그려라" 를 정한다
//   (VolumetricClouds.hlsl:23-24). 그 깊이에 탑이 제대로 안 들어 있으면
//   구름은 탑이 없는 줄 알고 칠한다.
//
//   지금 설정이 MSAA 4x + CopyDepthMode.AfterOpaques 다. MSAA 버퍼에서
//   깊이를 복사해 내릴 때 resolve 가 일어나는데, 탑처럼 얇은 격자는
//   한 픽셀 안에 탑 샘플과 하늘 샘플이 섞인다. resolve 된 깊이가 둘
//   사이 어중간한 값이 되면 구름은 "충분히 멀다" 고 읽고 링 위를 칠한다.
//
// 무엇을 시험하나
//   MSAA 를 4 -> 1 로 내리고 누수율을 다시 잰다.
//   확 떨어지면 MSAA 가 범인이고, 그대로면 아니다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProbeLeakMsaa
{
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string OutDir   = "Temp/Captures";
    const int    W = 1280, H = 720;
    const float  ZoomFov = 14f;

    [MenuItem("Tools/Yeouido 63/구름 누수 - MSAA 시험")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[MSAA] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;
        var state = clouds.GetType().GetField("state")?.GetValue(clouds)
                    as VolumeParameter<bool>;
        if (state == null) { Debug.LogError("[MSAA] state 를 못 잡았다"); return; }

        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (rp == null) { Debug.LogError("[MSAA] URP 에셋을 못 잡았다"); return; }

        bool baseState = state.value;
        int  baseMsaa  = rp.msaaSampleCount;
        float prevFov  = cam.fieldOfView;
        var   prevRot  = cam.transform.rotation;
        bool  baseCamMsaa = cam.allowMSAA;

        var tower = FindTower();
        Directory.CreateDirectory(OutDir);

        try
        {
            if (tower != null)
                cam.transform.rotation = Quaternion.LookRotation(
                    tower.position - cam.transform.position, Vector3.up);
            cam.fieldOfView = ZoomFov;

            foreach (int msaa in new[] { 4, 2, 1 })
            {
                rp.msaaSampleCount = msaa;
                cam.allowMSAA = msaa > 1;

                state.value = false; state.overrideState = true;
                var off = Shot(cam, $"msaa{msaa}_off");

                state.value = true;  state.overrideState = true;
                var on  = Shot(cam, $"msaa{msaa}_on");

                float pct = Leak(off, on, out int towerPx);
                Debug.Log($"[MSAA] {msaa}x — 탑 누수 {pct:F2}%  (탑 안쪽 {towerPx:N0} px)");

                Object.DestroyImmediate(off);
                Object.DestroyImmediate(on);
            }

            Debug.Log("[MSAA] 낮을수록 탑이 구름을 제대로 가린다. " +
                      "1x 에서 확 떨어지면 MSAA 깊이 resolve 가 범인이다.");
        }
        finally
        {
            state.value = baseState; state.overrideState = true;
            rp.msaaSampleCount = baseMsaa;
            cam.allowMSAA = baseCamMsaa;
            cam.fieldOfView = prevFov;
            cam.transform.rotation = prevRot;
        }
    }

    static float Leak(Texture2D off, Texture2D on, out int towerPx)
    {
        var a = off.GetPixels32();
        var b = on.GetPixels32();
        int w = off.width, h = off.height;
        towerPx = 0; int changed = 0;

        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
        {
            int i = y * w + x;
            if (!IsTower(a[i])) continue;
            if (!IsTower(a[i - 1]) || !IsTower(a[i + 1]) ||
                !IsTower(a[i - w]) || !IsTower(a[i + w])) continue;

            towerPx++;
            var c = a[i]; var d = b[i];
            if (Mathf.Abs(c.r - d.r) + Mathf.Abs(c.g - d.g) + Mathf.Abs(c.b - d.b) > 8)
                changed++;
        }
        return towerPx > 0 ? 100f * changed / towerPx : 0f;
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
        if (post == null) { Debug.LogError("[MSAA] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[MSAA] 구름 오버라이드 없음");
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
