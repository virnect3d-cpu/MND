// 하늘 자전을 씬에 붙인다
//
// 메뉴: Tools/Yeouido 63/하늘 자전 켜기
//
// 왜 이렇게 하나
//   구름 globalSpeed 를 7.5 까지 올려도 움직임이 잘 안 보인다. 구름이
//   고도 250 m 에 수 km 밖까지 뻗어 있어 같은 속도라도 화면에서 변하는
//   각도가 아주 작기 때문이다.
//
//   배경 HDRI 는 화면 전체를 덮으니 조금만 돌아도 눈에 들어온다.
//   그쪽을 살살 돌리는 편이 훨씬 효율적이다.

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class SetupSkyRotation
{
    const string Flag = "Temp/yeouido63_skyrot.flag";
    const string HostName = "SKY_Rotator";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[하늘] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/하늘 자전 켜기")]
    public static void Run()
    {
        var sky = RenderSettings.skybox;
        if (sky == null) { Debug.LogError("[하늘] 씬에 스카이박스가 없다."); return; }
        if (!sky.HasProperty("_Rotation"))
        {
            Debug.LogError($"[하늘] '{sky.name}' 에 _Rotation 이 없다. " +
                           "Skybox/Panoramic 이나 Cubemap 셰이더여야 한다.");
            return;
        }

        var host = GameObject.Find(HostName);
        if (host == null)
        {
            host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "하늘 자전");
        }

        var rot = host.GetComponent<SkyboxRotator>() ?? host.AddComponent<SkyboxRotator>();
        rot.degreesPerSecond = 0.35f;
        rot.rotateInEditMode = true;
        EditorUtility.SetDirty(host);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        Debug.Log($"[하늘] '{sky.name}' 자전 켬. {rot.degreesPerSecond} 도/초 " +
                  $"(한 바퀴 {360f / rot.degreesPerSecond / 60f:F1} 분).");
    }

    [MenuItem("Tools/Yeouido 63/하늘 자전 끄기")]
    public static void Stop()
    {
        var host = GameObject.Find(HostName);
        if (host != null) Undo.DestroyObjectImmediate(host);
        Debug.Log("[하늘] 자전 껐다.");
    }
}
