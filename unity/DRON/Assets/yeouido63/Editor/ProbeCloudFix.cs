// 구름 깨짐의 원인을 갈라 본다 — 해상도냐 시간누적이냐
//
// 메뉴: Tools/Yeouido 63/구름 원인 분리
//
// 무엇을 보고 있나
//   탑 주변에서 구름 가장자리가 스프레이 뿌린 것처럼 부서진다. 그리고
//   shapeOffset 을 1.2m 미는 것만으로 구름 덩어리 모양이 통째로 바뀐다.
//   1.2m 로 구름이 그렇게 변할 리 없으니 둘 다 렌더링 문제다.
//
// 의심 두 가지 — 서로 독립이라 따로 꺼 봐야 한다
//
//   A) 절반 해상도 업스케일
//      resolutionScale 0.5 로 구름을 절반 크기에 그린 뒤 키워 올린다.
//      그 업스케일이 깊이를 안 보고 "색이 비슷한 이웃" 으로만 가중치를
//      매긴다. 탑처럼 얇은 격자 앞에서는 하늘 쪽 값이 탑 위로 번져
//      투과도가 망가진다 — 그게 가장자리 부서짐으로 보인다.
//
//   B) 시간 누적(temporal accumulation)
//      이전 프레임 결과를 0.95 비율로 재사용해 노이즈를 지운다. 그런데
//      재투영을 카메라 움직임으로만 계산해서, 구름 자체가 흐르는 건
//      계산에 안 들어간다. 우리는 shapeOffset 을 매 프레임 밀고 있으니
//      히스토리를 엉뚱한 자리에서 가져온다.
//
// 어떻게 가르나
//   넷을 찍는다. 원인이 A 면 (기준)과 (A끔)이 다르고, B 면 (기준)과
//   (B끔)이 다르다. 둘 다면 넷이 전부 다르다.
//     fix_base    지금 상태
//     fix_fullres 해상도만 1.0 으로
//     fix_notemp  시간누적만 0 으로
//     fix_both    둘 다
//
//   각 장은 구름을 1.2m 민 상태로 찍는다. 움직였을 때 깨지는 거라
//   정지 상태로는 B 를 못 본다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProbeCloudFix
{
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string OutDir   = "Temp/Captures";
    const int    W = 1280, H = 720;
    const float  ZoomFov = 14f;

    // 구름을 이만큼 민 상태로 찍는다. 시간 누적 문제는 움직여야 나온다.
    const float DriftX = 1.2f;

    [MenuItem("Tools/Yeouido 63/구름 원인 분리")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[분리] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;

        var offset = GetParam<Vector3>(clouds, "shapeOffset");
        var temporal = GetParam<float>(clouds, "temporalAccumulationFactor");
        if (offset == null || temporal == null)
        {
            Debug.LogError("[분리] shapeOffset / temporalAccumulationFactor 를 못 잡았다");
            return;
        }

        // 해상도와 업스케일 방식은 볼륨이 아니라 렌더러 피처에 있다.
        //   필드는 private 이라 프로퍼티로 접근한다. 예전에 GetField 로
        //   찾다가 조용히 null 이 나와 테스트를 통째로 건너뛴 적이 있다.
        var feature = FindCloudsFeature(out string featName);
        var resProp  = feature?.GetType().GetProperty("ResolutionScale");
        var modeProp = feature?.GetType().GetProperty("UpscaleMode");
        if (resProp == null)
            Debug.LogWarning("[분리] ResolutionScale 프로퍼티를 못 찾았다. 해상도 테스트는 건너뛴다.");
        if (modeProp == null)
            Debug.LogWarning("[분리] UpscaleMode 프로퍼티를 못 찾았다. 업스케일 테스트는 건너뛴다.");

        // 원래 값 전부 보관
        var baseOffset  = offset.value;
        float baseTemp  = temporal.value;
        bool  baseTempOv= temporal.overrideState;
        float baseRes   = resProp  != null ? (float)resProp.GetValue(feature) : 0f;
        object baseMode = modeProp != null ? modeProp.GetValue(feature) : null;

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

            // 구름을 민 상태로 고정. 네 장 모두 같은 오프셋이라
            // 차이가 나면 그건 렌더링 설정 때문이다.
            offset.value = baseOffset + new Vector3(DriftX, 0f, 0f);
            offset.overrideState = true;

            Shot(cam, "fix_base");
            Debug.Log($"[분리] fix_base — 해상도 {baseRes:F2}, 업스케일 {baseMode}, " +
                      $"시간누적 {baseTemp:F2}" +
                      $"{(baseTempOv ? "" : " (오버라이드 꺼짐, 기본값 0.95 사용)")}");

            // 해상도만 1.0 — 업스케일 자체를 없앤다. 이게 원인이면 여기서 낫는다.
            if (resProp != null)
            {
                resProp.SetValue(feature, 1.0f);
                Shot(cam, "fix_fullres");
                Debug.Log("[분리] fix_fullres — 해상도 1.0 (업스케일 없음)");
                resProp.SetValue(feature, baseRes);
            }

            // 해상도는 그대로 두고 업스케일 방식만 Bilateral 로.
            //   지금은 upscaleMode 0 = Bilinear 다. 패키지 툴팁이
            //   "Bilateral 은 저해상도에서 생기는 노이즈를 줄인다" 고 한다.
            //   해상도를 안 올리고 고칠 수 있으면 이쪽이 훨씬 싸다.
            if (modeProp != null)
            {
                modeProp.SetValue(feature,
                    System.Enum.ToObject(modeProp.PropertyType, 1));   // Bilateral
                Shot(cam, "fix_bilateral");
                Debug.Log("[분리] fix_bilateral — 해상도 0.5 그대로, 업스케일 Bilateral");
                modeProp.SetValue(feature, baseMode);
            }

            temporal.value = 0f;
            temporal.overrideState = true;
            Shot(cam, "fix_notemp");
            Debug.Log("[분리] fix_notemp — 해상도 그대로, 시간누적 0");

            // 가장 좋은 조합 후보: Bilateral + 시간누적 0
            if (modeProp != null)
            {
                modeProp.SetValue(feature,
                    System.Enum.ToObject(modeProp.PropertyType, 1));
                Shot(cam, "fix_both");
                Debug.Log("[분리] fix_both — Bilateral + 시간누적 0");
                modeProp.SetValue(feature, baseMode);
            }

            Debug.Log($"[분리] 저장 완료: {OutDir}/fix_*.png");
        }
        finally
        {
            offset.value = baseOffset;
            temporal.value = baseTemp;
            temporal.overrideState = baseTempOv;
            if (resProp != null) resProp.SetValue(feature, baseRes);
            if (modeProp != null) modeProp.SetValue(feature, baseMode);
            cam.fieldOfView = prevFov;
            cam.transform.rotation = prevRot;
        }
    }

    static VolumeParameter<T> GetParam<T>(VolumeComponent c, string name)
        => c.GetType().GetField(name)?.GetValue(c) as VolumeParameter<T>;

    static VolumeComponent FindClouds()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[분리] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[분리] 구름 오버라이드 없음");
        return null;
    }

    // 구름은 렌더러 피처로 들어간다. 이름이 바뀔 수 있으니 타입명으로 찾는다.
    static ScriptableRendererFeature FindCloudsFeature(out string name)
    {
        name = null;
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;
            foreach (var f in data.rendererFeatures)
            {
                if (f == null) continue;
                if (f.GetType().Name.Contains("VolumetricClouds"))
                { name = f.name; return f; }
            }
        }
        return null;
    }

    static void Shot(Camera cam, string name)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            // 시간 누적이 걸린 상태라 한 장만 찍으면 직전 히스토리가 섞인다.
            // 몇 번 돌려서 히스토리를 현재 설정으로 채운 뒤 읽는다.
            for (int i = 0; i < 6; i++) cam.Render();

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
