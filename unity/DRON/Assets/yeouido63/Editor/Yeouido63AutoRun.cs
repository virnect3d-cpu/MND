// 스크립트 리로드 시 씬 셋업을 1회 자동 실행한다.
//
// 왜 필요한가
//   메뉴(Tools/Yeouido 63/씬 셋업)를 사람이 눌러야만 돌아가는데, 외부에서
//   유니티를 조작할 수단이 없을 때 막힌다. 플래그 파일이 있으면 리로드 직후
//   한 번 돌고 플래그를 지운다.
//
// 쓰는 법
//   1) 프로젝트 루트(Assets 옆)에 `Temp/yeouido63_run.flag` 파일을 만든다
//   2) 유니티 창을 클릭해서 포커스를 준다 -> 컴파일 -> 리로드 -> 자동 실행
//
// Temp/ 는 유니티가 관리하는 임시 폴더라 에셋으로 잡히지 않는다.
//
// 경고 — 이건 씬을 처음 세울 때만 쓰는 물건이다
//   SetupYeouido63.Setup() 은 빈 씬을 만들어 Yeouido63.unity 에 덮어쓴다.
//   지금 씬에는 잔디 8 배치, 먼지 3 층, 카메라 설정, 볼륨이 들어 있고
//   그건 전부 사라진다. 플래그 파일 하나가 그 방아쇠다.
//
//   Setup() 쪽에 확인 대화상자를 넣어 뒀지만, 그건 사람이 보고 있을 때만
//   막아 준다. 배치 모드(-batchmode)나 대화상자를 못 띄우는 상황에서는
//   그냥 진행된다. 이 플래그는 새 씬을 만들 의도가 있을 때만 만들어라.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class Yeouido63AutoRun
{
    const string Flag = "Temp/yeouido63_run.flag";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        // 컴파일 직후엔 에셋 DB 가 아직 정리 중이라 한 프레임 미룬다
        EditorApplication.delayCall += () =>
        {
            try
            {
                File.Delete(Flag);      // 먼저 지워서 실패해도 무한 반복은 막는다
                Debug.Log("[Yeouido63] 플래그 감지 -> 씬 셋업 자동 실행");
                SetupYeouido63.Setup();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Yeouido63] 자동 실행 실패: " + e);
            }
        };
    }

    [MenuItem("Tools/Yeouido 63/자동 실행 플래그 만들기")]
    static void MakeFlag()
    {
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Flag, "run");
        Debug.Log("[Yeouido63] 플래그 생성: " + Flag);
    }
}
