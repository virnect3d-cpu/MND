// Game View 를 PNG 로 떠서 눈으로 검증한다
//
// 메뉴: Tools/Yeouido 63/화면 캡처
//
// 왜 필요한가
//   컴포넌트 값이 맞다고 화면이 맞는 건 아니다. 렌더러 피처가 켜져 있어도
//   패스가 실제로 그려지는지, 황사가 보이는지는 픽셀을 봐야 안다.
//
// 어떻게 뜨나
//   에디터에서 Game View 를 직접 읽는 건 타이밍이 까다롭다. 대신 씬의 메인
//   카메라를 RenderTexture 로 한 번 렌더한다. URP 렌더러 피처와 볼륨이 모두
//   적용된 결과가 나오므로 검증 목적에는 충분하다.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class CaptureGameView
{
    const string Flag = "Temp/yeouido63_capture.flag";
    const string OutDir = "Temp/Captures";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Capture(); }
            catch (System.Exception e) { Debug.LogError("[캡처] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/화면 캡처")]
    public static void Capture()
    {
        var cam = Camera.main;
        if (cam == null)
            cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                        .FirstOrDefault(c => c.enabled && c.targetTexture == null);
        if (cam == null) { Debug.LogError("[캡처] 카메라를 못 찾았다."); return; }

        const int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1     // 결과를 읽어야 하므로 MSAA RT 는 피한다
        };

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
            string path = Path.Combine(OutDir, "gameview.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            Debug.Log($"[캡처] 저장: {Path.GetFullPath(path)}  카메라={cam.name} pos={cam.transform.position}");
        }
        finally
        {
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
