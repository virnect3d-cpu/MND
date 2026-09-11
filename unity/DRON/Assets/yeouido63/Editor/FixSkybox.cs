// 날아간 스카이박스를 되돌린다
//
// 메뉴: Tools/Yeouido 63/스카이박스 복구
//
// 왜 필요했나
//   SkyboxRotator 초판이 원본 머티리얼을 복제해 HideAndDontSave 인스턴스로
//   갈아끼웠다. 그 인스턴스는 스크립트 리로드 때 파괴되는데,
//   RenderSettings.skybox 는 파괴된 것을 계속 가리켜 null 이 된다.
//   그대로 씬이 저장되면서 m_SkyboxMaterial: {fileID: 0} 이 디스크에 박혔다.
//
//   로테이터는 원본을 직접 돌리는 방식으로 고쳤다. 이 스크립트는 이미
//   비어 버린 참조를 되돌리는 용도다.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class FixSkybox
{
    const string Flag    = "Temp/yeouido63_fixsky.flag";
    const string SkyPath = "Assets/yeouido63/Runtime/HDRI/Sky_Yeouido.mat";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[하늘] 복구 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/스카이박스 복구")]
    public static void Run()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null)
        {
            Debug.LogError("[하늘] 원본 머티리얼을 못 찾았다: " + SkyPath);
            return;
        }

        RenderSettings.skybox = sky;

        // 환경광이 스카이박스에서 오므로 같이 갱신한다. 안 하면 하늘은
        // 돌아왔는데 씬 전체가 어두운 채로 남는다.
        DynamicGI.UpdateEnvironment();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        Debug.Log($"[하늘] 스카이박스를 '{sky.name}' 로 복구했다. " +
                  $"_Rotation={sky.GetFloat("_Rotation"):F1}");
    }
}

