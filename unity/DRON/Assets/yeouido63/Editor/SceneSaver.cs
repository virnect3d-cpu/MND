// 씬 저장을 한 곳으로 모은다
//
// 왜 필요한가
//   EditorSceneManager.SaveScene 은 플레이 모드에서 부르면
//   InvalidOperationException 을 던진다. 이 프로젝트의 셋업 스크립트들은
//   전부 마지막에 씬을 저장하는데, 플레이 중에 돌리면 거기서 터진다.
//   실제로 여러 번 겪었다 — 작업은 다 해 놓고 저장에서 예외가 나면
//   "실패" 로 보이지만 값은 메모리에 들어가 있어서 혼란스럽다.
//
//   아홉 군데에 같은 if 문을 흩뿌리는 대신 여기로 모은다. 저장 못 했으면
//   그 사실을 경고로 남겨서, 플레이에서 나온 뒤 다시 돌려야 한다는 걸
//   알 수 있게 한다.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneSaver
{
    /// <summary>플레이 중이 아니면 씬을 저장한다. 저장했으면 true.</summary>
    public static bool Save(Scene scene, string who)
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"[{who}] 플레이 중이라 씬을 저장 못 했다. " +
                             "값은 메모리에 들어갔으니 화면으로는 보이지만, " +
                             "플레이에서 나와 다시 돌려야 디스크에 남는다.");
            return false;
        }

        if (!scene.IsValid())
        {
            Debug.LogError($"[{who}] 저장할 씬이 유효하지 않다.");
            return false;
        }

        EditorSceneManager.SaveScene(scene);
        return true;
    }

    /// <summary>경로를 지정해 씬을 저장한다. 새 씬을 처음 디스크에 쓸 때 쓴다.</summary>
    public static bool Save(Scene scene, string path, string who)
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"[{who}] 플레이 중이라 씬을 저장 못 했다: {path}");
            return false;
        }

        if (!scene.IsValid())
        {
            Debug.LogError($"[{who}] 저장할 씬이 유효하지 않다.");
            return false;
        }

        EditorSceneManager.SaveScene(scene, path);
        return true;
    }

    /// <summary>현재 활성 씬을 저장한다.</summary>
    public static bool Save(string who) => Save(EditorSceneManager.GetActiveScene(), who);
}
