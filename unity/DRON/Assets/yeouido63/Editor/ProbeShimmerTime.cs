// 아지랑이 하네스가 시간을 실제로 흘리는지 확인한다
//
// 메뉴: Tools/Yeouido 63/아지랑이 하네스 점검
//
// 왜 필요한가
//   ProbeShimmer 는 cam.Render() 를 반복해 프레임을 뽑는다. 그런데
//   구름 지터는 _Time.y 로 흔들린다(VolumetricCloudsUtilities.hlsl).
//   에디터에서 cam.Render() 를 아무리 불러도 _Time.y 가 안 흐르면
//   시간 기반 노이즈는 애초에 변하지 않는다. 그러면 "시간 노이즈를
//   껐는데 효과 없음" 이라는 결과가 나와도 그건 원인이 아니라는 뜻이
//   아니라 **측정이 불가능했다** 는 뜻이다.
//
//   실제로 정지 노이즈 실험이 2.78% -> 2.72% 로 무변화였다. 원인이
//   아니어서인지, 시간이 안 흘러서인지 지금은 구분이 안 된다.
//
// 무엇을 하나
//   같은 카메라, 같은 설정으로 아무것도 안 바꾸고 연속 캡처만 한다.
//   시간이 흐르고 지터가 프레임마다 바뀐다면 두 장이 달라야 한다.
//   완전히 같으면 하네스가 시간을 못 흘리는 것이고, ProbeShimmer 의
//   시간 관련 결론은 전부 무효다.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class ProbeShimmerTime
{
    const int W = 640, H = 360;
    // 수정 후 재측정 (디더링 OFF 적용 확인됨: 바닥 떨림 72% -> 7%)

    [DidReloadScripts]
    static void OnReload()
        => AutoRunFlag.Consume("shimtime", "하네스", ProbeShimmerTime.Run, exitPlay: true);

    [MenuItem("Tools/Yeouido 63/아지랑이 하네스 점검")]
    public static void Run()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[하네스] Camera.main 이 없다"); return; }

        Debug.Log($"[하네스] 시작 시점 Time.realtimeSinceStartup={Time.realtimeSinceStartup:F3}, " +
                  $"Time.time={Time.time:F3}, isPlaying={EditorApplication.isPlaying}");

        // 아무것도 안 바꾸고 세 장을 찍는다. 셰이더가 시간으로 흔들린다면
        // 이것만으로도 달라야 한다.
        var a = Shot(cam);
        var b = Shot(cam);
        var c = Shot(cam);
        if (a == null || b == null || c == null) { Debug.LogError("[하네스] 캡처 실패"); return; }

        long ab = 0, ac = 0;
        int abPix = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d1 = Diff(a[i], b[i]);
            int d2 = Diff(a[i], c[i]);
            ab += d1; ac += d2;
            if (d1 != 0) abPix++;
        }

        double meanAb = (double)ab / a.Length;
        double meanAc = (double)ac / a.Length;
        double pctChanged = 100.0 * abPix / a.Length;

        Debug.Log($"[하네스] 끝 시점 Time.time={Time.time:F3}");
        Debug.Log($"[하네스] 아무것도 안 바꾼 연속 2 장 평균차 {meanAb:F3}, " +
                  $"바뀐 픽셀 {pctChanged:F2}%  (1-3 장 평균차 {meanAc:F3})");

        if (pctChanged < 0.01)
            Debug.LogWarning("[하네스] 두 장이 사실상 동일하다. 에디터에서 cam.Render() 로는 " +
                             "_Time.y 가 안 흐른다는 뜻이다. 시간 기반 지터는 이 하네스로 " +
                             "측정할 수 없다 — ProbeShimmer 의 '정지 노이즈' 결과는 무효다. " +
                             "플레이 모드에서 프레임을 실제로 돌려야 한다.");
        else
            Debug.Log("[하네스] 프레임이 실제로 달라진다. 시간 기반 지터를 측정할 수 있다.");

        // 여기부터가 핵심이다. 위에서 "가만히 있어도 화면이 바뀐다" 가
        // 나왔다면, 구름/카메라 이동과 무관한 떨림이 있다는 뜻이다.
        // 무엇이 그걸 만드는지 하나씩 끈다.
        Isolate();
    }

    // 정지 상태에서 후보를 하나씩 꺼 보고 바닥 노이즈가 줄어드는지 본다.
    //   구름도 카메라도 안 움직이므로, 여기서 줄어드는 항목이 곧
    //   아지랑이의 원인이다. 이동 조건과 섞지 않는 게 요점이다 —
    //   앞선 측정은 카메라를 움직인 채로 껐다 켜서 신호가 묻혔다.
    static void Isolate()
    {
        var post = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(
            Yeouido63Paths.Post);
        if (post == null) { Debug.LogError("[하네스] 프로파일 없음"); return; }

        UnityEngine.Rendering.VolumeComponent clouds = null;
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") { clouds = c; break; }
        if (clouds == null) { Debug.LogError("[하네스] 구름 오버라이드 없음"); return; }

        var temporal = clouds.GetType().GetField("temporalAccumulationFactor")
            ?.GetValue(clouds) as UnityEngine.Rendering.ClampedFloatParameter;
        if (temporal == null) { Debug.LogError("[하네스] 시간누적 파라미터 없음"); return; }

        float baseVal = temporal.value;
        bool baseOv = temporal.overrideState;
        try
        {
            Debug.Log($"[하네스] --- 정지 상태 바닥 노이즈 분리 ---");
            Baseline("시간누적 그대로");

            temporal.value = 0f; temporal.overrideState = true;
            Baseline("시간누적 0");

            temporal.value = 1f; temporal.overrideState = true;
            Baseline("시간누적 1");
        }
        finally
        {
            temporal.value = baseVal; temporal.overrideState = baseOv;
        }

        // 정지 상태에서 70% 픽셀이 떨린다면 구름만의 문제가 아닐 수 있다.
        // 이 씬에는 먼지 파티클 3 층과 볼류메트릭 안개(역시 레이마치)가
        // 있다. 하나씩 꺼서 범인을 가른다. 이걸 안 가르고 구름만 파면
        // 엉뚱한 데를 고치게 된다.
        Blame(clouds);
    }

    static void Blame(UnityEngine.Rendering.VolumeComponent clouds)
    {
        Debug.Log("[하네스] --- 무엇이 떠는지 분리 ---");
        var cam = Camera.main;
        var extra = cam.GetUniversalAdditionalCameraData();

        // 1) 구름 끄기
        bool cloudsWere = clouds.active;
        try
        {
            clouds.active = false;
            Baseline("구름 OFF");
        }
        finally { clouds.active = cloudsWere; }

        // 2) 먼지 파티클 끄기
        var dust = new System.Collections.Generic.List<GameObject>();
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            dust.Add(ps.gameObject);
        try
        {
            foreach (var g in dust) g.SetActive(false);
            Baseline($"먼지 OFF ({dust.Count} 개)");
        }
        finally { foreach (var g in dust) g.SetActive(true); }

        // 3) 구름 + 먼지 둘 다 끄기. 그래도 떨면 남은 건 안개나
        //    포스트 프로세싱 쪽이다.
        try
        {
            clouds.active = false;
            foreach (var g in dust) g.SetActive(false);
            Baseline("구름+먼지 OFF");
        }
        finally
        {
            clouds.active = cloudsWere;
            foreach (var g in dust) g.SetActive(true);
        }

        // 4) 포스트 프로세싱까지 끈다. 여기서도 70% 가 나오면 씬 내용과
        //    무관하다는 뜻이고, 그러면 떨림의 출처는 측정 하네스다
        //    (cam.Render() 반복이 URP 임시 버퍼를 매번 다른 상태로
        //    시작시키는 경우). 그때는 이 수치 전부를 버려야 한다.
        cam = Camera.main;
        extra = cam.GetUniversalAdditionalCameraData();
        bool hadPost = extra != null && extra.renderPostProcessing;
        try
        {
            if (extra != null) extra.renderPostProcessing = false;
            Baseline("포스트까지 OFF");
        }
        finally { if (extra != null) extra.renderPostProcessing = hadPost; }

        // 4b) 포스트 안에서 무엇이 떠는지 가른다.
        //     씬 카메라는 m_Antialiasing: 2 (= TAA), m_Dithering: 1 이다.
        //     TAA 는 매 프레임 투영을 서브픽셀만큼 흔들어 섞는 기법이라
        //     정지 화면에서도 화면 전체가 미세하게 흔들린다. 구름 같은
        //     부드러운 그라데이션 위에서 제일 잘 보인다.
        if (extra != null)
        {
            var hadAA = extra.antialiasing;
            bool hadDither = extra.dithering;
            try
            {
                extra.antialiasing = AntialiasingMode.None;
                Baseline("TAA OFF");

                extra.antialiasing = hadAA;
                extra.dithering = false;
                Baseline("디더링 OFF");

                extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                extra.dithering = hadDither;
                Baseline("SMAA 로 교체");
            }
            finally
            {
                extra.antialiasing = hadAA;
                extra.dithering = hadDither;
            }
        }

        // 5) 가장 순수한 대조군: 이 씬의 카메라가 아니라 임시 카메라로
        //    빈 하늘만 찍는다. 씬 오브젝트도, 이 카메라의 설정도 안 탄다.
        //    여기서도 떨면 하네스 문제가 확정이다.
        var probe = new GameObject("~ShimmerProbeCam");
        try
        {
            var c2 = probe.AddComponent<Camera>();
            c2.CopyFrom(Camera.main);
            c2.cullingMask = 0;                 // 아무 오브젝트도 안 그린다
            c2.clearFlags = CameraClearFlags.Skybox;
            var e2 = c2.GetUniversalAdditionalCameraData();
            if (e2 != null) e2.renderPostProcessing = false;
            BaselineWith(c2, "빈 카메라(스카이박스만)");
        }
        finally { Object.DestroyImmediate(probe); }
    }

    static void Baseline(string label) => BaselineWith(Camera.main, label);

    static void BaselineWith(Camera cam, string label)
    {
        var a = Shot(cam); var b = Shot(cam);
        if (a == null || b == null) { Debug.LogError($"[하네스] {label}: 캡처 실패"); return; }

        long sum = 0; int changed = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Diff(a[i], b[i]);
            sum += d;
            if (d != 0) changed++;
        }
        Debug.Log($"[하네스] {label} — 정지 평균차 {(double)sum / a.Length:F3}, " +
                  $"바뀐 픽셀 {100.0 * changed / a.Length:F2}%");
    }

    static int Diff(Color32 x, Color32 y)
        => Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b);

    static Color32[] Shot(Camera cam)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            cam.targetTexture = rt;
            for (int i = 0; i < 8; i++) cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
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
}
