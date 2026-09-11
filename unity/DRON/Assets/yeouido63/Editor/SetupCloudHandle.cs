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

    // 구름 덩어리가 흘러가는 속도(m/s).
    //
    //   처음에 12 로 뒀다. "구름은 멀리 있으니 빨라도 된다" 는 계산이었는데
    //   반대였다 — shapeOffset 은 카메라 거리와 무관하게 무늬를 미는 값이라
    //   원근 감쇠가 없다. 12 면 하늘 전체가 눈에 띄게 쓸려 간다.
    //
    //   2.5 로 내린다. 5 분에 750 m 라 보고 있으면 느리게 흐르는 게
    //   느껴지되 시선을 끌지는 않는다.
    const float DriftSpeed  = 2.5f;

    // 제자리에서 굴러가는 속도. 7.5 -> 2.5.
    const float GlobalSpeed = 2.5f;

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

        // 드리프트 — 구름 덩어리를 X 축으로 흘려보낸다.
        //
        //   globalSpeed 를 아무리 올려도 구름은 제자리에서 뭉개지기만 한다.
        //   판 전체를 미는 건 shapeOffset 뿐이라 거기에 속도를 준다.
        h.driftSpeed = DriftSpeed;
        h.driftDirection = new Vector2(1f, 0f);   // +X
        h.driftInEditMode = true;

        // 굴러가는 속도는 낮춘다. 7.5 로 두면 모양이 워낙 빨리 변해서
        // 어느 쪽으로 흐르는지가 묻힌다. 2.5 면 모양은 거의 유지되고
        // 이동 방향이 또렷하게 읽힌다.
        h.speed = GlobalSpeed;

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
                  $"기준 오프셋 ({p.x:F0},{p.z:F0}), 짙기 {h.density:F2}. " +
                  $"드리프트 {h.driftSpeed:F1} m/s -> " +
                  $"({h.driftDirection.x:F0},{h.driftDirection.y:F0}), " +
                  $"굴러가는 속도 {h.speed:F1}.");
    }
}
