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
    const string EdgeFlag = "Temp/yeouido63_edge.flag";
    const string SplitFlag = "Temp/yeouido63_split.flag";
    const string ScoreFlag = "Temp/yeouido63_score.flag";
    const string StepFlag = "Temp/yeouido63_step.flag";
    const string OrderFlag = "Temp/yeouido63_order.flag";
    const string LeakFlag = "Temp/yeouido63_leak.flag";
    const string MsaaFlag = "Temp/yeouido63_msaa.flag";

    [DidReloadScripts]
    static void OnMsaa()
    {
        if (!File.Exists(MsaaFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(MsaaFlag); ProbeLeakMsaa.Run(); }
            catch (System.Exception e) { Debug.LogError("[MSAA] 실패: " + e); }
        };
    }

    [DidReloadScripts]
    static void OnLeak()
    {
        if (!File.Exists(LeakFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(LeakFlag); ProbeOrderFix.Run(); }
            catch (System.Exception e) { Debug.LogError("[누수] 실패: " + e); }
        };
    }

    [DidReloadScripts]
    static void OnOrder()
    {
        if (!File.Exists(OrderFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(OrderFlag); ProbeCloudOrder.Run(); }
            catch (System.Exception e) { Debug.LogError("[순서] 실패: " + e); }
        };
    }

    [DidReloadScripts]
    static void OnStep()
    {
        if (!File.Exists(StepFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(StepFlag); ProbeCloudSteps.Run(); }
            catch (System.Exception e) { Debug.LogError("[스텝] 실패: " + e); }
        };
    }

    [DidReloadScripts]
    static void OnScore()
    {
        if (!File.Exists(ScoreFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(ScoreFlag); ScoreCloudFix.Run(); }
            catch (System.Exception e) { Debug.LogError("[점수] 실패: " + e); }
        };
    }

    // 구름 깨짐 원인 분리 (해상도 / 시간누적)
    [DidReloadScripts]
    static void OnSplit()
    {
        if (!File.Exists(SplitFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(SplitFlag); ProbeCloudFix.Run(); }
            catch (System.Exception e) { Debug.LogError("[분리] 실패: " + e); }
        };
    }

    // 먼지 재배치 + 구름 경계 진단.
    [DidReloadScripts]
    static void OnEdge()
    {
        if (!File.Exists(EdgeFlag)) return;
        EditorApplication.delayCall += () =>
        {
            try
            {
                File.Delete(EdgeFlag);
                // 먼저 진단부터. SetupDustParticles 는 기존 먼지를 지웠다
                // 다시 만드는데, 그 파괴가 같은 프레임에 섞이면 진단 쪽에서
                // 죽은 오브젝트를 참조해 MissingReferenceException 이 난다.
                ProbeCloudEdge.Run();
                EditorApplication.delayCall += () =>
                {
                    SetupDustParticles.Setup();
                    EditorApplication.delayCall += CaptureGameView.Capture;
                };
            }
            catch (System.Exception e) { Debug.LogError("[경계] 실패: " + e); }
        };
    }

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
// touch 1789345450
