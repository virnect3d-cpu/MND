// 볼류메트릭 구름이 화면에 실제로 기여하는지 픽셀로 판정한다
//
// 메뉴: Tools/Yeouido 63/구름 기여 확인
//
// 왜 필요한가
//   하늘에 구름이 보인다고 해서 볼류메트릭 구름이 도는 건 아니다.
//   스카이박스 텍스처에도 구름이 그려져 있어서 육안으로는 구분이 안 간다.
//   실제로 카메라 포스트프로세싱이 꺼져 있던 동안에도 하늘엔 구름이
//   있었다 — 그게 전부 스카이박스였다.
//
//   구름 state 오버라이드를 껐다 켜고 같은 프레임을 두 장 떠서
//   픽셀이 달라지는지 본다. 안 달라지면 볼륨 구름은 기여가 0 이다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProbeClouds
{
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string OutDir   = "Temp/Captures";

    [MenuItem("Tools/Yeouido 63/구름 기여 확인")]
    public static void Run()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null) { Debug.LogError("[구름] 프로파일 없음"); return; }

        VolumeComponent clouds = null;
        foreach (var c in post.components)
            if (c != null && c.GetType().Name == "VolumetricClouds") { clouds = c; break; }
        if (clouds == null) { Debug.LogError("[구름] 오버라이드 없음"); return; }

        var stateField = clouds.GetType().GetField("state");
        if (stateField == null) { Debug.LogError("[구름] state 필드 없음"); return; }
        var state = stateField.GetValue(clouds) as VolumeParameter<bool>;
        if (state == null)
        {
            // bool 이 아니라 enum 계열일 수 있다. 타입을 찍어서 단서를 남긴다.
            Debug.LogError($"[구름] state 타입이 예상과 다르다: " +
                           $"{stateField.GetValue(clouds)?.GetType().FullName}");
            return;
        }

        bool original = state.value;
        Directory.CreateDirectory(OutDir);

        state.value = true;  state.overrideState = true;
        var on  = Shot();

        state.value = false; state.overrideState = true;
        var off = Shot();

        state.value = original; state.overrideState = true;

        if (on == null || off == null) { Debug.LogError("[구름] 캡처 실패"); return; }
        Compare(on, off);
        Object.DestroyImmediate(on);
        Object.DestroyImmediate(off);
    }

    // 게임 뷰가 아니라 오프스크린으로 직접 렌더한다. 게임 뷰 캡처는
    // 에디터 리페인트 타이밍에 묶여서 두 장이 같은 프레임을 보장 못 한다.
    static Texture2D Shot()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[구름] Camera.main 이 없다"); return null; }

        var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.DefaultHDR);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;

        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static void Compare(Texture2D on, Texture2D off)
    {
        var a = on.GetPixels32();
        var b = off.GetPixels32();

        long diffSum = 0;
        int  diffPx  = 0, maxDiff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Mathf.Abs(a[i].r - b[i].r)
                  + Mathf.Abs(a[i].g - b[i].g)
                  + Mathf.Abs(a[i].b - b[i].b);
            if (d > 2) { diffPx++; diffSum += d; }   // 2 는 디더링 노이즈 무시용
            if (d > maxDiff) maxDiff = d;
        }

        float pct = 100f * diffPx / a.Length;
        Debug.Log($"[구름] 켬/끔 픽셀 비교 — 다른 픽셀 {diffPx:N0} / {a.Length:N0} ({pct:F2}%), " +
                  $"최대 차이 {maxDiff}, 평균 차이 {(diffPx > 0 ? (float)diffSum / diffPx : 0f):F1}\n" +
                  (pct < 0.5f
                    ? "-> 기여 없음. 볼류메트릭 구름은 화면에 안 나온다. 하늘 구름은 스카이박스다."
                    : "-> 기여 있음. 볼류메트릭 구름이 실제로 그려지고 있다."));

        File.WriteAllBytes($"{OutDir}/clouds_on.png",  on.EncodeToPNG());
        File.WriteAllBytes($"{OutDir}/clouds_off.png", off.EncodeToPNG());
        Debug.Log($"[구름] 비교 이미지 저장: {OutDir}/clouds_on.png, clouds_off.png");
    }
}
