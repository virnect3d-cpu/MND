// 구름 레이어 핸들을 씬에 심는다
//
// 메뉴: Tools/Yeouido 63/구름 레이어 오브젝트 만들기
//
// 구름은 Volume 프로파일 오버라이드라 하이어라키에 아무것도 없다.
// 이 스크립트가 CLOUD_Layer 오브젝트를 만들어 프로파일에 연결하고,
// 프로파일의 현재 값을 그대로 끌어와 초기 상태를 맞춘다.
// (임의의 기본값으로 덮어쓰면 지금 보이는 하늘이 바뀌어 버린다.)

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class SetupCloudHandle
{
    const string Flag     = "Temp/yeouido63_cloudhandle.flag";
    const string PostPath = "Assets/yeouido63/Scenes/Yeouido63_Post.asset";
    const string ObjName  = "CLOUD_Layer";

    [DidReloadScripts]
    static void OnReload()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.delayCall += () =>
        {
            try { File.Delete(Flag); Run(); }
            catch (System.Exception e) { Debug.LogError("[구름] 실패: " + e); }
        };
    }

    [MenuItem("Tools/Yeouido 63/구름 레이어 오브젝트 만들기")]
    public static void Run()
    {
        var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
        if (post == null)
        {
            Debug.LogError("[구름] 포스트 프로파일을 못 찾았다: " + PostPath);
            return;
        }

        var go = GameObject.Find(ObjName);
        if (go == null)
        {
            go = new GameObject(ObjName);
            Undo.RegisterCreatedObjectUndo(go, "Create Cloud Layer");
        }

        var h = go.GetComponent<CloudLayerHandle>();
        if (h == null) h = Undo.AddComponent<CloudLayerHandle>(go);

        h.profile = post;

        // 프로파일이 원본이다. 현재 값을 먼저 읽어 와 위치/두께/속도를
        // 맞춘 뒤에야 다시 쓴다. 순서를 뒤집으면 컴포넌트 기본값이
        // 프로파일을 덮어써서 하늘이 통째로 바뀐다.
        h.Pull();

        // 회전 연동은 꺼 둔다. 방위는 SyncWind 가 바람 각도에서 계산해
        // 쓰고 있어서, 둘 다 켜면 마지막에 쓴 쪽이 이긴다.
        h.driveOrientation = false;

        h.Apply();

        EditorUtility.SetDirty(h);
        EditorUtility.SetDirty(post);
        AssetDatabase.SaveAssets();

        var scene = go.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var p = go.transform.position;
        Debug.Log($"[구름] '{ObjName}' 준비됐다. " +
                  $"고도 {p.y:F0}~{p.y + h.thickness:F0} m, " +
                  $"오프셋 ({p.x:F0},{p.z:F0}), 짙기 {h.density:F2}, 속도 {h.speed:F1}. " +
                  $"하이어라키에서 끌어 움직이면 된다.");
    }
}
