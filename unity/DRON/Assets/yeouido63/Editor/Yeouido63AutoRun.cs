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
