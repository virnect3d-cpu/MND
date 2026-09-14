// 구름이 움직일 때 생기는 아지랑이(어른거림)의 원인을 가른다
//
// 메뉴: Tools/Yeouido 63/구름 아지랑이 진단
//
// 증상
//   구름이 흐르면 가장자리가 잘게 어른거린다. 정지 화면 한 장으로는
//   안 보이고 움직일 때만 보이는 종류라, 캡처 한 장으로는 진단이 안 된다.
//
// 무엇을 재나
//   구름을 실제로 흘려보내면서 연속 프레임을 찍고, 이웃 프레임 사이의
//   픽셀 변화를 센다. 구름이 천천히 흐르는 중이니 "조금씩 부드럽게"
//   변하는 게 정상이다. 아지랑이는 그 위에 얹힌 고주파 깜빡임이라
//   **같은 픽셀이 밝아졌다 어두워졌다를 반복**하는 형태로 나타난다.
//
//   그래서 두 가지를 따로 센다.
//     흐름(drift)  : 연속 두 프레임의 평균 절대차. 구름이 흐르면 자연히 는다.
//     깜빡임(flip) : 세 프레임 A,B,C 에서 B 가 A 와 C 사이를 벗어난 픽셀 수.
//                    부드럽게 흐르면 B 는 A 와 C 의 중간쯤에 있다. 거기서
//                    튀어나오면 그건 흐름이 아니라 떨림이다.
//
//   깜빡임 비율이 후보들 사이에서 크게 갈리면 그게 원인이다.
//
// 무엇을 시험하나
//   기준 / 시간누적 off / 시간누적 절반 / 전체 해상도 네 가지를 각각
//   같은 방식으로 잰다. 하나만 확 떨어지면 범인이 잡힌다.
//
// 유력한 가설 (셰이더를 읽고 세운 것)
//   VolumetricClouds.shader 의 재투영 패스(:412~441)는 모션 벡터를
//   **카메라 행렬만으로** 만든다. 구름 자체가 흐르는 건 벡터에 안 들어간다.
//   카메라가 서 있으면 velocity 가 0 이라 히스토리를 같은 픽셀에서
//   읽는데(:435), 그 픽셀의 구름은 이미 흘러가 버렸다. 틀린 색을 가져온
//   뒤 이웃 4 픽셀 박스로 clamp(:439) 하니, 같은 픽셀이 프레임마다
//   밝아졌다 어두워졌다 한다. 그게 아지랑이의 모양이다.
//
//   이 구조가 맞다면 temporalAccumulationFactor 를 낮출수록 깜빡임이
//   줄어야 한다. 히스토리 기여분이 줄기 때문이다.
//
// 예전 측정과 헷갈리지 말 것
//   THIRD_PARTY.md 에 "temporalAccumulationFactor 0.95 -> 0: 변화 없음"
//   이 적혀 있는데, 그건 **누수** 지표다. 정지 화면에서 탑 내부 픽셀이
//   바뀌는 비율을 재는 거라 시간누적과 애초에 상관이 없다. 여기서 재는
//   깜빡임과는 다른 값이다. 저 줄을 근거로 시간누적을 배제하면 안 된다.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProbeShimmer
{
    const string OutDir = Yeouido63Paths.CaptureDir;
    const string CloudMat = "Assets/VolumetricClouds/VolumetricClouds.mat";
    const string StaticNoise = "_CLOUD_STATIC_NOISE";
    const int    W = 960, H = 540;
    const float  ZoomFov = 22f;

    // 몇 프레임을 이어 찍나. 깜빡임은 세 장이 한 조라 홀수로 잡는다.
    const int Frames = 9;

    // 프레임 사이에 구름을 얼마나 흘릴까(초). 실제 드리프트 속도로는
    // 몇 분을 기다려야 눈에 띄므로, 진단에서는 오프셋을 직접 민다.
    const float StepMeters = 3.0f;

    [DidReloadScripts]
    static void OnReload()
        => AutoRunFlag.Consume("shimmer", "아지랑이", ProbeShimmer.Run, exitPlay: true);

    [MenuItem("Tools/Yeouido 63/구름 아지랑이 진단")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[아지랑이] Camera.main 이 없다"); return; }

        var clouds = FindClouds();
        if (clouds == null) return;

        // shapeOffset 은 Vector3Parameter, temporal 은 ClampedFloatParameter 다.
        // 둘 다 패키지 소스에서 확인한 타입이다(VolumetricCloudsVolume.cs:117, :277).
        var offset = Param<Vector3Parameter>(clouds, "shapeOffset");
        var temporal = Param<ClampedFloatParameter>(clouds, "temporalAccumulationFactor");
        if (offset == null) { Debug.LogError("[아지랑이] shapeOffset 을 못 잡았다"); return; }
        if (temporal == null) { Debug.LogError("[아지랑이] temporalAccumulationFactor 를 못 잡았다"); return; }

        // 해상도는 렌더러 피처의 private 필드라 프로퍼티로 접근한다.
        var feature = FindCloudsFeature();
        var resProp = feature?.GetType().GetProperty("ResolutionScale");

        var baseOffset   = offset.value;
        var baseOffsetOv = offset.overrideState;
        var baseTemporal = temporal.value;
        var baseTempOv   = temporal.overrideState;
        float baseRes    = resProp != null ? (float)resProp.GetValue(feature) : -1f;
        float prevFov    = cam.fieldOfView;

        Directory.CreateDirectory(OutDir);

        try
        {
            cam.fieldOfView = ZoomFov;

            Debug.Log($"[아지랑이] 시간누적 실효값 {baseTemporal:F2} " +
                      $"(오버라이드 {(baseTempOv ? "켬" : "꺼짐 — 패키지 기본값 0.95 가 쓰인다")})");

            // 실제 드리프트는 아주 느리다(0.025). 큰 스텝으로만 재면
            // 느린 흐름에서만 나오는 현상을 통째로 놓친다. 미세 스텝을
            // 같이 잰다.
            Measure("기준 미세스텝", cam, offset, baseOffset, step: 0.05f);

            Measure("기준", cam, offset, baseOffset);

            temporal.value = 0f; temporal.overrideState = true;
            Measure("시간누적 0", cam, offset, baseOffset);

            temporal.value = 0.5f; temporal.overrideState = true;
            Measure("시간누적 0.5", cam, offset, baseOffset);

            temporal.value = baseTemporal; temporal.overrideState = baseTempOv;

            if (resProp != null)
            {
                resProp.SetValue(feature, 1.0f);
                EditorUtility.SetDirty(feature);
                Measure("전체 해상도", cam, offset, baseOffset);
                resProp.SetValue(feature, baseRes);
                EditorUtility.SetDirty(feature);
            }
            else Debug.LogWarning("[아지랑이] 해상도 프로퍼티를 못 잡아 그 항목은 건너뛴다");

            // 카메라를 같이 움직이면 재투영이 velocity 를 얻는다. 가설이
            // 맞다면 이때 깜빡임이 눈에 띄게 줄어야 한다 — 히스토리를
            // 제대로 된 자리에서 당겨오기 때문이다. 안 줄면 원인은
            // 모션 벡터가 아니라 다른 데 있다.
            Measure("기준+카메라 이동", cam, offset, baseOffset, panDegrees: 0.05f);

            // 카메라만 움직이고 구름은 세운다. 위 6% 가 "구름+카메라"
            // 합작인지 카메라 단독인지 가른다. 여기서도 높게 나오면
            // 구름 흐름과 무관한, 카메라 모션에 딸린 현상이다.
            Measure("카메라만 이동", cam, offset, baseOffset,
                    panDegrees: 0.05f, step: 0f);

            // 시간누적을 끄고 카메라를 움직인다. 카메라 모션 쪽 떨림이
            // 재투영 때문인지 보는 마지막 갈림길이다.
            temporal.value = 0f; temporal.overrideState = true;
            Measure("카메라 이동+시간누적 0", cam, offset, baseOffset,
                    panDegrees: 0.05f, step: 0f);
            temporal.value = baseTemporal; temporal.overrideState = baseTempOv;

            // 레이마치 지터의 시간 항을 죽인다. 여기서 깜빡임이 떨어지면
            // 원인은 시작점 노이즈가 맞다.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CloudMat);
            if (mat != null)
            {
                bool had = mat.IsKeywordEnabled(StaticNoise);
                mat.EnableKeyword(StaticNoise);
                Measure("정지 노이즈 미세스텝", cam, offset, baseOffset, step: 0.05f);
                Measure("정지 노이즈+카메라 이동", cam, offset, baseOffset,
                        panDegrees: 0.05f, step: 0f);
                if (!had) mat.DisableKeyword(StaticNoise);
            }
            else Debug.LogWarning($"[아지랑이] 구름 머티리얼을 못 찾았다: {CloudMat}");

            Debug.Log("[아지랑이] 깜빡임이 확 낮아지는 항목이 원인이다. " +
                      "어느 것도 안 낮아지면 시간누적/해상도가 아니라 다른 곳이다.");
        }
        finally
        {
            offset.value = baseOffset; offset.overrideState = baseOffsetOv;
            temporal.value = baseTemporal; temporal.overrideState = baseTempOv;
            if (resProp != null && baseRes >= 0f)
            {
                resProp.SetValue(feature, baseRes);
                EditorUtility.SetDirty(feature);
            }
            cam.fieldOfView = prevFov;
        }
    }

    // 구름을 한 칸씩 밀면서 연속 프레임을 찍고 흐름/깜빡임을 센다.
    //   panDegrees 가 0 이 아니면 카메라도 프레임마다 그만큼 돌린다.
    static void Measure(string label, Camera cam,
                        Vector3Parameter offset, Vector3 baseOffset,
                        float panDegrees = 0f, float step = StepMeters)
    {
        var shots = new Color32[Frames][];
        var baseRot = cam.transform.rotation;
        try
        {
            for (int i = 0; i < Frames; i++)
            {
                offset.value = baseOffset + new Vector3(step * i, 0f, 0f);
                offset.overrideState = true;
                if (panDegrees != 0f)
                    cam.transform.rotation = baseRot * Quaternion.Euler(0f, panDegrees * i, 0f);
                shots[i] = Shot(cam, $"shimmer_{Slug(label)}_{i}", i == 0 || i == Frames - 1);
                if (shots[i] == null) { Debug.LogError($"[아지랑이] {label}: 캡처 실패"); return; }
            }

            long driftSum = 0; long driftCount = 0;
            long flip = 0; long flipTotal = 0;

            for (int i = 1; i < Frames; i++)
            {
                var a = shots[i - 1]; var b = shots[i];
                for (int p = 0; p < a.Length; p++)
                {
                    int d = Diff(a[p], b[p]);
                    driftSum += d; driftCount++;
                }
            }

            // 깜빡임: B 가 A 와 C 의 구간 밖으로 얼마나 튀는지.
            //   부드러운 흐름이면 B 는 A~C 사이에 있다. 여유(Slack)를 둬서
            //   양자화 잡음까지 깜빡임으로 세지 않게 한다.
            const int Slack = 6;
            for (int i = 1; i < Frames - 1; i++)
            {
                var a = shots[i - 1]; var b = shots[i]; var c = shots[i + 1];
                for (int p = 0; p < b.Length; p++)
                {
                    int la = Luma(a[p]), lb = Luma(b[p]), lc = Luma(c[p]);
                    int lo = Mathf.Min(la, lc) - Slack;
                    int hi = Mathf.Max(la, lc) + Slack;
                    flipTotal++;
                    if (lb < lo || lb > hi) flip++;
                }
            }

            double drift = driftCount > 0 ? (double)driftSum / driftCount : 0;
            double flipPct = flipTotal > 0 ? 100.0 * flip / flipTotal : 0;

            Debug.Log($"[아지랑이] {label} — 깜빡임 {flipPct:F2}% , 흐름 평균차 {drift:F2}");
        }
        finally
        {
            // 카메라를 돌렸으면 반드시 되돌린다. 중간에 던지면 씬 카메라가
            // 돌아간 채 남아서 이후 캡처가 전부 엉뚱한 곳을 본다.
            cam.transform.rotation = baseRot;
            // Color32[] 라 해제할 네이티브 자원은 없다. 명시적으로 놓아 준다.
            for (int i = 0; i < Frames; i++) shots[i] = null;
        }
    }

    static int Diff(Color32 a, Color32 b)
        => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    static int Luma(Color32 c) => (c.r * 299 + c.g * 587 + c.b * 114) / 1000;

    static string Slug(string s) => s.Replace(" ", "_").Replace(".", "");

    // 픽셀만 필요하므로 Texture2D 를 들고 다니지 않는다. 9 장을 텍스처로
    // 쥐고 있으면 에디터 메모리에 그대로 쌓인다.
    static Color32[] Shot(Camera cam, string name, bool savePng)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            cam.targetTexture = rt;
            // 시간누적을 쓰는 패스라 히스토리가 자리잡을 때까지 여러 번 그린다.
            for (int i = 0; i < 8; i++) cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            if (savePng)
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            return tex.GetPixels32();
        }
        finally
        {
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
            if (tex != null) Object.DestroyImmediate(tex);
        }
    }

    static T Param<T>(VolumeComponent c, string name) where T : class
        => c.GetType().GetField(name)?.GetValue(c) as T;

    static VolumeComponent FindClouds()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Yeouido63Paths.Post);
        if (post == null) { Debug.LogError("[아지랑이] 프로파일 없음"); return null; }
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") return c;
        Debug.LogError("[아지랑이] 구름 오버라이드 없음");
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
}
