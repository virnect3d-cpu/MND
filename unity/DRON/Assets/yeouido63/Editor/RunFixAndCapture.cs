// 카메라 포스트를 켜고 화면을 캡처한다
//
//   플래그 파일이 있으면 리컴파일 직후 한 번 돈다.
//   "컴포넌트 값만 보고 됐다고 하지 말고 픽셀로 확인" 을 강제하는 장치다.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class RunFixAndCapture
{
    const string Flag = "Temp/yeouido63_fixcam.flag";
    const string ProbeFlag = "Temp/yeouido63_probe.flag";

    [DidReloadScripts]
    static void OnProbe()
    {
        if (!File.Exists(ProbeFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(ProbeFlag); ProbeClouds.Run(); }
            catch (System.Exception e) { Debug.LogError("[구름] 실패: " + e); }
        };
    }

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                File.Delete(Flag);
                FixCameraPost.Run();
                // 포스트 파이프라인이 한 프레임 뒤에 붙으므로 캡처를 미룬다.
                EditorApplication.delayCall += CaptureGameView.Capture;
            }
            catch (System.Exception e) { Debug.LogError("[카메라] 실패: " + e); }
        };
    }
}
// touch 1789342604
