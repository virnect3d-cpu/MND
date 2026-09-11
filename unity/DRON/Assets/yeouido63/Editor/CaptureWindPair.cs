// 시간을 달리한 두 장을 찍어 바람 움직임을 검증한다
//
// 메뉴: Tools/Yeouido 63/바람 검증 캡처
//
// 왜 필요한가
//   잔디가 흔들리는지, 어느 방향으로 눕는지는 정지 화면 한 장으로는
//   알 수 없다. 값이 맞는 것과 화면이 맞는 건 다른 문제라 픽셀로
//   확인해야 하는데, 움직임은 두 시점을 비교해야만 보인다.
//
// 에디터에서 시간을 어떻게 흘리나
//   비플레이 모드에서는 _Time 이 자동으로 흐르지 않는다. 셰이더가 쓰는
//   시간은 Shader.SetGlobalVector("_Time", ...) 로 직접 밀어 넣는다.
//   _Time = (t/20, t, t*2, t*3) 규약이라 그대로 만들어 준다.
//   파티클은 별도로 Simulate 로 원하는 시점까지 돌린다.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class CaptureWindPair
{
    const string Flag = "Temp/yeouido63_windpair.flag";
    const string OutDir = "Temp/Captures";

    // 두 컷의 시간 간격.
    //
    //   0.25 초(3.7 m 이동)로 잡았더니 상호상관에서 이동이 0 으로 나왔다.
    //   잔디 잎은 제자리에서 눕기만 하고 실제로 이동하는 건 "결"이라
    //   짧은 간격에서는 잎 하나하나의 떨림이 결의 이동을 덮어 버린다.
    //   위상 반주기(약 0.97 초)에 가깝게 벌려야 눕는 방향이 뒤집혀
    //   차이가 뚜렷해진다.
    const float T0 = 3.0f;
    const float T1 = 3.9f;

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[바람검증] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/바람 검증 캡처")]
    public static void Run()
    {
        Shot(T0, "wind_a");
        Shot(T1, "wind_b");

        // 셰이더 시간을 원래대로 — 안 되돌리면 씬 뷰의 잔디가 멈춘 채 남는다.
        Shader.SetGlobalVector("_Time", new Vector4(0, 0, 0, 0));
        Debug.Log($"[바람검증] 두 장 저장: wind_a(t={T0}) wind_b(t={T1}), 간격 {T1 - T0}초");
    }

    static void Shot(float t, string name)
    {
        Shader.SetGlobalVector("_Time", new Vector4(t / 20f, t, t * 2f, t * 3f));
        Capture(t, name);
    }

    static void Capture(float t, string name)
    {
        var cam = Camera.main;
        if (cam == null)
            cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                        .FirstOrDefault(c => c.enabled && c.targetTexture == null);
        if (cam == null) { Debug.LogError("[바람검증] 카메라를 못 찾았다."); return; }

        const int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };

        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            ps.Simulate(t, true, true);

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

            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);

            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            { ps.Clear(true); ps.Play(true); }
        }
    }
}
