// 플레이 모드를 끄고 나서 먼지/캡처 스크립트를 돌린다
//
// 왜 필요한가
//   SetupDustParticles 는 EditorSceneManager.SaveScene 을 부르는데, 플레이
//   모드에서는 "This cannot be used during play mode" 로 거부당한다. 실제로
//   그렇게 한 번 실패했고, 로그에는 직전 실행의 성공 메시지가 남아 있어서
//   적용된 것처럼 착각하기 쉬웠다.
//
//   EditorApplication.isPlaying = false 는 즉시 반영되지 않는다. 다음
//   프레임 이후에 빠져나오므로 update 콜백으로 기다렸다가 실행해야 한다.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class ExitPlayAndRun
{
    const string Flag = "Temp/yeouido63_exitplay.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        File.Delete(Flag);
        EditorApplication.delayCall += Go;
    }

    [MenuItem("Tools/Yeouido 63/플레이 끄고 먼지 적용")]
    public static void Go()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.Log("[적용] 플레이 모드다. 끄고 나서 진행한다.");
            EditorApplication.isPlaying = false;
            EditorApplication.update += WaitThenRun;
            return;
        }
        Run();
    }

    // 플레이가 끝난 뒤 몇 프레임 더 기다린다.
    //
    //   isPlaying 만 보고 바로 진행했더니 씬에서 먼지 오브젝트 4 개가
    //   통째로 사라진 채 저장된 적이 있다. 플레이 종료 직후에는 도메인
    //   리로드와 씬 되돌리기가 아직 돌고 있어서, 그 틈에 Setup 이
    //   기존 오브젝트를 지우고 새로 심으면 삭제만 남고 생성이 날아간다.
    //
    //   isPlayingOrWillChangePlaymode 까지 확인하고, 그 뒤로도 몇 프레임
    //   여유를 둔다.
    const int SettleFrames = 10;
    static int _settle;

    static void WaitThenRun()
    {
        if (EditorApplication.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            _settle = 0;
            return;
        }

        if (_settle++ < SettleFrames) return;

        EditorApplication.update -= WaitThenRun;
        _settle = 0;
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        SetupDustParticles.Setup();

        // 심은 결과를 확인한다. 한 번 통째로 날아간 적이 있어서,
        // 조용히 실패하면 다음 커밋에 "삭제" 로 남는다.
        var root = GameObject.Find("DUST_Particles");
        int layers = root == null ? 0 : root.GetComponentsInChildren<ParticleSystem>(true).Length;
        if (layers < 3)
        {
            Debug.LogError($"[적용] 먼지가 제대로 안 심겼다 (층 {layers}/3). " +
                           "씬을 저장하지 말고 Tools/Yeouido 63 에서 다시 돌려라.");
            return;
        }

        // 먼지를 심은 뒤에 동기화한다. SyncWind 가 먼지 값을 기준으로
        // 잔디/구름을 맞추므로 순서가 바뀌면 한 박자 늦은 값을 읽는다.
        SyncWind.Run();
        // 심은 직후 바로 찍는다. 한 번의 리프레시로 적용과 검증이 끝난다.
        EditorApplication.delayCall += CaptureGameView.Capture;
    }
}
