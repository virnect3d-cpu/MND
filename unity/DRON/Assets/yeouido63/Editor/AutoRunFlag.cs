// 파일 플래그로 에디터 작업을 자동 실행한다
//
// 무엇인가
//   Unity 에디터 바깥(터미널, 스크립트)에서 `Temp/yeouido63_*.flag` 파일을
//   만들고 에디터에 포커스를 주면, 스크립트 리로드가 돌면서 해당 작업이
//   실행된다. 메뉴를 손으로 누르지 않고 셋업·캡처를 돌리는 통로다.
//
//   리포 안에는 이 파일을 만드는 코드가 없다. 그게 정상이다 — 쓰는 쪽이
//   에디터 바깥이라서다. "아무도 안 쓰니 죽은 코드" 로 오해하기 쉬운데
//   지우면 외부 자동화가 통째로 끊긴다.
//
// 왜 헬퍼로 모았나
//   같은 7 줄이 28 곳에 복붙돼 있었다. 본문은 한 글자도 안 달랐는데
//   예외 처리만 제각각이라, 한 곳은 try/catch 가 아예 없어서 File.Delete
//   가 던지면 도메인 리로드 콜백 체인으로 예외가 새어 나갔다.
//
//   플래그를 먼저 지우고 작업을 돌리는 순서가 중요하다. 반대로 하면
//   작업이 던졌을 때 플래그가 남아 리로드마다 무한 재시도가 된다.
//
// 플레이 모드
//   씬 저장은 플레이 중에 거부당한다. 예전엔 그것만 따로 처리하는
//   스크립트(ExitPlayAndRun)가 있었는데, 플래그를 쓰는 작업 대부분이
//   같은 문제를 겪어서 여기로 들여왔다. ExitPlay = true 면 플레이를 끄고
//   몇 프레임 기다렸다가 실행한다.
//
//   기다리는 이유: 플레이 종료 직후에는 도메인 리로드와 씬 되돌리기가
//   아직 돌고 있다. 그 틈에 오브젝트를 지우고 새로 심으면 삭제만 남고
//   생성이 날아간다. 실제로 먼지 3 층이 통째로 사라진 채 저장된 적이 있다.

using System.IO;
using UnityEditor;
using UnityEngine;

public static class AutoRunFlag
{
    /// <summary>플래그 이름("dustfx")을 전체 경로로 만든다.</summary>
    public static string Path(string name) => $"Temp/yeouido63_{name}.flag";

    /// <summary>
    /// 플래그가 있으면 지우고 작업을 예약한다. 없으면 아무 일도 안 한다.
    /// [DidReloadScripts] 안에서 한 줄로 부르면 된다.
    /// </summary>
    /// <param name="name">플래그 이름. "yeouido63_" 접두사와 ".flag" 는 뺀다.</param>
    /// <param name="label">로그 태그. 실패했을 때 어느 작업인지 알아보려고 쓴다.</param>
    /// <param name="run">실행할 작업.</param>
    /// <param name="exitPlay">플레이 모드면 끄고 정착을 기다린 뒤 실행한다.</param>
    public static void Consume(string name, string label, System.Action run,
                               bool exitPlay = false)
    {
        var path = Path(name);
        if (!File.Exists(path)) return;

        // 먼저 지운다. 작업이 던져도 플래그는 사라져서 재시도 루프가 안 생긴다.
        try { File.Delete(path); }
        catch (System.Exception e)
        {
            Debug.LogError($"[{label}] 플래그를 못 지웠다. 그냥 두면 리로드마다 " +
                           $"다시 돌아서 실행을 건너뛴다: {e.Message}");
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (exitPlay && (EditorApplication.isPlaying ||
                             EditorApplication.isPlayingOrWillChangePlaymode))
            {
                Debug.Log($"[{label}] 플레이 모드다. 끄고 나서 진행한다.");
                EditorApplication.isPlaying = false;
                WaitThenRun(label, run);
                return;
            }
            Invoke(label, run);
        };
    }

    static void Invoke(string label, System.Action run)
    {
        try { run(); }
        catch (System.Exception e) { Debug.LogError($"[{label}] 실패: " + e); }
    }

    // 플레이가 끝난 뒤 몇 프레임 더 기다린다. isPlaying 만 보고 바로
    // 진행했더니 먼지 오브젝트가 통째로 날아간 채 저장된 적이 있다.
    const int SettleFrames = 10;

    static void WaitThenRun(string label, System.Action run)
    {
        int settle = 0;
        void Tick()
        {
            if (EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                settle = 0;
                return;
            }

            if (settle++ < SettleFrames) return;

            EditorApplication.update -= Tick;
            EditorApplication.delayCall += () => Invoke(label, run);
        }
        EditorApplication.update += Tick;
    }
}
