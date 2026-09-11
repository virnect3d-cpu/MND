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

    static void WaitThenRun()
    {
        if (EditorApplication.isPlaying) return;   // 아직 빠져나오는 중
        EditorApplication.update -= WaitThenRun;
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        SetupDustParticles.Setup();
        // 먼지를 심은 뒤에 동기화한다. SyncWind 가 먼지 값을 기준으로
        // 잔디/구름을 맞추므로 순서가 바뀌면 한 박자 늦은 값을 읽는다.
        SyncWind.Run();
        // 심은 직후 바로 찍는다. 한 번의 리프레시로 적용과 검증이 끝난다.
        EditorApplication.delayCall += CaptureGameView.Capture;
    }
}
