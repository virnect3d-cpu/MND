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
    const string TuneFlag = "Temp/yeouido63_tune.flag";

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

    // 대기 튜닝 + 먼지 재배치 + 캡처를 한 번에.
    //   먼지는 값이 파티클 시스템에 구워져 있어서 다시 심어야 반영된다.
    [DidReloadScripts]
    static void OnTune()
    {
        if (!File.Exists(TuneFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                File.Delete(TuneFlag);
                TuneAtmosphere.Run();
                SetupDustParticles.Setup();

                // 먼지가 제대로 심겼는지 확인하고 넘어간다. 예전에 도메인
                // 리로드와 겹쳐서 삭제만 저장되고 재생성이 날아간 적이 있다.
                var root = GameObject.Find("DUST_Particles");
                int layers = root == null
                    ? 0 : root.GetComponentsInChildren<ParticleSystem>(true).Length;
                if (layers < 3)
                {
                    Debug.LogError($"[대기] 먼지가 제대로 안 심겼다 (층 {layers}/3). 캡처 취소.");
                    return;
                }

                EditorApplication.delayCall += CaptureGameView.Capture;
            }
            catch (System.Exception e) { Debug.LogError("[대기] 실패: " + e); }
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
