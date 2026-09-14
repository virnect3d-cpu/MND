// 탑 위로 구름이 새는 걸 뭘로 막을 수 있는지 시험한다
//
// 메뉴: Tools/Yeouido 63/구름 누수 해결 시험
//
// 확인된 사실
//   ProbeCloudOrder 로 재 보니 탑 안쪽 픽셀의 4.46% 가 구름을 켜면
//   색이 달라진다. 안티앨리어싱 경계는 판정에서 뺐으니 진짜 누수다.
//   차이 이미지를 보면 탑 상단 격자 링이 통째로 빛난다.
//
// 왜 새나
//   구름은 resolutionScale 0.5 로 절반 크기에 그려진다. 그러면 구름의
//   한 픽셀이 화면의 2x2 를 담당하는데, 그 자리의 깊이는 한 점에서만
//   읽는다. 탑처럼 얇은 격자에서는 그 한 점이 링 사이 빈틈(=하늘,
//   먼 깊이)에 걸리기 쉽고, 그러면 "여긴 하늘이니 구름을 그려도 된다"
//   고 판단해서 링 위까지 칠해 버린다.
//
//   그래서 해상도를 올리면 줄어야 한다. 그게 맞는지 숫자로 본다.
//
// 무엇을 시험하나
//   resolutionScale 을 0.5 / 0.75 / 1.0 으로 바꿔 가며 누수율을 잰다.
//   업스케일 방식(Bilinear/Bilateral)도 같이 본다 — Bilateral 은
//   이웃 표본을 섞으므로 경계에서 다르게 나올 수 있다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProbeOrderFix
{
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string OutDir   = "Temp/Captures";
    const int    W = 1280, H = 720;
    const float  ZoomFov = 14f;

    [MenuItem("Tools/Yeouido 63/구름 누수 해결 시험")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[누수] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;
        var state = clouds.GetType().GetField("state")?.GetValue(clouds)
                    as VolumeParameter<bool>;
        if (state == null) { Debug.LogError("[누수] state 를 못 잡았다"); return; }

        var feature  = FindCloudsFeature();
        var resProp  = feature?.GetType().GetProperty("ResolutionScale");
        var modeProp = feature?.GetType().GetProperty("UpscaleMode");
        if (resProp == null) { Debug.LogError("[누수] ResolutionScale 을 못 잡았다"); return; }

        bool   baseState = state.value;
        float  baseRes   = (float)resProp.GetValue(feature);
        object baseMode  = modeProp?.GetValue(feature);

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

            // 기준이 되는 "구름 끔" 장은 한 번만 찍으면 된다.
            // 해상도를 바꿔도 구름이 없으면 화면은 같다.
            state.value = false; state.overrideState = true;
            var off = Shot(cam, "leak_off");

            foreach (var (res, modeIdx, label) in new[]
            {
                (0.5f,  0, "0.50_Bilinear"),
                (0.5f,  1, "0.50_Bilateral"),
                (0.75f, 0, "0.75_Bilinear"),
                (1.0f,  0, "1.00_Bilinear"),
            })
            {
                resProp.SetValue(feature, res);
                if (modeProp != null)
                    modeProp.SetValue(feature,
                        System.Enum.ToObject(modeProp.PropertyType, modeIdx));

                state.value = true; state.overrideState = true;
                var on = Shot(cam, $"leak_{label}");
                float pct = Leak(off, on);
                Object.DestroyImmediate(on);

                Debug.Log($"[누수] {label,-16} 탑 누수 {pct:F2}%");
            }

            Object.DestroyImmediate(off);
            Debug.Log("[누수] 낮을수록 탑이 구름을 제대로 가린다.");

            // 해상도를 올려도 누수가 안 줄면 원인이 딴 데 있다.
            // 구름층 고도와 탑/카메라의 실제 높이를 찍어서 비교한다.
            if (tower != null)
            {
                var rends = tower.GetComponentsInChildren<MeshRenderer>();
                var b = tower.GetComponent<MeshRenderer>() != null
                    ? tower.GetComponent<MeshRenderer>().bounds
                    : new Bounds(tower.position, Vector3.zero);
                foreach (var r in rends) b.Encapsulate(r.bounds);

                var bottom = GetParam<float>(clouds, "bottomAltitude");
                var range  = GetParam<float>(clouds, "altitudeRange");
                float lo = bottom?.value ?? -1, hi = lo + (range?.value ?? 0);

                Debug.Log($"[누수] 탑 월드 높이 {b.min.y:F1} ~ {b.max.y:F1} m, " +
                          $"카메라 y={cam.transform.position.y:F1}; " +
                          $"구름층 {lo:F0} ~ {hi:F0} m; " +
                          $"탑 꼭대기가 구름 바닥보다 " +
                          (b.max.y > lo ? "높다 -> 탑이 구름을 뚫는 게 정상"
                                        : "낮다 -> 구름이 탑 위로 새면 안 된다"));
            }
        }
        finally
        {
            state.value = baseState; state.overrideState = true;
            resProp.SetValue(feature, baseRes);
            if (modeProp != null && baseMode != null) modeProp.SetValue(feature, baseMode);
            cam.fieldOfView = prevFov;
            cam.transform.rotation = prevRot;
        }
    }

    // 탑 안쪽 픽셀 중 구름 켜서 달라진 비율
    static float Leak(Texture2D off, Texture2D on)
    {
        var a = off.GetPixels32();
        var b = on.GetPixels32();
        int w = off.width, h = off.height;
        int towerPx = 0, changed = 0;

        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
        {
            int i = y * w + x;
            if (!IsTower(a[i])) continue;
            // 실루엣 경계는 뺀다 — 거긴 원래 섞인다
            if (!IsTower(a[i - 1]) || !IsTower(a[i + 1]) ||
                !IsTower(a[i - w]) || !IsTower(a[i + w])) continue;

            towerPx++;
            var c = a[i]; var d = b[i];
            if (Mathf.Abs(c.r - d.r) + Mathf.Abs(c.g - d.g) + Mathf.Abs(c.b - d.b) > 8)
                changed++;
        }
        return towerPx > 0 ? 100f * changed / towerPx : 0f;
    }

    static VolumeParameter<T> GetParam<T>(VolumeComponent c, string name)
        => c.GetType().GetField(name)?.GetValue(c) as VolumeParameter<T>;

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
        if (post == null) { Debug.LogError("[누수] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[누수] 구름 오버라이드 없음");
        return null;
    }

    static ScriptableRendererFeature FindCloudsFeature()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            foreach (var f in data.rendererFeatures)
                if (f != null && f.GetType().Name.Contains("VolumetricClouds")) return f;
        }
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
