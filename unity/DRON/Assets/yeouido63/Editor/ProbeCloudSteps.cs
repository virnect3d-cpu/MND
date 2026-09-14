// 구름 가장자리 깨짐이 밀도/스텝 수 때문인지 본다
//
// 메뉴: Tools/Yeouido 63/구름 밀도·스텝 진단
//
// 앞에서 뭘 배웠나
//   탑 주변 구름 가장자리가 스프레이 뿌린 것처럼 부서진다.
//   해상도(0.5 -> 1.0), 업스케일 방식(Bilinear -> Bilateral),
//   시간누적(0.95 -> 0) 을 각각 꺼 봤는데 셋 다 가장자리가 그대로였다.
//   같은 자리를 잘라 나란히 놓고 보니 세 장이 거의 구분이 안 갔다.
//
//   그러니 원인은 합성 단계가 아니라 레이마칭 자체다. 후보는 둘이다.
//
//     A) 짙기(densityMultiplier)가 너무 낮다
//        0.14 -> 0.06 으로 내렸다. 구름이 옅으면 가장자리에서 밀도가
//        문턱을 겨우 넘나들어, 표본이 잡히는 자리와 안 잡히는 자리가
//        갈라진다. 그게 점 무늬로 보인다.
//
//     B) 주 스텝 수(numPrimarySteps 24)가 모자란다
//        레이를 24 등분해 훑는데, 옅은 구름에서는 그 간격이 밀도
//        변화보다 커서 가장자리를 못 따라간다.
//
//   둘은 곱해서 작용하므로 따로 올려 보고 어느 쪽이 듣는지 본다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProbeCloudSteps
{
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string OutDir   = "Temp/Captures";
    const int    W = 1280, H = 720;
    const float  ZoomFov = 14f;

    [MenuItem("Tools/Yeouido 63/구름 밀도·스텝 진단")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[스텝] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;

        var density = GetParam<float>(clouds, "densityMultiplier");
        var steps   = GetParam<int>(clouds, "numPrimarySteps");
        var lsteps  = GetParam<int>(clouds, "numLightSteps");
        if (density == null || steps == null)
        {
            Debug.LogError("[스텝] densityMultiplier / numPrimarySteps 를 못 잡았다");
            return;
        }

        float baseDen = density.value;
        int   baseStp = steps.value;
        bool  baseStpOv = steps.overrideState;
        int   baseLs  = lsteps?.value ?? -1;
        bool  baseLsOv = lsteps?.overrideState ?? false;

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

            // 주스텝만 올려 가며 찍는다. 앞선 진단에서 24 -> 64 가
            // 가장자리 부서짐을 통째로 없애는 걸 확인했으니, 이제
            // 어디까지 내려도 버티는지 찾는 게 목적이다.
            foreach (int n in new[] { 24, 32, 40, 48, 64 })
            {
                steps.value = n; steps.overrideState = true;
                Shot(cam, $"step_n{n}");
                Debug.Log($"[스텝] step_n{n} — 주스텝 {n}, 짙기 {baseDen:F3}");
            }

            Debug.Log($"[스텝] 저장 완료: {OutDir}/step_*.png");
        }
        finally
        {
            density.value = baseDen; density.overrideState = true;
            steps.value = baseStp;   steps.overrideState = baseStpOv;
            if (lsteps != null) { lsteps.value = baseLs; lsteps.overrideState = baseLsOv; }
            cam.fieldOfView = prevFov;
            cam.transform.rotation = prevRot;
        }
    }

    static VolumeParameter<T> GetParam<T>(VolumeComponent c, string name)
        => c.GetType().GetField(name)?.GetValue(c) as VolumeParameter<T>;

    static VolumeComponent FindClouds()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[스텝] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[스텝] 구름 오버라이드 없음");
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
            for (int i = 0; i < 6; i++) cam.Render();   // 히스토리를 현재 설정으로 채운다
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
